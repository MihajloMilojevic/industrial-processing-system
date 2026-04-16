using IndustrialProcessingSystem.Core.Models;

namespace IndustrialProcessingSystem.Services.Collections;

internal sealed class JobPriorityQueue : IDisposable
{
    private readonly SortedSet<JobEntry> _set = new(JobEntryComparer.Instance);
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _disposed;

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _set.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public bool TryEnqueue(JobEntry entry)
    {
        _lock.EnterWriteLock();
        try { return _set.Add(entry); }
        finally { _lock.ExitWriteLock(); }
    }

    public bool TryRemove(JobEntry entry)
    {
        _lock.EnterWriteLock();
        try { return _set.Remove(entry); }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Returns a snapshot of the current queue state.
    /// ReadLock is held only during the copy — caller iterates without holding the lock.
    /// </summary>
    public IReadOnlyList<JobEntry> GetSnapshot()
    {
        _lock.EnterReadLock();
        try { return _set.ToList(); }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Returns the top N jobs by priority from the currently active queue.
    /// </summary>
    public IEnumerable<Job> GetTopJobs(int n)
    {
        _lock.EnterReadLock();
        try { return _set.Take(n).Select(e => e.Job).ToList(); }
        finally { _lock.ExitReadLock(); }
    }

    public JobEntry? FindById(Guid id)
    {
        _lock.EnterReadLock();
        try { return _set.FirstOrDefault(e => e.Job.Id == id); }
        finally { _lock.ExitReadLock(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }
}
