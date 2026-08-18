using PCMonitor.Application.Models.Api;
using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;

namespace PCMonitor.Application.Services.Notifications;

public sealed class NotificationRegistrationService(
    IAppSettingsService settings,
    MonitorApiClient api,
    IPushTokenProvider tokens)
{
    private readonly SemaphoreSlim _sync = new(1, 1);

    public bool IsAvailable => tokens.IsAvailable;

    public async Task<NotificationRegistrationResult> EnableAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (!tokens.IsAvailable)
                return new(false, "This build does not contain Firebase configuration.");
            var status = await api.GetNotificationStatusAsync(cancellationToken);
            if (!status.Enabled || !status.Configured)
                return new(false, "Push notifications are not enabled on the PC service.");
            var token = await tokens.RequestPermissionAndGetTokenAsync(cancellationToken);
            var installationId = await settings.GetNotificationInstallationIdAsync();
            await api.RegisterNotificationDeviceAsync(new(installationId, token, PlatformName(),
                DeviceInfo.Current.Name), cancellationToken);
            await settings.SetNotificationsEnabledAsync(true);
            return new(true, $"Notifications enabled for {status.MinimumSeverity.ToLowerInvariant()} alerts.");
        }
        finally { _sync.Release(); }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await settings.GetNotificationsEnabledAsync() || !tokens.IsAvailable) return;
        await EnableAsync(cancellationToken);
    }

    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var installationId = await settings.GetNotificationInstallationIdAsync();
            try { await api.UnregisterNotificationDeviceAsync(installationId, cancellationToken); }
            finally { await settings.SetNotificationsEnabledAsync(false); }
        }
        finally { _sync.Release(); }
    }

    private static string PlatformName() => DeviceInfo.Current.Platform == DevicePlatform.iOS ? "ios" : "android";
}

public sealed record NotificationRegistrationResult(bool Enabled, string Message);
