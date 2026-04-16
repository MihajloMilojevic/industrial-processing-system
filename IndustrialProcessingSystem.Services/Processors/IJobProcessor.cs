using IndustrialProcessingSystem.Core.Models;

namespace IndustrialProcessingSystem.Services.Processors;

public interface IJobProcessor
{
    /// <summary>
    /// Executes the job and returns an int result.
    /// CancellationToken carries the remaining time until the deadline.
    /// </summary>
    Task<int> ProcessAsync(Job job, CancellationToken cancellationToken);

    /// <summary>
    /// Number of slots from the global thread pool that this job requires.
    /// </summary>
    int GetRequiredSlots(Job job);
}
