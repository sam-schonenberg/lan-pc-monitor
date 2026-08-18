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

    public NotificationDispatcher(DeviceRegistrationStore devices, IPushNotificationProvider provider,
        IOptions<NotificationOptions> options, ILogger<NotificationDispatcher> logger)
    {
        _devices = devices; _provider = provider; _options = options.Value; _logger = logger;
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
                _logger.LogWarning("Notification delivery is enabled but Firebase is not fully configured");
                continue;
            }
            foreach (var device in _devices.GetAll())
            {
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        if (await _provider.SendAsync(device, alert, stoppingToken) == PushDeliveryResult.InvalidToken)
                        {
                            _devices.RemoveByToken(device.Token);
                            _logger.LogInformation("Removed an expired push token for installation {InstallationId}",
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
