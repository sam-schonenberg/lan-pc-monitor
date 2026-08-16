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
        Assert.Equal(0, snapshot.Sequence);
    }

    [Fact]
    public void AssignsMonotonicSequencesAndPagesByCursor()
    {
        var (store, _) = CreateSystem();
        for (var index = 0; index < 1000; index++)
            store.Add(Historical(_now.AddMinutes(index), "sensor"));

        var first = store.QueryCompact(null, null, null, 500, HistoryResolution.Minute, null);
        Assert.Equal(500, first.Snapshots.Count);
        Assert.True(first.HasMore);
        Assert.Equal(1, first.FromSequence);
        Assert.Equal(500, first.NextSequence);

        var second = store.QueryCompact(null, null, first.NextSequence, 500, HistoryResolution.Minute, null);
        Assert.Equal(500, second.Snapshots.Count);
        Assert.False(second.HasMore);
        Assert.Equal(501, second.FromSequence);
        Assert.Equal(1000, second.ToSequence);
    }

    [Fact]
    public void ReverseCursorReturnsNewestBucketsFirstWithoutCrossingGapBoundary()
    {
        var (store, _) = CreateSystem();
        for (var index = 0; index < 100; index++)
            store.Add(Historical(_now.AddMinutes(index), "sensor"));

        var newestPage = store.QueryCompact(null, null, 39, 20, HistoryResolution.Minute,
            null, null, 81);

        Assert.Equal(20, newestPage.Snapshots.Count);
        Assert.Equal(80, newestPage.Snapshots[0].Sequence);
        Assert.Equal(61, newestPage.Snapshots[^1].Sequence);
        Assert.Equal(61, newestPage.PreviousSequence);
        Assert.True(newestPage.HasMore);
        Assert.All(newestPage.Snapshots, x => Assert.InRange(x.Sequence, 40, 80));
    }

    [Fact]
    public void RecoveryContinuesAfterHighestSequenceAndUpgradesLegacyRecords()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "history.jsonl");
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        File.WriteAllLines(path,
        [
            JsonSerializer.Serialize(Historical(_now.AddMinutes(-2), "legacy"), json),
            JsonSerializer.Serialize(Historical(_now.AddMinutes(-1), "existing") with { Sequence = 41 }, json)
        ]);
        var store = CreateStore(path, 24);
        store.Add(Historical(_now, "new"));

        var records = store.Query(null, null, null).Snapshots;
        Assert.All(records, x => Assert.True(x.Sequence > 0));
        Assert.Equal(43, records.Single(x => x.Sensors[0].Id == "new").Sequence);
    }

    [Fact]
    public void HourAggregationUsesWeightedAverageAndClockBoundaries()
    {
        var (store, _) = CreateSystem();
        var first = _now.Date.AddHours(14);
        store.Add(new HistoricalSnapshot(first, first.AddMinutes(1),
            [new("sensor", "Hardware", "Sensor", "Load", "%", 40, 60, 50, 60)]));
        store.Add(new HistoricalSnapshot(first.AddMinutes(59), first.AddHours(1),
            [new("sensor", "Hardware", "Sensor", "Load", "%", 90, 110, 100, 10)]));
        store.Add(new HistoricalSnapshot(first.AddHours(1), first.AddHours(1).AddMinutes(1),
            [new("sensor", "Hardware", "Sensor", "Load", "%", 5, 6, 5, 1)]));

        var result = store.QueryCompact(null, null, null, 500, HistoryResolution.Hour, null);
        Assert.Equal(2, result.Snapshots.Count);
        var aggregate = result.Snapshots[0];
        Assert.Equal(first, aggregate.StartTime);
        Assert.Equal(40, aggregate.Sensors[0].Min);
        Assert.Equal(110, aggregate.Sensors[0].Max);
        Assert.Equal(57.1, aggregate.Sensors[0].Avg);
        Assert.Equal(70, aggregate.Sensors[0].Count);
    }

    [Fact]
    public void CatalogContainsEveryCompactHistorySensorId()
    {
        var (store, _) = CreateSystem();
        store.Add(new HistoricalSnapshot(_now, _now.AddMinutes(1), [Reading("cpu", 10), Reading("gpu", 20)]));
        var catalog = store.GetCatalog();
        var ids = catalog.Sensors.Select(x => x.Id).ToHashSet();
        var history = store.QueryCompact(null, null, null, 500, HistoryResolution.Minute, null);
        Assert.NotEmpty(catalog.Version);
        Assert.All(history.Snapshots.SelectMany(x => x.Sensors), x => Assert.Contains(x.SensorId, ids));
    }

    [Fact]
    public void ManifestReportsRetainedCoverageAndSequenceGaps()
    {
        var (store, _) = CreateSystem();
        store.Add(Historical(_now.AddMinutes(-3), "sensor") with { Sequence = 10 });
        store.Add(Historical(_now.AddMinutes(-2), "sensor") with { Sequence = 11 });
        store.Add(Historical(_now.AddMinutes(-1), "sensor") with { Sequence = 14 });

        var manifest = store.GetManifest();
        Assert.NotEqual(Guid.Empty, manifest.StreamId);
        Assert.Equal(10, manifest.OldestSequence);
        Assert.Equal(14, manifest.NewestSequence);
        Assert.Equal(3, manifest.BucketCount);
        Assert.Equal(2, manifest.SequenceRanges.Count);
        Assert.Equal(new HistorySequenceRange(10, 11, 2), manifest.SequenceRanges[0]);
        Assert.Equal(new HistorySequenceRange(14, 14, 1), manifest.SequenceRanges[1]);
    }

    [Fact]
    public void ManifestStreamIdentitySurvivesRestart()
    {
        var path = Path.Combine(_directory, "history.jsonl");
        var first = CreateStore(path, 24);
        first.Add(Historical(_now.AddMinutes(-1), "sensor"));
        var streamId = first.GetManifest().StreamId;

        var restored = CreateStore(path, 24);
        Assert.Equal(streamId, restored.GetManifest().StreamId);
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
