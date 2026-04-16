using IndustrialProcessingSystem.Core.Enums;
using IndustrialProcessingSystem.Core.Events;
using IndustrialProcessingSystem.Core.Models;
using IndustrialProcessingSystem.Services;
using Xunit;

namespace IndustrialProcessingSystem.Tests;

public sealed class ProcessingSystemTests : IDisposable
{
    private readonly ProcessingSystem _system = new(TestConfigFactory.Default());

    public void Dispose() => _system.Dispose();

    // -------------------------------------------------------------------------
    // Submit
    // -------------------------------------------------------------------------

    [Fact]
    public void Submit_ReturnsHandle_WithMatchingId()
    {
        var job    = MakeIoJob();
        var handle = _system.Submit(job);

        Assert.Equal(job.Id, handle.Id);
        Assert.NotNull(handle.Result);
    }

    [Fact]
    public async Task Submit_IoJob_CompletesWithValueInRange()
    {
        var handle = _system.Submit(MakeIoJob("delay:10"));
        var result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.InRange(result, 0, 100);
    }

    [Fact]
    public async Task Submit_PrimeJob_ReturnsCorrectCount()
    {
        var handle = _system.Submit(MakePrimeJob());
        var result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(4, result); // 2,3,5,7
    }

    // -------------------------------------------------------------------------
    // Idempotency
    // -------------------------------------------------------------------------

    [Fact]
    public void Submit_SameJob_ReturnsSameHandle()
    {
        var job     = MakeIoJob();
        var handle1 = _system.Submit(job);
        var handle2 = _system.Submit(job);

        Assert.Same(handle1, handle2);
    }

    [Fact]
    public async Task Submit_SameJob_ExecutedOnlyOnce()
    {
        var completedCount = 0;
        _system.JobCompleted += (_, _) => Interlocked.Increment(ref completedCount);

        var job = MakeIoJob();
        _system.Submit(job);
        _system.Submit(job);
        _system.Submit(job);

        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Equal(1, completedCount);
    }

    // -------------------------------------------------------------------------
    // MaxQueueSize
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Submit_WhenQueueFull_ThrowsInvalidOperationException()
    {
        using var system = new ProcessingSystem(
            TestConfigFactory.Default(maxQueueSize: 2, workerCount: 1, jobTimeoutSeconds: 60));

        // Submit a long-running job to occupy the single worker slot.
        // We must wait until the scheduler dispatches it (removes it from the queue)
        // before filling the queue — otherwise the race between Submit and dispatch
        // makes the queue count non-deterministic.
        system.Submit(MakeIoJob("delay:60_000"));

        // Poll until queue is empty — the job has been dispatched and the slot is taken.
        // Scheduler wakes up every 100ms at most, so this converges quickly.
        using var pollCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (system.GetTopJobs(1).Any())
            await Task.Delay(10, pollCts.Token);

        // Queue is empty, worker slot is full — new jobs queue up and cannot be dispatched.
        system.Submit(MakeIoJob("delay:100"));
        system.Submit(MakeIoJob("delay:100"));

        Assert.Throws<InvalidOperationException>(() => system.Submit(MakeIoJob("delay:100")));
    }

    // -------------------------------------------------------------------------
    // Priority
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Submit_StrictPriority_HighPriorityCompletesFirst()
    {
        using var system = new ProcessingSystem(
            TestConfigFactory.Default(workerCount: 1, strictPriority: true, jobTimeoutSeconds: 30));

        var completionOrder = new List<int>();

        var low  = new Job { Type = JobType.IO, Payload = "delay:50",  Priority = 3 };
        var high = new Job { Type = JobType.IO, Payload = "delay:50",  Priority = 1 };

        system.JobCompleted += (_, e) =>
        {
            if (e.JobId == high.Id) completionOrder.Add(1);
            if (e.JobId == low.Id)  completionOrder.Add(3);
        };

        // Submit low first, then high — high should still run first
        system.Submit(low);
        system.Submit(high);

        await Task.WhenAll(
            system.Submit(low).Result.WaitAsync(TimeSpan.FromSeconds(10)),
            system.Submit(high).Result.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(1, completionOrder.First());
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    [Fact]
    public async Task JobCompleted_IsFiredOnSuccess()
    {
        var tcs = new TaskCompletionSource<JobCompletedEventArgs>();
        _system.JobCompleted += (_, e) => tcs.TrySetResult(e);

        var job = MakeIoJob("delay:10");
        _system.Submit(job);

        var args = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(job.Id, args.JobId);
    }

    [Fact]
    public async Task JobFailed_IsFiredOnTimeout()
    {
        using var system = new ProcessingSystem(
            TestConfigFactory.Default(workerCount: 1, jobTimeoutSeconds: 0.1));

        var tcs = new TaskCompletionSource<JobFailedEventArgs>();
        system.JobFailed += (_, e) => tcs.TrySetResult(e);

        system.Submit(MakeIoJob("delay:60_000")); // will always time out

        var args = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(Guid.Empty, args.JobId);
    }

    // -------------------------------------------------------------------------
    // Retry & Abort
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Abort_AfterThreeFails_TaskIsCanceled()
    {
        using var system = new ProcessingSystem(
            TestConfigFactory.Default(workerCount: 1, jobTimeoutSeconds: 0.1));

        // IO job with delay far exceeding timeout — will always fail
        var handle = system.Submit(MakeIoJob("delay:60_000"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handle.Result.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Abort_FiresJobFailed_WithMinusOneAttempt()
    {
        using var system = new ProcessingSystem(
            TestConfigFactory.Default(workerCount: 1, jobTimeoutSeconds: 0.1));

        var abortSeen = new TaskCompletionSource<bool>();
        system.JobFailed += (_, e) =>
        {
            if (e.AttemptNumber == -1) abortSeen.TrySetResult(true);
        };

        system.Submit(MakeIoJob("delay:60_000"));

        var result = await abortSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // GetTopJobs / GetJob
    // -------------------------------------------------------------------------

    [Fact]
    public void GetTopJobs_ReturnsNJobsByPriority()
    {
        using var system = new ProcessingSystem(
            TestConfigFactory.Default(workerCount: 1, jobTimeoutSeconds: 60));

        // Fill queue — single worker busy with a long job
        system.Submit(MakeIoJob("delay:60_000"));

        var p3 = new Job { Type = JobType.IO, Payload = "delay:100", Priority = 3 };
        var p1 = new Job { Type = JobType.IO, Payload = "delay:100", Priority = 1 };
        var p2 = new Job { Type = JobType.IO, Payload = "delay:100", Priority = 2 };

        system.Submit(p3);
        system.Submit(p1);
        system.Submit(p2);

        var top2 = system.GetTopJobs(2).ToList();

        Assert.Equal(2, top2.Count);
        Assert.True(top2[0].Priority <= top2[1].Priority);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Job MakeIoJob(string payload = "delay:50") => new()
    {
        Type = JobType.IO, Payload = payload, Priority = 1
    };

    private static Job MakePrimeJob(string payload = "numbers:10,threads:1") => new()
    {
        Type = JobType.Prime, Payload = payload, Priority = 1
    };
}
