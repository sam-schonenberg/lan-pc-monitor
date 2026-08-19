using PCMonitor.Service.Alerts;

namespace PCMonitor.Service.Notifications;

public enum MobilePlatform
{
    Android,
    Ios
}

public sealed record DeviceRegistrationRequest(
    string InstallationId,
    string SendSecret,
    MobilePlatform Platform,
    string? DeviceName);

public sealed record DeviceRegistration(
    string InstallationId,
    string SendSecret,
    MobilePlatform Platform,
    string? DeviceName,
    DateTimeOffset UpdatedAt);

public sealed record DeviceRegistrationResponse(
    string InstallationId,
    MobilePlatform Platform,
    string? DeviceName,
    DateTimeOffset UpdatedAt);

public sealed record NotificationStatus(
    bool Enabled,
    bool Configured,
    int RegisteredDevices,
    AlertSeverity MinimumSeverity);

public enum PushDeliveryResult
{
    Delivered,
    InvalidDestination
}
