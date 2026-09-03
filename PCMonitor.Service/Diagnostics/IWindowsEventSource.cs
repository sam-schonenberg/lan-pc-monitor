namespace PCMonitor.Service.Diagnostics;

public interface IWindowsEventSource
{
    Task<WindowsEventBatch> ReadAfterAsync(string channel, WindowsEventCheckpoint? checkpoint,
        int maximumCount, CancellationToken cancellationToken);
}
