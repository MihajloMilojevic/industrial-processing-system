using IndustrialProcessingSystem.Services.Configuration;

namespace IndustrialProcessingSystem.Tests;

internal static class TestConfigFactory
{
    /// <summary>
    /// Default test config — fast timeouts, no strict priority.
    /// </summary>
    public static SystemConfig Default(
        int workerCount            = 5,
        int maxQueueSize           = 100,
        double jobTimeoutSeconds   = 5.0,
        double skipThreshold       = 0.5,
        bool strictPriority        = false) => new()
    {
        WorkerCount            = workerCount,
        MaxQueueSize           = maxQueueSize,
        JobTimeoutSeconds      = jobTimeoutSeconds,
        PrioritySkipThreshold  = skipThreshold,
        StrictPriority         = strictPriority
    };
}
