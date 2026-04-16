using IndustrialProcessingSystem.Core.Enums;
using IndustrialProcessingSystem.Core.Models;
using IndustrialProcessingSystem.Services.Processors;
using Xunit;

namespace IndustrialProcessingSystem.Tests;

public class JobProcessorTests
{
    // -------------------------------------------------------------------------
    // PrimeJobProcessor
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(10,    4)]   // 2,3,5,7
    [InlineData(20,    8)]   // 2,3,5,7,11,13,17,19
    [InlineData(1,     0)]   // no primes
    [InlineData(2,     1)]   // just 2
    public async Task Prime_ReturnsCorrectCount(int limit, int expected)
    {
        var job = MakePrimeJob($"numbers:{limit},threads:1");
        var processor = new PrimeJobProcessor();

        var result = await processor.ProcessAsync(job, CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task Prime_MultipleThreadsProduceSameResult()
    {
        var payload   = "numbers:10_000,threads:1";
        var payloadMt = "numbers:10_000,threads:4";

        var single = await new PrimeJobProcessor().ProcessAsync(
            MakePrimeJob(payload), CancellationToken.None);
        var multi  = await new PrimeJobProcessor().ProcessAsync(
            MakePrimeJob(payloadMt), CancellationToken.None);

        Assert.Equal(single, multi);
    }

    [Theory]
    [InlineData("numbers:10,threads:0",  1)]   // clamped to min 1
    [InlineData("numbers:10,threads:99", 8)]   // clamped to max 8
    public void Prime_GetRequiredSlots_ClampsThreads(string payload, int expected)
    {
        var processor = new PrimeJobProcessor();
        var slots     = processor.GetRequiredSlots(MakePrimeJob(payload));
        Assert.Equal(expected, slots);
    }

    [Fact]
    public async Task Prime_CancellationToken_ThrowsOnCancel()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var job = MakePrimeJob("numbers:1_000_000,threads:1");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new PrimeJobProcessor().ProcessAsync(job, cts.Token));
    }

    // -------------------------------------------------------------------------
    // IoJobProcessor
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Io_ReturnsValueBetween0And100()
    {
        var job    = MakeIoJob("delay:10");
        var result = await new IoJobProcessor().ProcessAsync(job, CancellationToken.None);

        Assert.InRange(result, 0, 100);
    }

    [Fact]
    public void Io_GetRequiredSlots_AlwaysOne()
    {
        var slots = new IoJobProcessor().GetRequiredSlots(MakeIoJob("delay:0"));
        Assert.Equal(1, slots);
    }

    [Fact]
    public async Task Io_CancellationToken_ThrowsOnCancel()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var job = MakeIoJob("delay:60_000");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new IoJobProcessor().ProcessAsync(job, cts.Token));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Job MakePrimeJob(string payload) => new()
    {
        Type = JobType.Prime, Payload = payload, Priority = 1
    };

    private static Job MakeIoJob(string payload) => new()
    {
        Type = JobType.IO, Payload = payload, Priority = 1
    };
}
