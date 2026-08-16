using Android.Content;
using AndroidX.Work;
using Java.Util.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using PCMonitor.Application.Services.Sync;

namespace PCMonitor.Application.Platforms.Android;

public sealed class HistoryBackfillWorker(Context context, WorkerParameters workerParameters)
    : Worker(context, workerParameters)
{
    public override Result DoWork()
    {
        try
        {
            var sync = IPlatformApplication.Current?.Services.GetRequiredService<HistorySyncService>();
            if (sync is null) return Result.InvokeRetry();

            sync.BackfillBatchAsync(8).GetAwaiter().GetResult();
            return Result.InvokeSuccess();
        }
        catch
        {
            return Result.InvokeRetry();
        }
    }
}

public sealed class AndroidHistoryBackgroundScheduler : IHistoryBackgroundScheduler
{
    private const string ImmediateWorkName = "pcmonitor-history-backfill";
    private const string PeriodicWorkName = "pcmonitor-history-maintenance";
    private readonly Context _context;

    public AndroidHistoryBackgroundScheduler()
    {
        _context = global::Android.App.Application.Context;
    }

    public void EnqueueBackfill()
    {
        var request = OneTimeWorkRequest.Builder
            .From<HistoryBackfillWorker>()
            .SetConstraints(NetworkConstraints())
            .Build();
        WorkManager.GetInstance(_context).EnqueueUniqueWork(
            ImmediateWorkName, ExistingWorkPolicy.Keep!, request);
    }

    public void EnsurePeriodicBackfill()
    {
        var request = PeriodicWorkRequest.Builder
            .From<HistoryBackfillWorker>(15, TimeUnit.Minutes!)
            .SetConstraints(NetworkConstraints())
            .Build();
        WorkManager.GetInstance(_context).EnqueueUniquePeriodicWork(
            PeriodicWorkName, ExistingPeriodicWorkPolicy.Keep!, request);
    }

    private static Constraints NetworkConstraints() => new Constraints.Builder()
        .SetRequiredNetworkType(NetworkType.Connected!)
        .Build();
}
