using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PCMonitor.Service.Alerts;
using PCMonitor.Service.Models;
using PCMonitor.Service.Notifications;
using Xunit;

namespace PCMonitor.Service.Tests.Alerts;

public sealed class AlertEvaluatorTests : IDisposable
{
    private readonly DateTimeOffset _start = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly string _rulesPath = Path.Combine(Path.GetTempPath(), $"pcmonitor-rules-{Guid.NewGuid():N}.json");

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
    public void UnusedSystemFanHeadersNeverRaiseAlerts()
    {
        var evaluator = Create(out var store);
        var fan = new SensorReading("system-fan-1", "Nuvoton", "System Fan #1", "Fan", 0, "RPM");
        evaluator.Process(Snapshot(_start, 75, fan));
        evaluator.Process(Snapshot(_start.AddSeconds(30), 75, fan));
        Assert.Empty(store.Query(null, null).Alerts);
        Assert.DoesNotContain(evaluator.GetStatus(Snapshot(_start.AddSeconds(31), 75, fan)).Sensors,
            sensor => sensor.Category == "fan");
    }

    [Fact]
    public void FanFailureNotifiesOnceUntilRpmActuallyRecovers()
    {
        var evaluator = Create(out var store);
        var stopped = new SensorReading("fan", "Nuvoton", "CPU Fan", "Fan", 0, "RPM");
        evaluator.Process(Snapshot(_start, 75, stopped));
        evaluator.Process(Snapshot(_start.AddSeconds(15), 75, stopped));
        evaluator.Process(Snapshot(_start.AddSeconds(30), 60, stopped));
        evaluator.Process(Snapshot(_start.AddSeconds(45), 75, stopped));
        evaluator.Process(Snapshot(_start.AddSeconds(60), 75, stopped));
        Assert.Single(store.Query(null, null).Alerts);

        var recovered = stopped with { Value = 800 };
        evaluator.Process(Snapshot(_start.AddSeconds(61), 75, recovered));
        evaluator.Process(Snapshot(_start.AddSeconds(62), 75, stopped));
        evaluator.Process(Snapshot(_start.AddSeconds(77), 75, stopped));
        Assert.Equal(2, store.Query(null, null).Alerts.Count);
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

    [Fact]
    public void CustomRuleRaisesOnceAndCanSuppressPushNotification()
    {
        var dispatcher = new RecordingNotificationDispatcher();
        var evaluator = Create(out var store, out var rules, dispatcher);
        rules.Create(new CustomAlertRuleRequest("SSD running hot", "ssd-temp", AlertRuleDirection.Above,
            70, 65, 10, AlertSeverity.Warning, true, false));
        var sensor = new SensorReading("ssd-temp", "NVMe SSD", "Drive Temperature", "Temperature", 72, "°C");
        evaluator.Process(Snapshot(_start, 60, sensor));
        evaluator.Process(Snapshot(_start.AddSeconds(10), 60, sensor));
        evaluator.Process(Snapshot(_start.AddSeconds(20), 60, sensor));

        var alert = Assert.Single(store.Query(null, null).Alerts);
        Assert.Contains("SSD running hot", alert.SensorName);
        Assert.Empty(dispatcher.Alerts);
    }

    [Fact]
    public void DuplicatePerCoreTemperaturesDoNotCreateBuiltInAlerts()
    {
        var evaluator = Create(out var store);
        var core = new SensorReading("core-1", "Intel Core i7", "CPU Core 1 Temperature", "Temperature", 99, "°C");
        evaluator.Process(Snapshot(_start, 70, core));
        evaluator.Process(Snapshot(_start.AddSeconds(10), 70, core));
        Assert.Empty(store.Query(null, null).Alerts);
    }

    private AlertEvaluator Create(out AlertStore store)
    {
        return Create(out store, out _, new NullNotificationDispatcher());
    }

    private AlertEvaluator Create(out AlertStore store, out CustomAlertRuleStore rules,
        INotificationDispatcher notifications)
    {
        var options = Options.Create(new AlertOptions { RuleStoreFile = _rulesPath });
        store = new AlertStore(options, new FixedTimeProvider(_start), new LiveEventHub());
        rules = new CustomAlertRuleStore(options, NullLogger<CustomAlertRuleStore>.Instance);
        return new AlertEvaluator(options, store, notifications, rules,
            NullLogger<AlertEvaluator>.Instance);
    }

    public void Dispose()
    {
        if (File.Exists(_rulesPath)) File.Delete(_rulesPath);
        if (File.Exists(_rulesPath + ".tmp")) File.Delete(_rulesPath + ".tmp");
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
    private sealed class RecordingNotificationDispatcher : INotificationDispatcher
    {
        public List<MonitorAlert> Alerts { get; } = [];
        public void Enqueue(MonitorAlert alert) => Alerts.Add(alert);
    }
}
