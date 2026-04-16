using System.Threading.Channels;
using IndustrialProcessingSystem.Core.Events;

namespace IndustrialProcessingSystem.Services.Logging;

public sealed class JobLogger : IAsyncDisposable
{
    private readonly Channel<string> _channel;
    private readonly Task _writerTask;
    private readonly string _logPath;

    public JobLogger(string logPath)
    {
        _logPath = logPath;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        _writerTask = Task.Run(ConsumeLoopAsync);
    }

    public void LogCompleted(object? sender, JobCompletedEventArgs e)
    {
        var line = $"[{e.CompletedAt:yyyy-MM-dd HH:mm:ss}] [COMPLETED] {e.JobId}, {e.Result}";
        _channel.Writer.TryWrite(line);
    }

    public void LogFailed(object? sender, JobFailedEventArgs e)
    {
        var line = $"[{e.FailedAt:yyyy-MM-dd HH:mm:ss}] [FAILED]    {e.JobId}, attempt {e.AttemptNumber}";
        _channel.Writer.TryWrite(line);
    }

    public void LogAbort(Guid jobId)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ABORT]     {jobId}";
        _channel.Writer.TryWrite(line);
    }

    private async Task ConsumeLoopAsync()
    {
        await using var writer = new StreamWriter(_logPath, append: true) { AutoFlush = true };

        await foreach (var line in _channel.Reader.ReadAllAsync())
        {
            try
            {
                await writer.WriteLineAsync(line);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[JobLogger] Failed to write log entry: {ex.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.Complete();
        await _writerTask;
    }
}
