using System.Collections.Concurrent;
using IndustrialProcessingSystem.Core.Events;
using IndustrialProcessingSystem.Core.Interfaces;
using IndustrialProcessingSystem.Core.Models;
using IndustrialProcessingSystem.Services.Collections;
using IndustrialProcessingSystem.Services.Configuration;
using IndustrialProcessingSystem.Services.Processors;
using IndustrialProcessingSystem.Services.Reporting;

namespace IndustrialProcessingSystem.Services;

public sealed class ProcessingSystem : IProcessingSystem, IDisposable
{
    // --- Events ---
    public event EventHandler<JobCompletedEventArgs>? JobCompleted;
    public event EventHandler<JobFailedEventArgs>?    JobFailed;

    // --- Config ---
    private readonly SystemConfig _config;

    // --- Queue & tracking ---
    private readonly JobPriorityQueue                      _queue   = new();
    private readonly ConcurrentDictionary<Guid, JobHandle> _handles = new();
    private readonly ConcurrentDictionary<Guid, bool>      _executedIds = new();

    // --- Slot management ---
    // Only the scheduler thread decrements _freeSlots.
    // Completions increment via Interlocked — no race.
    private int _freeSlots;

    // --- Scheduler signalling ---
    // Released when a job is submitted or slots are freed, so the scheduler wakes up promptly.
    private readonly SemaphoreSlim _schedulerSignal = new(0, 1);

    // --- Lifecycle ---
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _schedulerTask;
    private bool _disposed;

    // --- Reporting ---
    private readonly ReportGenerator? _reportGenerator;

    public ProcessingSystem(SystemConfig config, ReportGenerator? reportGenerator = null)
    {
        _config          = config;
        _freeSlots       = config.WorkerCount;
        _reportGenerator = reportGenerator;
        _schedulerTask   = Task.Run(SchedulerLoopAsync);
    }

    // -------------------------------------------------------------------------
    // IProcessingSystem
    // -------------------------------------------------------------------------

    public JobHandle Submit(Job job)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Idempotency — already executed or queued
        if (_handles.TryGetValue(job.Id, out var existing))
            return existing;

        // MaxQueueSize
        if (_queue.Count >= _config.MaxQueueSize)
            throw new InvalidOperationException(
                $"Queue is full (max {_config.MaxQueueSize}). Job {job.Id} rejected.");

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = new JobHandle { Id = job.Id, Result = tcs.Task };

        var entry = new JobEntry
        {
            Job      = job,
            Tcs      = tcs,
            Deadline = DateTime.UtcNow.Add(_config.JobTimeout)
        };

        _handles[job.Id] = handle;
        _queue.TryEnqueue(entry);
        SignalScheduler();

        return handle;
    }

    public IEnumerable<Job> GetTopJobs(int n) => _queue.GetTopJobs(n);

    public Job? GetJob(Guid id)
    {
        var entry = _queue.FindById(id);
        if (entry is not null) return entry.Job;

        // job was executed, return the handle that holds the original Job
        return _handles.TryGetValue(id, out _)
            ? _queue.FindById(id)?.Job   // may still be in queue
            : null;
    }

    // -------------------------------------------------------------------------
    // Scheduler
    // -------------------------------------------------------------------------

    private async Task SchedulerLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                // Wait for signal or periodic fallback check (100ms)
                await _schedulerSignal.WaitAsync(TimeSpan.FromMilliseconds(100), _cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // _cts was cancelled — exit loop cleanly
                break;
            }

            if (_cts.IsCancellationRequested) break;

            ProcessQueue();
        }
    }

    private void ProcessQueue()
    {
        var snapshot = _queue.GetSnapshot();

        foreach (var entry in snapshot)
        {
            // Check deadline
            if (DateTime.UtcNow >= entry.Deadline)
            {
                _queue.TryRemove(entry);
                HandleFailed(entry, timedOut: true);
                continue;
            }

            var slotsNeeded = JobProcessorFactory.Resolve(entry.Job.Type).GetRequiredSlots(entry.Job);
            var canRun      = _freeSlots >= slotsNeeded;

            if (_config.StrictPriority)
            {
                // Strict mode: do not skip, wait for the first job in queue
                if (!canRun) break;
            }
            else
            {
                // Threshold mode: skip if not waited long enough
                var waited   = DateTime.UtcNow - entry.EnqueuedAt;
                var mustRun  = waited >= _config.SkipThreshold;

                if (!canRun && !mustRun) continue;
                if (!canRun)            break; // mustRun but no free slots — wait
            }

            // Dispatch
            _freeSlots -= slotsNeeded;
            _queue.TryRemove(entry);
            _executedIds[entry.Job.Id] = true;

            DispatchJob(entry, slotsNeeded);
        }
    }

    private void DispatchJob(JobEntry entry, int slotsNeeded)
    {
        var processor = JobProcessorFactory.Resolve(entry.Job.Type);
        var remaining = entry.Deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            ReleaseSlots(slotsNeeded);
            HandleFailed(entry, timedOut: true);
            return;
        }

        // Do not use 'using' — CTS must live until ContinueWith completes.
        // Dispose is called explicitly inside ContinueWith.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        cts.CancelAfter(remaining);

        var startedAt = DateTime.UtcNow;

        _ = processor.ProcessAsync(entry.Job, cts.Token).ContinueWith(task =>
        {
            cts.Dispose();
            ReleaseSlots(slotsNeeded);
            var duration = (DateTime.UtcNow - startedAt).TotalMilliseconds;

            if (task.IsCompletedSuccessfully)
            {
                _reportGenerator?.RecordCompleted(entry.Job.Id, entry.Job.Type, duration);
                entry.Tcs.TrySetResult(task.Result);
                JobCompleted?.Invoke(this, new JobCompletedEventArgs
                {
                    JobId       = entry.Job.Id,
                    Result      = task.Result,
                    CompletedAt = DateTime.Now
                });
            }
            else
            {
                _reportGenerator?.RecordFailed(entry.Job.Id, entry.Job.Type, duration);
                HandleFailed(entry, timedOut: false, task.Exception?.InnerException);
            }
        }, TaskScheduler.Default);
    }

    // -------------------------------------------------------------------------
    // Retry / fail handling
    // -------------------------------------------------------------------------

    private void HandleFailed(JobEntry entry, bool timedOut, Exception? ex = null)
    {
        JobFailed?.Invoke(this, new JobFailedEventArgs
        {
            JobId         = entry.Job.Id,
            AttemptNumber = entry.RetryCount + 1,
            Exception     = ex,
            FailedAt      = DateTime.Now
        });

        if (entry.RetryCount < 2)
        {
            var retry = new JobEntry
            {
                Job        = entry.Job,
                Tcs        = entry.Tcs,
                Deadline   = DateTime.UtcNow.Add(_config.JobTimeout),
                RetryCount = entry.RetryCount + 1
            };

            // Remove from executedIds so the job can re-enter the queue
            _executedIds.TryRemove(entry.Job.Id, out _);
            _queue.TryEnqueue(retry);
            SignalScheduler();
        }
        else
        {
            // Third failure — ABORT
            OnAbort(entry);
        }
    }

    private void OnAbort(JobEntry entry)
    {
        // Logging is handled via event subscription in Program.cs
        JobFailed?.Invoke(this, new JobFailedEventArgs
        {
            JobId         = entry.Job.Id,
            AttemptNumber = -1,   // -1 signals ABORT
            FailedAt      = DateTime.Now
        });

        entry.Tcs.TrySetCanceled();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void ReleaseSlots(int count)
    {
        Interlocked.Add(ref _freeSlots, count);
        SignalScheduler();
    }

    private void SignalScheduler()
    {
        // SemaphoreSlim(0,1) — if a signal is already pending (CurrentCount==1),
        // Release would throw SemaphoreFullException, so we swallow it
        try { _schedulerSignal.Release(1); }
        catch (SemaphoreFullException) { }
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        try { _schedulerTask.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException ex)
            when (ex.InnerExceptions.All(e => e is TaskCanceledException or OperationCanceledException)) { }

        _queue.Dispose();
        _schedulerSignal.Dispose();
        _cts.Dispose();
    }
}
