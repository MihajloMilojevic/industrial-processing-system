using IndustrialProcessingSystem.Core.Models;

namespace IndustrialProcessingSystem.Services.Processors;

public sealed class PrimeJobProcessor : IJobProcessor
{
    private const int MinThreads = 1;
    private const int MaxThreads = 8;

    public int GetRequiredSlots(Job job)
    {
        var (_, threads) = ParsePayload(job.Payload);
        return threads;
    }

    public Task<int> ProcessAsync(Job job, CancellationToken cancellationToken)
    {
        var (limit, threads) = ParsePayload(job.Payload);

        return Task.Run(() =>
        {
            int count = 0;

            Parallel.For(2, limit + 1, new ParallelOptions
            {
                MaxDegreeOfParallelism = threads,
                CancellationToken = cancellationToken
            },
            () => 0,
            (i, state, localCount) =>
            {
                if (IsPrime(i)) localCount++;
                return localCount;
            },
            localCount => Interlocked.Add(ref count, localCount));

            return count;
        }, cancellationToken);
    }

    private static (int limit, int threads) ParsePayload(string payload)
    {
        // format: numbers:10_000,threads:3
        var parts = payload.Split(',');
        var limit   = int.Parse(parts[0].Split(':')[1].Replace("_", ""));
        var threads = int.Parse(parts[1].Split(':')[1]);
        threads = Math.Clamp(threads, MinThreads, MaxThreads);
        return (limit, threads);
    }

    private static bool IsPrime(int n)
    {
        if (n < 2) return false;
        if (n == 2) return true;
        if (n % 2 == 0) return false;

        var boundary = (int)Math.Sqrt(n);
        for (int i = 3; i <= boundary; i += 2)
            if (n % i == 0) return false;

        return true;
    }
}
