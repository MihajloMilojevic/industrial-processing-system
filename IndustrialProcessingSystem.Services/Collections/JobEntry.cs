using IndustrialProcessingSystem.Core.Models;

namespace IndustrialProcessingSystem.Services.Collections;

internal class JobEntry
{
    public Job Job { get; init; } = null!;
    public TaskCompletionSource<int> Tcs { get; init; } = null!;
    public DateTime Deadline { get; init; }
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
    public int RetryCount { get; set; } = 0;
}
