using PCMonitor.Service.Alerts;

namespace PCMonitor.Service.Notifications;

public interface IPushNotificationProvider
{
    bool IsConfigured { get; }
    Task<PushDeliveryResult> SendAsync(DeviceRegistration device, MonitorAlert alert,
        CancellationToken cancellationToken);
}
