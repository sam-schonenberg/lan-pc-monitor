using System.Threading.Channels;
using Microsoft.Extensions.Options;
using PCMonitor.Service.Alerts;

namespace PCMonitor.Service.Notifications;

public interface INotificationDispatcher
{
    void Enqueue(MonitorAlert alert);
}

public sealed class NotificationDispatcher : BackgroundService, INotificationDispatcher
{
    private readonly Channel<MonitorAlert> _queue = Channel.CreateBounded<MonitorAlert>(
        new BoundedChannelOptions(128) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly DeviceRegistrationStore _devices;
    private readonly IPushNotificationProvider _provider;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationDispatcher> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, DateTimeOffset> _lastPushBySource = new(StringComparer.OrdinalIgnoreCase);

    public NotificationDispatcher(DeviceRegistrationStore devices, IPushNotificationProvider provider,
        IOptions<NotificationOptions> options, ILogger<NotificationDispatcher> logger, TimeProvider timeProvider)
    {
        _devices = devices; _provider = provider; _options = options.Value; _logger = logger; _timeProvider = timeProvider;
    }

    public void Enqueue(MonitorAlert alert)
    {
        if (_options.Enabled && alert.Severity >= _options.MinimumSeverity && !_queue.Writer.TryWrite(alert))
            _logger.LogWarning("Notification queue is unavailable; alert {AlertId} will not be pushed", alert.Id);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var alert in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (!_provider.IsConfigured)
            {
                _logger.LogWarning("Notification delivery is enabled but the relay is not fully configured");
                continue;
            }
            var devices = _devices.GetAll();
            if (devices.Count == 0) continue;
            var isTest = alert.SensorId.StartsWith("/test/", StringComparison.OrdinalIgnoreCase);
            var now = _timeProvider.GetUtcNow();
            var interval = TimeSpan.FromSeconds(Math.Max(0, _options.MinimumIntervalSeconds));
            // Include the rule/display name and severity so an unrelated alert, or an escalation
            // from warning to critical, is never hidden by a noisy sensor.
            var sourceKey = $"{alert.SensorId}|{alert.SensorName}|{alert.Severity}";
            if (!isTest && _lastPushBySource.TryGetValue(sourceKey, out var lastPushAt) && now - lastPushAt < interval)
            {
                _logger.LogInformation("Suppressed duplicate push for alert {AlertId}; source {Source} is rate limited",
                    alert.Id, sourceKey);
                continue;
            }
            if (!isTest)
            {
                _lastPushBySource[sourceKey] = now;
                foreach (var expired in _lastPushBySource.Where(entry => now - entry.Value >= interval).Select(entry => entry.Key).ToArray())
                    _lastPushBySource.Remove(expired);
            }
            foreach (var device in devices)
            {
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        if (await _provider.SendAsync(device, alert, stoppingToken) == PushDeliveryResult.InvalidDestination)
                        {
                            _devices.Remove(device.InstallationId);
                            _logger.LogInformation("Removed an expired relay destination for installation {InstallationId}",
                                device.InstallationId);
                        }
                        break;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception exception) when (attempt < 3)
                    {
                        _logger.LogWarning(exception,
                            "Notification attempt {Attempt} failed for installation {InstallationId}",
                            attempt, device.InstallationId);
                        await Task.Delay(TimeSpan.FromSeconds(attempt * 2), stoppingToken);
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception,
                            "Could not deliver alert {AlertId} to installation {InstallationId}",
                            alert.Id, device.InstallationId);
                    }
                }
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    { _queue.Writer.TryComplete(); await base.StopAsync(cancellationToken); }
}
