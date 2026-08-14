using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PCMonitor.Service.History;
using PCMonitor.Service.Models;
using PCMonitor.Service.SessionDetection;
using Xunit;

namespace PCMonitor.Service.Tests.History;

public sealed class HistoricalMonitoringTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pcmonitor-tests-{Guid.NewGuid():N}");
    private readonly DateTimeOffset _now = new(2026, 8, 14, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void AggregatesValuesAndIgnoresNulls()
    {
        var (store, aggregator) = CreateSystem();
        var start = new DateTimeOffset(2026, 8, 14, 14, 20, 0, TimeSpan.Zero);

        aggregator.Process(Snapshot(start.AddSeconds(1), 10));
        aggregator.Process(Snapshot(start.AddSeconds(2), null));
        aggregator.Process(Snapshot(start.AddSeconds(3), 20));
        aggregator.Process(Snapshot(start.AddSeconds(4), 30));
        aggregator.Process(Snapshot(start.AddMinutes(1), 40));

        var bucket = Assert.Single(store.Query(null, null, null).Snapshots);
        var sensor = Assert.Single(bucket.Sensors);
        Assert.Equal(10, sensor.Min);
        Assert.Equal(30, sensor.Max);
        Assert.Equal(20, sensor.Average);
        Assert.Equal(3, sensor.SampleCount);
    }

    [Fact]
    public void CrossingBoundaryFinalizesPreviousAlignedBucket()
    {
        var (store, aggregator) = CreateSystem();
        var start = new DateTimeOffset(2026, 8, 14, 14, 20, 0, TimeSpan.Zero);

        aggregator.Process(Snapshot(start.AddSeconds(59), 10));
        Assert.Empty(store.Query(null, null, null).Snapshots);
        aggregator.Process(Snapshot(start.AddMinutes(1), 20));

        var bucket = Assert.Single(store.Query(null, null, null).Snapshots);
        Assert.Equal(start, bucket.StartTime);
        Assert.Equal(start.AddMinutes(1), bucket.EndTime);
    }

    [Fact]
    public void RetentionRemovesExpiredRecords()
    {
        var (store, _) = CreateSystem(retentionHours: 1);
        store.Add(Historical(_now.AddHours(-2), "old"));
        store.Add(Historical(_now.AddMinutes(-30), "recent"));

        var bucket = Assert.Single(store.Query(null, null, null).Snapshots);
        Assert.Equal("recent", Assert.Single(bucket.Sensors).Id);
    }

    [Fact]
    public void RecoveryLoadsRecentSkipsOldAndMalformedRecords()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "history.jsonl");
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        File.WriteAllLines(path,
        [
            JsonSerializer.Serialize(Historical(_now.AddMinutes(-20), "recent"), jsonOptions),
            "{incomplete",
            JsonSerializer.Serialize(Historical(_now.AddHours(-2), "old"), jsonOptions)
        ]);

        var store = CreateStore(path, retentionHours: 1);

        var bucket = Assert.Single(store.Query(null, null, null).Snapshots);
        Assert.Equal("recent", Assert.Single(bucket.Sensors).Id);
    }

    [Fact]
    public void QueryUsesExclusiveFromInclusiveToAndFiltersSensor()
    {
        var (store, _) = CreateSystem();
        var first = _now.AddMinutes(-2);
        var second = _now.AddMinutes(-1);
        store.Add(Historical(first, "cpu"));
        store.Add(new HistoricalSnapshot(second, second.AddMinutes(1),
        [
            Reading("cpu", 10),
            Reading("gpu", 20)
        ]));

        var result = store.Query(first, second, "gpu");

        var bucket = Assert.Single(result.Snapshots);
        Assert.Equal(second, bucket.StartTime);
        Assert.Equal("gpu", Assert.Single(bucket.Sensors).Id);
    }

    [Fact]
    public void HistoricalJsonWithoutSessionFieldsRemainsCompatible()
    {
        const string json = """
            {"startTime":"2026-08-14T14:20:00Z","endTime":"2026-08-14T14:21:00Z","sensors":[]}
            """;

        var snapshot = JsonSerializer.Deserialize<HistoricalSnapshot>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(snapshot);
        Assert.Null(snapshot.SessionId);
        Assert.Null(snapshot.DominantProcess);
    }

    private (HistoricalHistoryStore Store, HistoricalSensorAggregator Aggregator) CreateSystem(double retentionHours = 24)
    {
        var path = Path.Combine(_directory, "history.jsonl");
        var options = Options.Create(new HistoricalMonitoringOptions
        {
            BucketDurationSeconds = 60,
            RetentionHours = retentionHours,
            HistoryFilePath = path
        });
        var store = new HistoricalHistoryStore(options, new FixedTimeProvider(_now),
            NullLogger<HistoricalHistoryStore>.Instance);
        var aggregator = new HistoricalSensorAggregator(store, new SessionRuntimeContext(), options,
            NullLogger<HistoricalSensorAggregator>.Instance);
        return (store, aggregator);
    }

    private HistoricalHistoryStore CreateStore(string path, double retentionHours) =>
        new(Options.Create(new HistoricalMonitoringOptions
        {
            RetentionHours = retentionHours,
            HistoryFilePath = path
        }), new FixedTimeProvider(_now), NullLogger<HistoricalHistoryStore>.Instance);

    private static SensorSnapshot Snapshot(DateTimeOffset timestamp, float? value) => new(timestamp,
    [
        new SensorReading("sensor", "Hardware", "Sensor", "Temperature", value, "°C")
    ]);

    private static HistoricalSnapshot Historical(DateTimeOffset start, string sensorId) =>
        new(start, start.AddMinutes(1), [Reading(sensorId, 10)]);

    private static HistoricalSensorReading Reading(string id, float value) =>
        new(id, "Hardware", "Sensor", "Temperature", "°C", value, value, value, 1);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
