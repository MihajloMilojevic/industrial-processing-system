using IndustrialProcessingSystem.Core.Enums;

namespace IndustrialProcessingSystem.Services.Processors;

public static class JobProcessorFactory
{
    private static readonly IJobProcessor PrimeProcessor = new PrimeJobProcessor();
    private static readonly IJobProcessor IoProcessor    = new IoJobProcessor();

    public static IJobProcessor Resolve(JobType type) => type switch
    {
        JobType.Prime => PrimeProcessor,
        JobType.IO    => IoProcessor,
        _             => throw new ArgumentOutOfRangeException(nameof(type), $"No processor for type: {type}")
    };
}
