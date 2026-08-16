using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;
namespace PCMonitor.Application.Services.Sync;

public interface IHistoryBackgroundScheduler
{
    void EnqueueBackfill();
    void EnsurePeriodicBackfill();
}
public sealed class NoOpHistoryBackgroundScheduler : IHistoryBackgroundScheduler
{
    public void EnqueueBackfill() { }
    public void EnsurePeriodicBackfill() { }
}

public sealed class HistorySyncService(
    MonitorApiClient api,
    HistoryRepository repository,
    IAppSettingsService settings,
    IHistoryBackgroundScheduler backgroundScheduler)
{
    public const int ProgressivePageSize = 60;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    // Foreground work commits the newest missing hour, then lets WorkManager repair older gaps.
    public async Task SyncAsync(IProgress<HistorySyncProgress>? progress = null, CancellationToken token = default)
    {
        var result = await SynchronizeNewestMissingAsync(1, progress, token);
        if (result.HasMore) backgroundScheduler.EnqueueBackfill();
    }

    // Called by bounded WorkManager jobs. A later job resumes from the persisted coverage ledger.
    public async Task<bool> BackfillBatchAsync(int maximumPages = 4, CancellationToken token = default) =>
        (await SynchronizeNewestMissingAsync(maximumPages, null, token)).HasMore;

    private async Task<SyncBatchResult> SynchronizeNewestMissingAsync(int maximumPages,
        IProgress<HistorySyncProgress>? progress, CancellationToken token)
    {
        await _syncLock.WaitAsync(token);
        try
        {
            progress?.Report(new(0, 0, null, false, "Comparing history inventories…"));
            var manifest = await api.GetHistoryManifestAsync(token);
            var catalog = await api.GetSensorCatalogAsync(token);
            await repository.SaveCatalogAsync(catalog);
            var catalogById = catalog.Sensors.ToDictionary(x => x.Id);
            var initialMissing = await repository.GetMissingCoverageAsync(manifest);
            var totalMissing = initialMissing.Sum(x => x.Count);
            if (initialMissing.Count == 0)
            {
                await settings.SetLastHistorySyncAsync(DateTimeOffset.UtcNow);
                progress?.Report(new(0, 0, 0, true, "History is already up to date."));
                return new(false, 0);
            }

            var pages = 0; var buckets = 0;
            while (pages < Math.Max(1, maximumPages))
            {
                token.ThrowIfCancellationRequested();
                var missing = await repository.GetMissingCoverageAsync(manifest);
                if (missing.Count == 0) break;
                var newestGap = missing[^1];
                var response = await api.GetCompactHistoryAsync(newestGap.FromSequence - 1,
                    ProgressivePageSize, token, newestGap.ToSequence + 1);
                if (!string.Equals(response.CatalogVersion, catalog.Version, StringComparison.Ordinal))
                    throw new InvalidOperationException("The sensor catalog changed during history synchronization. Refresh again.");
                if (response.Snapshots.Count == 0) break;
                await repository.SaveCompactAsync(manifest.StreamId, response, catalogById);
                pages++; buckets += response.Snapshots.Count;
                progress?.Report(new(pages, buckets, (int)Math.Min(int.MaxValue, totalMissing), false,
                    $"Latest history ready: {buckets:N0} of {totalMissing:N0} missing buckets stored…"));
            }

            var remaining = await repository.GetMissingCoverageAsync(manifest);
            var remainingCount = remaining.Sum(x => x.Count);
            await settings.SetLastHistorySyncAsync(DateTimeOffset.UtcNow);
            var hasMore = remainingCount > 0;
            progress?.Report(new(pages, buckets, (int)Math.Min(int.MaxValue, totalMissing), true,
                hasMore ? $"Latest history is ready. {remainingCount:N0} older buckets will continue in the background."
                    : "All available history is synchronized."));
            return new(hasMore, buckets);
        }
        finally { _syncLock.Release(); }
    }

    private sealed record SyncBatchResult(bool HasMore, int BucketsStored);
}

public sealed record HistorySyncProgress(int PagesCompleted, int BucketsStored, int? TotalBuckets,
    bool IsComplete, string Message)
{
    public double BarProgress => IsComplete ? 1 : TotalBuckets > 0
        ? Math.Clamp((double)BucketsStored / TotalBuckets.Value, 0.02, 0.98)
        : PagesCompleted == 0 ? 0.04 : 1d - 1d / (PagesCompleted + 2);
}

public sealed class AlertSyncService(MonitorApiClient api, AlertRepository repository)
{
    public async Task SyncAsync(CancellationToken token = default)
    {
        var response = await api.GetAlertsAsync(await repository.GetNewestTimestampAsync(), token);
        await repository.SaveAsync(response.Alerts);
    }
}
