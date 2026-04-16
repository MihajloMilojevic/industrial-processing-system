using IndustrialProcessingSystem.Core.Enums;

namespace IndustrialProcessingSystem.Services.Reporting;

internal sealed class CompletedJobRecord
{
    public Guid JobId { get; init; }
    public JobType Type { get; init; }
    public double DurationMs { get; init; }
    public bool Failed { get; init; }
}
