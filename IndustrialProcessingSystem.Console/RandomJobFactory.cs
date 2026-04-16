using IndustrialProcessingSystem.Core.Enums;
using IndustrialProcessingSystem.Core.Models;

namespace IndustrialProcessingSystem.Console;

internal static class RandomJobFactory
{
    private static readonly Random Rng = Random.Shared;

    private static readonly (JobType Type, string Payload)[] Templates =
    [
        (JobType.Prime, "numbers:5_000,threads:1"),
        (JobType.Prime, "numbers:10_000,threads:2"),
        (JobType.Prime, "numbers:20_000,threads:3"),
        (JobType.IO,    "delay:500"),
        (JobType.IO,    "delay:1_000"),
        (JobType.IO,    "delay:2_000"),
    ];

    public static Job Create()
    {
        var (type, payload) = Templates[Rng.Next(Templates.Length)];
        return new Job
        {
            Type     = type,
            Payload  = payload,
            Priority = Rng.Next(1, 4)
        };
    }
}
