using Microsoft.Extensions.Options;
using PCMonitor.Service.Models;
using PCMonitor.Service.Sensors;
using PCMonitor.Service.SessionDetection;
using PCMonitor.Service.History;
using PCMonitor.Service.Alerts;

namespace PCMonitor.Service.Services;

public sealed class SensorMonitoringService(
    ISensorProvider sensorProvider,
    SensorSnapshotStore snapshots,
    LoadSessionDetector sessionDetector,
    HistoricalSensorAggregator historicalAggregator,
    AlertEvaluator alertEvaluator,
    LiveEventHub liveEvents,
    IOptions<MonitoringOptions> options,
    ILogger<SensorMonitoringService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMilliseconds = Math.Max(100, options.Value.PollingIntervalMilliseconds);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMilliseconds));
        logger.LogInformation("Sensor monitoring started with a {Interval} ms polling interval", intervalMilliseconds);

        Poll();
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Poll();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            historicalAggregator.Complete();
        }
    }

    private void Poll()
    {
        try
        {
            var readings = sensorProvider.GetSensorReadings();
            var snapshot = new SensorSnapshot(DateTimeOffset.UtcNow, readings);
            snapshots.Update(snapshot);
            liveEvents.Publish(new LiveEventEnvelope("sensors", snapshot));
            sessionDetector.Process(snapshot);
            historicalAggregator.Process(snapshot);
            alertEvaluator.Process(snapshot);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Sensor polling failed");
        }
    }
}
