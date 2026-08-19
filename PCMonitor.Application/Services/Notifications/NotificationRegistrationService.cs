using PCMonitor.Application.Models.Api;
using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;

namespace PCMonitor.Application.Services.Notifications;

public sealed class NotificationRegistrationService(
    IAppSettingsService settings,
    MonitorApiClient api,
    IPushTokenProvider tokens,
    NotificationRelayClient relay,
    RelayInstallationStore relayStore)
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
            var token = await tokens.RequestPermissionAndGetTokenAsync(cancellationToken);
            var installation = await relayStore.GetAsync();
            if (installation is null || !await relay.UpdateTokenAsync(installation, token, cancellationToken))
            {
                installation = await relay.CreateInstallationAsync(token, cancellationToken);
                await relayStore.SaveAsync(installation);
            }
            await api.RegisterNotificationDeviceAsync(new(installation.InstallationId, installation.SendSecret,
                PlatformName(),
                DeviceInfo.Current.Name), cancellationToken);
            await settings.SetNotificationsEnabledAsync(true);
            return new(true, "Critical alerts will be delivered through the notification relay.");
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
            var installation = await relayStore.GetAsync();
            try
            {
                if (installation is not null)
                    await api.UnregisterNotificationDeviceAsync(installation.InstallationId, cancellationToken);
            }
            finally
            {
                try
                {
                    if (installation is not null)
                        await relay.DeleteInstallationAsync(installation, cancellationToken);
                }
                finally
                {
                    relayStore.Clear();
                    await settings.SetNotificationsEnabledAsync(false);
                }
            }
        }
        finally { _sync.Release(); }
    }

    private static string PlatformName() => DeviceInfo.Current.Platform == DevicePlatform.iOS ? "ios" : "android";
}

public sealed record NotificationRegistrationResult(bool Enabled, string Message);
