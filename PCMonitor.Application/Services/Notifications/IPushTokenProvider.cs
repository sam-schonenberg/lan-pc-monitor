namespace PCMonitor.Application.Services.Notifications;

public interface IPushTokenProvider
{
    bool IsAvailable { get; }
    Task<string> RequestPermissionAndGetTokenAsync(CancellationToken cancellationToken = default);
}
