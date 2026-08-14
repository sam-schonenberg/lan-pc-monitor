using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PCMonitor.Service.History;
using PCMonitor.Service.Models;
using PCMonitor.Service.SessionDetection;
using Xunit;

namespace PCMonitor.Service.Tests.SessionDetection;

public sealed class SessionMetadataTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pcmonitor-session-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CandidateIdRemainsStableWhenPromoted()
    {
        var detector = CreateDetector(out _);
        var start = DateTimeOffset.UtcNow;

        detector.Process(LoadSnapshot(start, 80));
        var candidate = detector.GetCurrent();
        detector.Process(LoadSnapshot(start.AddSeconds(2), 80));
        var active = detector.GetCurrent();

        Assert.Equal(LoadSessionState.Candidate, candidate.State);
        Assert.Equal(LoadSessionState.Active, active.State);
        Assert.NotNull(candidate.Session);
        Assert.Equal(candidate.Session.Id, active.Session!.Id);
        Assert.Equal(start, active.Session.StartedAt);
    }

    [Fact]
    public void CancelledCandidateIsCleared()
    {
        var detector = CreateDetector(out var context);
        var start = DateTimeOffset.UtcNow;

        detector.Process(LoadSnapshot(start, 80));
        var candidateId = detector.GetCurrent().Session!.Id;
        detector.Process(LoadSnapshot(start.AddSeconds(1), 1));
        detector.Process(LoadSnapshot(start.AddSeconds(2), 1));

        Assert.Equal(LoadSessionState.Idle, detector.GetCurrent().State);
        Assert.Null(context.GetSnapshot().SessionId);
        Assert.NotEqual(Guid.Empty, candidateId);
    }

    [Fact]
    public void DominantProcessUsesMostDominantIntervals()
    {
        var context = new SessionRuntimeContext();
        var id = Guid.NewGuid();
        context.CreateCandidate(id, DateTimeOffset.UtcNow);
        context.Promote(id);

        for (var index = 0; index < 8; index++)
        {
            context.RecordProcessSample(id, DateTimeOffset.UtcNow,
                [new ProcessCpuReading("witcher3.exe", 48), new ProcessCpuReading("steam.exe", 3)]);
        }
        for (var index = 0; index < 2; index++)
        {
            context.RecordProcessSample(id, DateTimeOffset.UtcNow,
                [new ProcessCpuReading("steam.exe", 40), new ProcessCpuReading("witcher3.exe", 2)]);
        }

        var primary = context.GetSnapshot().PrimaryProcess;
        Assert.NotNull(primary);
        Assert.Equal("witcher3.exe", primary.Name);
        Assert.Equal(8, primary.DominantSampleCount);
    }

    [Fact]
    public void CpuPercentageIsNormalizedToTotalMachineCapacity()
    {
        var result = ProcessCpuCalculator.Calculate(
            TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1), 4);

        Assert.Equal(2.5, result);
    }

    [Fact]
    public void ConfirmedSessionIsAssociatedWithBucketAndIdleBucketIsNot()
    {
        var historyOptions = Options.Create(new HistoricalMonitoringOptions
        {
            BucketDurationSeconds = 60,
            RetentionHours = 24,
            HistoryFilePath = Path.Combine(_directory, "history.jsonl")
        });
        var now = new DateTimeOffset(2026, 8, 14, 16, 42, 0, TimeSpan.Zero);
        var context = new SessionRuntimeContext();
        var store = new HistoricalHistoryStore(historyOptions, new FixedTimeProvider(now.AddHours(1)),
            NullLogger<HistoricalHistoryStore>.Instance);
        var aggregator = new HistoricalSensorAggregator(store, context, historyOptions,
            NullLogger<HistoricalSensorAggregator>.Instance);
        var id = Guid.NewGuid();

        context.CreateCandidate(id, now.AddSeconds(3));
        aggregator.Process(SensorSnapshot(now.AddSeconds(3)));
        context.Promote(id);
        context.RecordProcessSample(id, now.AddSeconds(13), [new ProcessCpuReading("game.exe", 50)]);
        aggregator.Process(SensorSnapshot(now.AddSeconds(13)));
        context.Clear(id);
        aggregator.Process(SensorSnapshot(now.AddMinutes(1)));
        aggregator.Process(SensorSnapshot(now.AddMinutes(1).AddSeconds(1)));
        aggregator.Process(SensorSnapshot(now.AddMinutes(2)));

        var buckets = store.Query(null, null, null).Snapshots;
        Assert.Equal(id, buckets[0].SessionId);
        Assert.Equal("game.exe", buckets[0].DominantProcess?.Name);
        Assert.Null(buckets[1].SessionId);
    }

    private static LoadSessionDetector CreateDetector(out SessionRuntimeContext context)
    {
        context = new SessionRuntimeContext();
        return new LoadSessionDetector(
            new LoadSensorSelector(),
            context,
            Options.Create(new SessionDetectionOptions
            {
                StartCpuLoadPercent = 40,
                StartGpuLoadPercent = 40,
                StartWindowSeconds = 1,
                StartDurationSeconds = 2,
                EndCpuLoadPercent = 20,
                EndGpuLoadPercent = 20,
                EndWindowSeconds = 1,
                EndDurationSeconds = 2
            }),
            NullLogger<LoadSessionDetector>.Instance);
    }

    private static SensorSnapshot LoadSnapshot(DateTimeOffset timestamp, float cpuLoad) => new(timestamp,
    [
        new SensorReading("/intelcpu/0/load/0", "CPU", "CPU Total", "Load", cpuLoad, "%")
    ]);

    private static SensorSnapshot SensorSnapshot(DateTimeOffset timestamp) => new(timestamp,
    [
        new SensorReading("temperature", "GPU", "GPU Core", "Temperature", 70, "°C")
    ]);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
