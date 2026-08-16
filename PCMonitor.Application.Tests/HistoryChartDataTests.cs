using PCMonitor.Application.Data;
using PCMonitor.Application.Data.Entities;
using PCMonitor.Application.Models;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Models.Api;
using Xunit;

namespace PCMonitor.Application.Tests;

public sealed class HistoryChartDataTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pcmonitor-chart-{Guid.NewGuid():N}.db3");
    private AppDatabase? _database;

    [Fact]
    public async Task MinuteQueryIsRangeAndSensorScopedAndEmptyIsSafe()
    {
        var (database, repository) = Create();
        var start = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
        await Seed(database, Enumerable.Range(0, 70).Select(i => Row("gpu", start.AddMinutes(i), i, 1))
            .Append(Row("cpu", start.AddMinutes(1), 999, 1)));

        var points = await repository.GetChartDataAsync("gpu", start, start.AddHours(1), SensorChartResolution.Minute);
        Assert.Equal(60, points.Count);
        Assert.DoesNotContain(points, x => x.Average == 999);
        Assert.Empty(await repository.GetChartDataAsync("missing", start, start.AddHours(1), SensorChartResolution.Minute));
    }

    [Fact]
    public async Task HourAggregationUsesWeightedAverageAndPreservesExtrema()
    {
        var (database, repository) = Create();
        var start = new DateTimeOffset(2026, 8, 15, 14, 0, 0, TimeSpan.Zero);
        await Seed(database,
        [
            Row("gpu", start, 50, 60, 40, 60),
            Row("gpu", start.AddMinutes(30), 100, 10, 90, 110),
            Row("gpu", start.AddHours(1), 20, 1, 19, 21)
        ]);

        var points = await repository.GetChartDataAsync("gpu", start, start.AddHours(2), SensorChartResolution.Hour);
        Assert.Equal(2, points.Count);
        Assert.Equal((50d * 60 + 100d * 10) / 70, points[0].Average, 8);
        Assert.Equal(40, points[0].Minimum);
        Assert.Equal(110, points[0].Maximum);
        Assert.Equal(70, points[0].SampleCount);
    }

    [Fact]
    public async Task CoverageLedgerMergesCommittedPagesAndFindsManifestGaps()
    {
        var (_, repository) = Create();
        var stream = Guid.NewGuid();
        await repository.RecordCoverageAsync(stream, Enumerable.Range(100, 10).Select(x => (long)x));
        await repository.RecordCoverageAsync(stream, Enumerable.Range(120, 10).Select(x => (long)x));
        await repository.RecordCoverageAsync(stream, Enumerable.Range(109, 12).Select(x => (long)x));

        Assert.Equal([new SequenceInterval(100, 129)], await repository.GetCoverageAsync(stream));
        var manifest = new HistoryManifestResponseDto(stream, "catalog", 90, 140, 51, null, null,
            60, 168, [new HistorySequenceRangeDto(90, 140, 51)], DateTimeOffset.UtcNow);
        Assert.Equal([new SequenceInterval(90, 99), new SequenceInterval(130, 140)],
            await repository.GetMissingCoverageAsync(manifest));
    }

    [Fact]
    public void CoverageComparisonRespectsServerSideSequenceGaps()
    {
        var missing = HistoryRepository.SubtractCoverage(
            [new(10, 20), new(30, 40)], [new(10, 15), new(32, 35)]);
        Assert.Equal([new SequenceInterval(16, 20), new SequenceInterval(30, 31), new SequenceInterval(36, 40)], missing);
    }

    private (AppDatabase Database, HistoryRepository Repository) Create()
    {
        var database = new AppDatabase(_path);
        _database = database;
        return (database, new HistoryRepository(database));
    }

    private static HistoricalSensorEntity Row(string sensor, DateTimeOffset time, double average, long count,
        float? minimum = null, float? maximum = null) => new()
    {
        Id = $"{sensor}:{time.UtcTicks}", SensorId = sensor, BucketStartUtcTicks = time.UtcTicks,
        BucketEndUtcTicks = time.AddMinutes(1).UtcTicks, Hardware = sensor, SensorName = sensor,
        SensorType = "Temperature", Unit = "°C", Average = average, SampleCount = count,
        Min = minimum ?? (float)average, Max = maximum ?? (float)average
    };

    private static async Task Seed(AppDatabase database, IEnumerable<HistoricalSensorEntity> rows) =>
        await (await database.GetConnectionAsync()).InsertAllAsync(rows, runInTransaction: true);

    public void Dispose()
    {
        _database?.Dispose();
        if (File.Exists(_path)) File.Delete(_path);
    }
}
