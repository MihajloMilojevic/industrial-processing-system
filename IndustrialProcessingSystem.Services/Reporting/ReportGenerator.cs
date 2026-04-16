using System.Collections.Concurrent;
using System.Xml.Linq;
using IndustrialProcessingSystem.Core.Enums;

namespace IndustrialProcessingSystem.Services.Reporting;

public sealed class ReportGenerator : IDisposable
{
    private const int MaxReports = 10;

    private readonly ConcurrentBag<CompletedJobRecord> _records = new();
    private readonly System.Timers.Timer _timer;
    private readonly string _reportDirectory;
    private int _reportCounter;
    private bool _disposed;

    public ReportGenerator(string reportDirectory, TimeSpan interval)
    {
        _reportDirectory = reportDirectory;
        Directory.CreateDirectory(reportDirectory);

        _timer = new System.Timers.Timer(interval.TotalMilliseconds) { AutoReset = true };
        _timer.Elapsed += (_, _) => GenerateReport();
        _timer.Start();
    }

    public void RecordCompleted(Guid jobId, JobType type, double durationMs)
    {
        _records.Add(new CompletedJobRecord
        {
            JobId = jobId,
            Type = type,
            DurationMs = durationMs,
            Failed = false
        });
    }

    public void RecordFailed(Guid jobId, JobType type, double durationMs)
    {
        _records.Add(new CompletedJobRecord
        {
            JobId = jobId,
            Type = type,
            DurationMs = durationMs,
            Failed = true
        });
    }

    private void GenerateReport()
    {
        try
        {
            var snapshot = _records.ToArray();

            var groups = snapshot
                .GroupBy(r => r.Type)
                .OrderBy(g => g.Key.ToString())
                .Select(g => new
                {
                    Type          = g.Key,
                    Completed     = g.Count(r => !r.Failed),
                    Failed        = g.Count(r => r.Failed),
                    AvgDurationMs = g.Any() ? g.Average(r => r.DurationMs) : 0.0
                })
                .ToList();

            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("Report",
                    new XAttribute("GeneratedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                    new XAttribute("TotalJobs", snapshot.Length),
                    groups.Select(g =>
                        new XElement("JobType",
                            new XAttribute("Type",          g.Type),
                            new XAttribute("Completed",     g.Completed),
                            new XAttribute("Failed",        g.Failed),
                            new XAttribute("AvgDurationMs", Math.Round(g.AvgDurationMs, 2))
                        )
                    )
                )
            );

            var index = _reportCounter % MaxReports;
            var path = Path.Combine(_reportDirectory, $"report_{index}.xml");
            doc.Save(path);

            _reportCounter++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ReportGenerator] Failed to generate report: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }
}
