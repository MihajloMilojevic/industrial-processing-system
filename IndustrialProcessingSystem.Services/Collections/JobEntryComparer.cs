namespace IndustrialProcessingSystem.Services.Collections;

internal class JobEntryComparer : IComparer<JobEntry>
{
    public static readonly JobEntryComparer Instance = new();

    private JobEntryComparer() { }

    public int Compare(JobEntry? x, JobEntry? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        // lower Priority value = higher priority = comes first
        int cmp = x.Job.Priority.CompareTo(y.Job.Priority);
        if (cmp != 0) return cmp;

        // same priority → older job goes first (FIFO within the same group)
        cmp = x.EnqueuedAt.CompareTo(y.EnqueuedAt);
        if (cmp != 0) return cmp;

        // SortedSet uses comparer for equality as well —
        // without this tiebreaker, jobs with the same priority and enqueue time
        // would be discarded as duplicates
        return x.Job.Id.CompareTo(y.Job.Id);
    }
}
