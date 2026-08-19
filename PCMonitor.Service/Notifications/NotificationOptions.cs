using PCMonitor.Service.Alerts;

namespace PCMonitor.Service.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";
    public const string DefaultRelayBaseUrl = "https://138-201-94-167.sslip.io/";
    public bool Enabled { get; set; }
    public AlertSeverity MinimumSeverity { get; set; } = AlertSeverity.Critical;
    public int MinimumIntervalSeconds { get; set; } = 60;
    public string RelayBaseUrl { get; set; } = "";
    public string DeviceStoreFile { get; set; } = "";
}
