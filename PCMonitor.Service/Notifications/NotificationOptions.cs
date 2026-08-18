using PCMonitor.Service.Alerts;

namespace PCMonitor.Service.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";
    public bool Enabled { get; set; }
    public AlertSeverity MinimumSeverity { get; set; } = AlertSeverity.Critical;
    public string FirebaseProjectId { get; set; } = "";
    public string FirebaseServiceAccountFile { get; set; } = "";
    public string DeviceStoreFile { get; set; } = "";
}
