using IndustrialProcessingSystem.Console;
using IndustrialProcessingSystem.Services;
using IndustrialProcessingSystem.Services.Configuration;
using IndustrialProcessingSystem.Services.Logging;
using IndustrialProcessingSystem.Services.Reporting;

// --- Config ---
var config = XmlConfigReader.Read("SystemConfig.xml");

// --- Infrastructure ---
var logger  = new JobLogger(Path.Combine("logs", "jobs.log"));
var reports = new ReportGenerator(Path.Combine("reports"), TimeSpan.FromMinutes(1));

// --- System ---
using var system = new ProcessingSystem(config, reports);

// --- Event subscriptions ---
system.JobCompleted += logger.LogCompleted;
system.JobCompleted += (_, e) =>
    Console.WriteLine($"[COMPLETED] {e.JobId} → {e.Result}");

system.JobFailed += (sender, e) =>
{
    if (e.AttemptNumber == -1)
    {
        logger.LogAbort(e.JobId);
        Console.WriteLine($"[ABORT]     {e.JobId}");
    }
    else
    {
        logger.LogFailed(sender, e);
        Console.WriteLine($"[FAILED]    {e.JobId}, attempt {e.AttemptNumber}");
    }
};

// --- Initial jobs from config ---
foreach (var jobConfig in config.Jobs)
{
    try { system.Submit(jobConfig.ToJob()); }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"[Startup] Failed to submit initial job: {ex.Message}");
    }
}

// --- Producer threads ---
// Producers only submit jobs and move on — they do not await the result.
// Output is handled through JobCompleted/JobFailed events.
var cts       = new CancellationTokenSource();
var producers = Enumerable.Range(0, config.WorkerCount)
    .Select(i => Task.Run(async () =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var job = RandomJobFactory.Create();
                system.Submit(job);
                Console.WriteLine($"[Producer {i}] Submitted {job.Type} job, priority {job.Priority}");
            }
            catch (InvalidOperationException)
            {
                // Queue full — backoff
                await Task.Delay(200, cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Producer {i}] Error: {ex.Message}");
            }

            try
            {
                await Task.Delay(Random.Shared.Next(600, 1100), cts.Token);
            }
            catch (OperationCanceledException) { break; }
        }
    }, cts.Token))
    .ToList();

// --- Run until keypress ---
Console.WriteLine("System running. Press [Enter] to stop.");
Console.ReadLine();

cts.Cancel();

try { await Task.WhenAll(producers); }
catch (OperationCanceledException) { }

await logger.DisposeAsync();
reports.Dispose();

Console.WriteLine("System stopped.");
