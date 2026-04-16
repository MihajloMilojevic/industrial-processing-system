using IndustrialProcessingSystem.Core.Events;
using IndustrialProcessingSystem.Core.Models;

namespace IndustrialProcessingSystem.Core.Interfaces;

public interface IProcessingSystem
{
    event EventHandler<JobCompletedEventArgs> JobCompleted;
    event EventHandler<JobFailedEventArgs> JobFailed;

    JobHandle Submit(Job job);
    IEnumerable<Job> GetTopJobs(int n);
    Job? GetJob(Guid id);
}
