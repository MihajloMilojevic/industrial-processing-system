namespace IndustrialProcessingSystem.Core.Events;

public class JobCompletedEventArgs : EventArgs
{
    public Guid JobId { get; init; }
    public int Result { get; init; }
    public DateTime CompletedAt { get; init; } = DateTime.Now;
}

public class JobFailedEventArgs : EventArgs
{
    public Guid JobId { get; init; }
    public int AttemptNumber { get; init; }
    public Exception? Exception { get; init; }
    public DateTime FailedAt { get; init; } = DateTime.Now;
}
