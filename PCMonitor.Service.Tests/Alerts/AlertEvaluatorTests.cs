using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PCMonitor.Service.Alerts;
using PCMonitor.Service.Models;
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

    private AlertEvaluator Create(out AlertStore store)
    {
        var options = Options.Create(new AlertOptions());
        store = new AlertStore(options, new FixedTimeProvider(_start), new LiveEventHub());
        return new AlertEvaluator(options, store, NullLogger<AlertEvaluator>.Instance);
    }

    private static SensorSnapshot Snapshot(DateTimeOffset timestamp, float value) => new(timestamp,
    [new SensorReading("cpu-temp", "CPU", "CPU Package", "Temperature", value, "°C")]);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
