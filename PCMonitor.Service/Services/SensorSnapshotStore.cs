using PCMonitor.Service.Models;

namespace PCMonitor.Service.Services;

public sealed class SensorSnapshotStore
{
    private readonly Lock _sync = new();
    private SensorSnapshot _current = new(DateTimeOffset.UtcNow, Array.Empty<SensorReading>());
    private long _version;
    private TaskCompletionSource _changed = NewSignal();

    public SensorSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public void Update(SensorSnapshot snapshot)
    {
        TaskCompletionSource changed;
        lock (_sync)
        {
            _current = snapshot;
            _version++;
            changed = _changed;
            _changed = NewSignal();
        }

        changed.TrySetResult();
    }

    public async ValueTask<(long Version, SensorSnapshot Snapshot)> WaitForUpdateAsync(
        long afterVersion, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task signal;
            lock (_sync)
            {
                if (_version > afterVersion)
                {
                    return (_version, _current);
                }

                signal = _changed.Task;
            }

            await signal.WaitAsync(cancellationToken);
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
