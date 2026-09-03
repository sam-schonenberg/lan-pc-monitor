using Microsoft.Extensions.Options;

namespace PCMonitor.Service.Diagnostics;

public sealed class WindowsDiagnosticScanner : BackgroundService
{
    private readonly IWindowsEventSource _source;
    private readonly WindowsDiagnosticStore _store;
    private readonly WindowsDiagnosticsOptions _options;
    private readonly ILogger<WindowsDiagnosticScanner> _logger;
    private readonly Lock _statusSync = new();
    private DateTimeOffset? _lastSuccessfulScan;
    private string? _lastError;

    public WindowsDiagnosticScanner(IWindowsEventSource source, WindowsDiagnosticStore store,
        IOptions<WindowsDiagnosticsOptions> options, ILogger<WindowsDiagnosticScanner> logger)
    { _source = source; _store = store; _options = options.Value; _logger = logger; }

    public WindowsDiagnosticsStatusResponse GetStatus()
    {
        var inventory = _store.GetInventory();
        lock (_statusSync) return new(_options.Enabled, Math.Max(1, _options.ScanIntervalMinutes),
            Math.Max(1, _options.RetentionDays), Math.Max(1, _options.MaximumStorageMegabytes),
            _options.Channels.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            _options.Providers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            inventory.Count, inventory.Oldest, inventory.Newest,
            _lastSuccessfulScan, _lastError);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !OperatingSystem.IsWindows()) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            await ScanAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _options.ScanIntervalMinutes)), stoppingToken);
        }
    }

    internal async Task ScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var channel in _options.Channels.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                var hasMore = true;
                while (hasMore)
                {
                    var batch = await _source.ReadAfterAsync(channel, _store.GetCheckpoint(channel),
                        Math.Max(1, _options.MaximumEventsPerScan), cancellationToken);
                    _store.AddBatch(batch);
                    hasMore = batch.HasMore;
                }
            }
            lock (_statusSync) { _lastSuccessfulScan = DateTimeOffset.UtcNow; _lastError = null; }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            lock (_statusSync) _lastError = exception.Message;
            _logger.LogError(exception, "Windows diagnostic scan failed; sensor monitoring is unaffected");
        }
    }
}
