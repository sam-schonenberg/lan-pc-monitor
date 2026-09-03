using PCMonitor.Application.Data;
using PCMonitor.Application.Data.Entities;
using PCMonitor.Application.Models.Api;
using PCMonitor.Application.Models;
namespace PCMonitor.Application.Services.Storage;
public sealed class HistoryRepository(AppDatabase database)
{
    public const int DetailPageSize = 60;
    public async Task SaveAsync(IEnumerable<HistoricalSnapshotDto> snapshots)
    {
        var connection = await database.GetConnectionAsync();
        var entities = snapshots.SelectMany(bucket => bucket.Sensors.Select(sensor =>
            new HistoricalSensorEntity
            {
                Id = $"{bucket.StartTime.UtcTicks}:{sensor.Id}", BucketStartUtcTicks = bucket.StartTime.UtcTicks,
                BucketEndUtcTicks = bucket.EndTime.UtcTicks, SensorId = sensor.Id, Hardware = sensor.Hardware,
                SensorName = sensor.Name, SensorType = sensor.Type, Unit = sensor.Unit, Min = sensor.Min,
                Max = sensor.Max, Average = sensor.Average, SampleCount = sensor.SampleCount,
                SessionId = bucket.SessionId?.ToString(), DominantProcessName = bucket.DominantProcess?.Name
            })).ToArray();

        if (entities.Length > 0)
            await connection.InsertAllAsync(entities, "OR REPLACE", runInTransaction: true);
    }

    public async Task SaveCatalogAsync(SensorCatalogResponseDto catalog)
    {
        var connection = await database.GetConnectionAsync();
        var entities = catalog.Sensors.Select(x => new SensorCatalogEntity
        {
            TransportId = x.Id, SensorKey = x.Key, Hardware = x.Hardware, SensorName = x.Name,
            SensorType = x.Type, Unit = x.Unit, CatalogVersion = catalog.Version
        }).ToArray();
        await connection.RunInTransactionAsync(transaction =>
        {
            transaction.DeleteAll<SensorCatalogEntity>();
            transaction.InsertAll(entities, runInTransaction: false);
        });
    }

    public async Task SaveCompactAsync(Guid streamId, CompactHistoryResponseDto response,
        IReadOnlyDictionary<int, SensorCatalogEntryDto> catalog)
    {
        var connection = await database.GetConnectionAsync();
        var entities = response.Snapshots.SelectMany(bucket => bucket.Sensors
            .Where(sensor => catalog.ContainsKey(sensor.SensorId))
            .Select(sensor =>
            {
                var metadata = catalog[sensor.SensorId];
                return new HistoricalSensorEntity
                {
                    Id = $"{streamId:N}:{bucket.Sequence}:{metadata.Key}", StreamId = streamId.ToString("N"),
                    Sequence = bucket.Sequence,
                    BucketStartUtcTicks = bucket.StartTime.UtcTicks, BucketEndUtcTicks = bucket.EndTime.UtcTicks,
                    SensorId = metadata.Key, Hardware = metadata.Hardware, SensorName = metadata.Name,
                    SensorType = metadata.Type, Unit = metadata.Unit, Min = (float)sensor.Min, Max = (float)sensor.Max,
                    Average = sensor.Avg, SampleCount = sensor.Count, SessionId = bucket.SessionId?.ToString(),
                    DominantProcessName = bucket.DominantProcess?.Name
                };
            })).ToArray();
        if (entities.Length > 0)
        {
            var oldest = response.Snapshots.Min(x => x.StartTime.UtcTicks);
            var newest = response.Snapshots.Max(x => x.StartTime.UtcTicks);
            // Replace pre-ledger rows for this downloaded window so an upgrade does not show duplicates.
            await connection.ExecuteAsync(
                "DELETE FROM HistoricalSensors WHERE (StreamId IS NULL OR StreamId = '') AND BucketStartUtcTicks >= ? AND BucketStartUtcTicks <= ?",
                oldest, newest);
            await connection.InsertAllAsync(entities, "OR REPLACE", runInTransaction: true);
        }
        await RecordCoverageAsync(streamId, response.Snapshots.Select(x => x.Sequence));
    }

    public async Task<long?> GetNewestSequenceAsync(Guid? streamId = null)
    {
        var connection = await database.GetConnectionAsync();
        var query = connection.Table<HistoricalSensorEntity>().Where(x => x.Sequence > 0);
        if (streamId is not null)
        {
            var id = streamId.Value.ToString("N");
            query = query.Where(x => x.StreamId == id);
        }
        var latest = await query
            .OrderByDescending(x => x.Sequence).FirstOrDefaultAsync();
        return latest?.Sequence;
    }

    public async Task<IReadOnlyList<SequenceInterval>> GetCoverageAsync(Guid streamId)
    {
        var id = streamId.ToString("N");
        var rows = await (await database.GetConnectionAsync()).Table<HistoryCoverageEntity>()
            .Where(x => x.StreamId == id).OrderBy(x => x.FromSequence).ToListAsync();
        return rows.Select(x => new SequenceInterval(x.FromSequence, x.ToSequence)).ToArray();
    }

    public async Task<IReadOnlyList<SequenceInterval>> GetMissingCoverageAsync(HistoryManifestResponseDto manifest)
    {
        var covered = await GetCoverageAsync(manifest.StreamId);
        return SubtractCoverage(manifest.SequenceRanges
            .Select(x => new SequenceInterval(x.FromSequence, x.ToSequence)).ToArray(), covered);
    }

    public async Task RecordCoverageAsync(Guid streamId, IEnumerable<long> sequences)
    {
        var incoming = CompressSequences(sequences);
        if (incoming.Count == 0) return;
        var connection = await database.GetConnectionAsync();
        var id = streamId.ToString("N");
        await connection.RunInTransactionAsync(transaction =>
        {
            var existing = transaction.Table<HistoryCoverageEntity>().Where(x => x.StreamId == id)
                .OrderBy(x => x.FromSequence).ToList();
            var merged = MergeIntervals(existing.Select(x => new SequenceInterval(x.FromSequence, x.ToSequence))
                .Concat(incoming));
            foreach (var row in existing) transaction.Delete(row);
            foreach (var range in merged) transaction.Insert(new HistoryCoverageEntity
            {
                Id = $"{id}:{range.FromSequence}", StreamId = id, FromSequence = range.FromSequence,
                ToSequence = range.ToSequence, UpdatedUtcTicks = DateTimeOffset.UtcNow.UtcTicks
            });
        });
    }

    public static IReadOnlyList<SequenceInterval> SubtractCoverage(
        IReadOnlyList<SequenceInterval> available, IReadOnlyList<SequenceInterval> covered)
    {
        var result = new List<SequenceInterval>();
        foreach (var source in available)
        {
            var cursor = source.FromSequence;
            foreach (var local in covered.Where(x => x.ToSequence >= source.FromSequence && x.FromSequence <= source.ToSequence)
                         .OrderBy(x => x.FromSequence))
            {
                if (local.FromSequence > cursor) result.Add(new(cursor, Math.Min(source.ToSequence, local.FromSequence - 1)));
                cursor = Math.Max(cursor, local.ToSequence + 1);
                if (cursor > source.ToSequence) break;
            }
            if (cursor <= source.ToSequence) result.Add(new(cursor, source.ToSequence));
        }
        return result;
    }

    private static IReadOnlyList<SequenceInterval> CompressSequences(IEnumerable<long> sequences) =>
        MergeIntervals(sequences.Distinct().OrderBy(x => x).Select(x => new SequenceInterval(x, x)));

    private static IReadOnlyList<SequenceInterval> MergeIntervals(IEnumerable<SequenceInterval> intervals)
    {
        var ordered = intervals.OrderBy(x => x.FromSequence).ThenBy(x => x.ToSequence).ToArray();
        if (ordered.Length == 0) return [];
        var result = new List<SequenceInterval>(); var current = ordered[0];
        foreach (var next in ordered.Skip(1))
        {
            if (next.FromSequence <= current.ToSequence + 1)
                current = current with { ToSequence = Math.Max(current.ToSequence, next.ToSequence) };
            else { result.Add(current); current = next; }
        }
        result.Add(current); return result;
    }
    public async Task<long> CountAsync() => await (await database.GetConnectionAsync()).Table<HistoricalSensorEntity>().CountAsync();
    public async Task<DateTimeOffset?> GetNewestTimestampAsync() => (await (await database.GetConnectionAsync()).Table<HistoricalSensorEntity>().OrderByDescending(x => x.BucketStartUtcTicks).FirstOrDefaultAsync())?.BucketStartTime;

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff)
    {
        var connection = await database.GetConnectionAsync();
        return await connection.ExecuteAsync(
            $"DELETE FROM {nameof(HistoricalSensorEntity).Replace("Entity", "s")} WHERE {nameof(HistoricalSensorEntity.BucketEndUtcTicks)} < ?",
            cutoff.UtcTicks);
    }

    public async Task<IReadOnlyList<HistoricalSensorEntity>> GetSensorOptionsAsync()
    {
        var connection = await database.GetConnectionAsync();
        var catalog = await connection.Table<SensorCatalogEntity>().OrderBy(x => x.Hardware).ThenBy(x => x.SensorName).ToListAsync();
        if (catalog.Count > 0) return catalog.Select(x => new HistoricalSensorEntity
        {
            SensorId = x.SensorKey, Hardware = x.Hardware, SensorName = x.SensorName,
            SensorType = x.SensorType, Unit = x.Unit
        }).ToArray();
        return await connection.QueryAsync<HistoricalSensorEntity>(
            """
            SELECT SensorId, MAX(Hardware) AS Hardware, MAX(SensorName) AS SensorName,
                   MAX(SensorType) AS SensorType, MAX(Unit) AS Unit
            FROM HistoricalSensors
            GROUP BY SensorId
            ORDER BY Hardware, SensorName
            """);
    }

    public async Task<IReadOnlyList<HistoricalSensorEntity>> GetRecentAsync(string sensorId, int limit = 120)
    {
        var connection = await database.GetConnectionAsync();
        return await connection.Table<HistoricalSensorEntity>()
            .Where(item => item.SensorId == sensorId)
            .OrderByDescending(item => item.BucketStartUtcTicks)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<HistoricalSensorEntity>> GetAllHistoryAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = await database.GetConnectionAsync();
        var rows = await connection.QueryAsync<HistoricalSensorEntity>(
            """
            SELECT * FROM HistoricalSensors
            WHERE BucketStartUtcTicks >= ? AND BucketStartUtcTicks < ?
            ORDER BY BucketStartUtcTicks, Hardware, SensorName
            """, from.UtcTicks, to.UtcTicks);
        cancellationToken.ThrowIfCancellationRequested();
        return rows;
    }

    public async Task<IReadOnlyList<HistoricalSensorEntity>> GetSensorHistoryPageAsync(
        string sensorId, DateTimeOffset from, DateTimeOffset to, DateTimeOffset? before,
        int limit = DetailPageSize, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var upperTicks = Math.Min(to.UtcTicks, before?.UtcTicks ?? long.MaxValue);
        var connection = await database.GetConnectionAsync();
        var rows = await connection.QueryAsync<HistoricalSensorEntity>(
            """
            SELECT * FROM HistoricalSensors
            WHERE SensorId = ? AND BucketStartUtcTicks >= ? AND BucketStartUtcTicks < ?
            ORDER BY BucketStartUtcTicks DESC
            LIMIT ?
            """, sensorId, from.UtcTicks, upperTicks, Math.Clamp(limit, 1, 250));
        cancellationToken.ThrowIfCancellationRequested();
        return rows;
    }

    public async Task<HistoricalRangeStatistics?> GetStatisticsAsync(
        string sensorId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = await database.GetConnectionAsync();
        var rows = await connection.QueryAsync<StatisticsRow>(
            """
            SELECT MIN(Min) AS Minimum, MAX(Max) AS Maximum,
                   SUM(Average * SampleCount) AS WeightedSum,
                   SUM(SampleCount) AS TotalSamples,
                   (SELECT Average FROM HistoricalSensors latest
                    WHERE latest.SensorId = ? AND latest.BucketStartUtcTicks >= ? AND latest.BucketStartUtcTicks < ?
                    ORDER BY latest.BucketStartUtcTicks DESC LIMIT 1) AS Latest,
                   COUNT(*) AS RecordCount
            FROM HistoricalSensors
            WHERE SensorId = ? AND BucketStartUtcTicks >= ? AND BucketStartUtcTicks < ?
            """, sensorId, from.UtcTicks, to.UtcTicks, sensorId, from.UtcTicks, to.UtcTicks);
        cancellationToken.ThrowIfCancellationRequested();
        var row = rows.FirstOrDefault();
        return row is null || row.RecordCount == 0 ? null : new HistoricalRangeStatistics(
            row.TotalSamples > 0 ? row.WeightedSum / row.TotalSamples : null,
            row.Minimum, row.Maximum, row.Latest, row.TotalSamples, row.RecordCount);
    }

    public async Task<IReadOnlyList<SensorChartPoint>> GetChartDataAsync(
        string sensorId, DateTimeOffset from, DateTimeOffset to, SensorChartResolution resolution,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = await database.GetConnectionAsync();
        List<ChartRow> rows;
        if (resolution == SensorChartResolution.Minute)
        {
            rows = await connection.QueryAsync<ChartRow>(
                """
                SELECT BucketStartUtcTicks AS TimestampTicks, Min AS Minimum, Max AS Maximum,
                       Average, SampleCount
                FROM HistoricalSensors
                WHERE SensorId = ? AND BucketStartUtcTicks >= ? AND BucketStartUtcTicks < ?
                ORDER BY BucketStartUtcTicks
                LIMIT 1500
                """, sensorId, from.UtcTicks, to.UtcTicks);
        }
        else
        {
            // UTC clock alignment is consistent with persisted service buckets. Timestamps are localized by the chart.
            var bucketTicks = resolution == SensorChartResolution.Hour ? TimeSpan.TicksPerHour : TimeSpan.TicksPerDay;
            rows = await connection.QueryAsync<ChartRow>(
                """
                SELECT (BucketStartUtcTicks / ?) * ? AS TimestampTicks,
                       MIN(Min) AS Minimum, MAX(Max) AS Maximum,
                       CASE WHEN SUM(SampleCount) > 0
                            THEN SUM(Average * SampleCount) / SUM(SampleCount) ELSE 0 END AS Average,
                       SUM(SampleCount) AS SampleCount
                FROM HistoricalSensors
                WHERE SensorId = ? AND BucketStartUtcTicks >= ? AND BucketStartUtcTicks < ?
                GROUP BY (BucketStartUtcTicks / ?)
                ORDER BY TimestampTicks
                LIMIT 1500
                """, bucketTicks, bucketTicks, sensorId, from.UtcTicks, to.UtcTicks, bucketTicks);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return rows.Select(x => new SensorChartPoint(new DateTimeOffset(x.TimestampTicks, TimeSpan.Zero),
            x.Average, x.Minimum, x.Maximum, x.SampleCount)).ToArray();
    }

    private sealed class StatisticsRow
    {
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double WeightedSum { get; set; }
        public long TotalSamples { get; set; }
        public double Latest { get; set; }
        public long RecordCount { get; set; }
    }

    private sealed class ChartRow
    {
        public long TimestampTicks { get; set; }
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double Average { get; set; }
        public long SampleCount { get; set; }
    }
}

public sealed record HistoricalRangeStatistics(double? Average, double Minimum, double Maximum,
    double Latest, long SampleCount, long RecordCount);
public sealed record SequenceInterval(long FromSequence, long ToSequence)
{
    public long Count => Math.Max(0, ToSequence - FromSequence + 1);
}
