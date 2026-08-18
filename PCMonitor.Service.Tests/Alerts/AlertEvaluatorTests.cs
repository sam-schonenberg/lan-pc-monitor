using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PCMonitor.Service.Alerts;
using PCMonitor.Service.Models;
using PCMonitor.Service.Notifications;
using Xunit;

namespace PCMonitor.Service.Tests.Alerts;

public sealed class AlertEvaluatorTests
{
    private readonly DateTimeOffset _start = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TransientSpikeDoesNotRaiseAlert()
    {
        var evaluator = Create(out var store);
        evaluator.Process(Snapshot(_start, 96));
        evaluator.Process(Snapshot(_start.AddSeconds(1), 70));
        Assert.Empty(store.Query(null, null).Alerts);
    }

    [Fact]
    public void SustainedThresholdRaisesOnceUntilReset()
    {
        var evaluator = Create(out var store);
        evaluator.Process(Snapshot(_start, 86));
        evaluator.Process(Snapshot(_start.AddSeconds(5), 86));
        evaluator.Process(Snapshot(_start.AddSeconds(6), 90));
        Assert.Single(store.Query(null, null).Alerts);

        evaluator.Process(Snapshot(_start.AddSeconds(7), 79));
        evaluator.Process(Snapshot(_start.AddSeconds(8), 86));
        evaluator.Process(Snapshot(_start.AddSeconds(13), 86));
        Assert.Equal(2, store.Query(null, null).Alerts.Count);
    }

    [Fact]
    public void WarningCanEscalateToCritical()
    {
        var evaluator = Create(out var store);
        evaluator.Process(Snapshot(_start, 86));
        evaluator.Process(Snapshot(_start.AddSeconds(5), 86));
        evaluator.Process(Snapshot(_start.AddSeconds(6), 96));
        evaluator.Process(Snapshot(_start.AddSeconds(11), 96));

        var alerts = store.Query(null, null).Alerts;
        Assert.Equal([AlertSeverity.Warning, AlertSeverity.Critical], alerts.Select(alert => alert.Severity));
    }

    [Fact]
    public void SustainedMemoryPressureRaisesAlert()
    {
        var evaluator = Create(out var store);
        evaluator.Process(Snapshot(_start, 60, new SensorReading("memory", "Memory", "Memory Usage", "Load", 98, "%")));
        evaluator.Process(Snapshot(_start.AddSeconds(30), 60,
            new SensorReading("memory", "Memory", "Memory Usage", "Load", 98, "%")));

        var alert = Assert.Single(store.Query(null, null).Alerts);
        Assert.Equal("memory", alert.SensorId);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
    }

    [Fact]
    public void StoppedFanOnlyAlertsWhenRelatedHardwareIsHot()
    {
        var evaluator = Create(out var store);
        evaluator.Process(Snapshot(_start, 60, new SensorReading("fan", "CPU", "CPU Fan", "Fan", 0, "RPM")));
        evaluator.Process(Snapshot(_start.AddSeconds(15), 60, new SensorReading("fan", "CPU", "CPU Fan", "Fan", 0, "RPM")));
        Assert.Empty(store.Query(null, null).Alerts);

        evaluator.Process(Snapshot(_start.AddSeconds(16), 75, new SensorReading("fan", "CPU", "CPU Fan", "Fan", 0, "RPM")));
        evaluator.Process(Snapshot(_start.AddSeconds(31), 75, new SensorReading("fan", "CPU", "CPU Fan", "Fan", 0, "RPM")));
        Assert.Equal("fan", Assert.Single(store.Query(null, null).Alerts).SensorId);
    }

    [Fact]
    public void StatusReportsProgressAndThresholds()
    {
        var evaluator = Create(out _);
        var status = evaluator.GetStatus(Snapshot(_start, 76));
        var temperature = Assert.Single(status.Sensors);
        Assert.Equal("temperature", temperature.Category);
        Assert.Equal(95, temperature.CriticalThreshold);
        Assert.Equal(.8, temperature.Progress, 3);
        Assert.Equal(19, temperature.DistanceToCritical);
    }

    private AlertEvaluator Create(out AlertStore store)
    {
        var options = Options.Create(new AlertOptions());
        store = new AlertStore(options, new FixedTimeProvider(_start), new LiveEventHub());
        return new AlertEvaluator(options, store, new NullNotificationDispatcher(),
            NullLogger<AlertEvaluator>.Instance);
    }

    private static SensorSnapshot Snapshot(DateTimeOffset timestamp, float value, params SensorReading[] additional) => new(timestamp,
        new SensorReading[] { new("cpu-temp", "CPU", "CPU Package", "Temperature", value, "°C") }.Concat(additional).ToArray());

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NullNotificationDispatcher : INotificationDispatcher
    {
        public void Enqueue(MonitorAlert alert) { }
    }
}
