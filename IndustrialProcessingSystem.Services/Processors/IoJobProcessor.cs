using IndustrialProcessingSystem.Core.Models;

namespace IndustrialProcessingSystem.Services.Processors;

public sealed class IoJobProcessor : IJobProcessor
{
    private static readonly Random Rng = Random.Shared;

    public int GetRequiredSlots(Job job) => 1;

    public async Task<int> ProcessAsync(Job job, CancellationToken cancellationToken)
    {
        var delay = ParsePayload(job.Payload);
        await Task.Delay(delay, cancellationToken);
        return Rng.Next(0, 101);
    }

    private static int ParsePayload(string payload)
    {
        // format: delay:1_000
        return int.Parse(payload.Split(':')[1].Replace("_", ""));
    }
}
