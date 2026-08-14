using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace PCMonitor.Service.SessionDetection;

public sealed class ProcessMonitoringService(
    SessionRuntimeContext sessionContext,
    IOptions<ProcessMonitoringOptions> options,
    TimeProvider timeProvider,
    ILogger<ProcessMonitoringService> logger) : BackgroundService
{
    private readonly Dictionary<ProcessIdentity, TimeSpan> _previousCpuTimes = [];
    private Guid? _trackedSessionId;
    private DateTimeOffset? _previousSampleTime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(
            double.IsFinite(options.Value.SamplingIntervalSeconds) && options.Value.SamplingIntervalSeconds > 0
                ? options.Value.SamplingIntervalSeconds
                : 5);
        var topCount = options.Value.TopProcessCount > 0 ? options.Value.TopProcessCount : 3;
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    Sample(topCount);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Process CPU sampling failed; session detection and hardware monitoring continue");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void Sample(int topCount)
    {
        var runtime = sessionContext.GetSnapshot();
        if (runtime.State is not (LoadSessionState.Candidate or LoadSessionState.Active) ||
            runtime.SessionId is not { } sessionId)
        {
            Reset();
            return;
        }

        if (_trackedSessionId != sessionId)
        {
            Reset();
            _trackedSessionId = sessionId;
        }

        var now = timeProvider.GetUtcNow();
        var current = ReadProcessCpuTimes();
        if (_previousSampleTime is { } previousTime)
        {
            var elapsed = now - previousTime;
            var readings = current
                .Where(item => _previousCpuTimes.TryGetValue(item.Key, out _))
                .Select(item => new ProcessCpuReading(
                    item.Key.Name,
                    ProcessCpuCalculator.Calculate(
                        item.Value - _previousCpuTimes[item.Key], elapsed, Environment.ProcessorCount)))
                .Where(reading => reading.CpuPercent > 0 && double.IsFinite(reading.CpuPercent))
                .OrderByDescending(reading => reading.CpuPercent)
                .Take(topCount)
                .ToArray();

            sessionContext.RecordProcessSample(sessionId, now, readings);
        }

        _previousCpuTimes.Clear();
        foreach (var item in current) _previousCpuTimes[item.Key] = item.Value;
        _previousSampleTime = now;
    }

    private static Dictionary<ProcessIdentity, TimeSpan> ReadProcessCpuTimes()
    {
        var result = new Dictionary<ProcessIdentity, TimeSpan>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = NormalizeName(process.ProcessName);
                    var identity = new ProcessIdentity(process.Id, process.StartTime.ToUniversalTime().Ticks, name);
                    result[identity] = process.TotalProcessorTime;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or
                                                   NotSupportedException)
                {
                    // Processes commonly exit or deny inspection between enumeration and property access.
                }
            }
        }

        return result;
    }

    private static string NormalizeName(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.exe";

    private void Reset()
    {
        _trackedSessionId = null;
        _previousSampleTime = null;
        _previousCpuTimes.Clear();
    }

    private readonly record struct ProcessIdentity(int ProcessId, long StartTimeTicks, string Name);
}

public static class ProcessCpuCalculator
{
    public static double Calculate(TimeSpan processCpuDelta, TimeSpan elapsed, int logicalProcessorCount)
    {
        if (processCpuDelta < TimeSpan.Zero || elapsed <= TimeSpan.Zero || logicalProcessorCount <= 0)
        {
            return 0;
        }

        var percent = processCpuDelta.TotalMilliseconds / elapsed.TotalMilliseconds /
                      logicalProcessorCount * 100d;
        return Math.Clamp(percent, 0, 100);
    }
}
