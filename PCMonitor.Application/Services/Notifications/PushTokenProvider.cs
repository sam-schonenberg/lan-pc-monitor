#if (ANDROID || IOS) && FIREBASE_CONFIGURED
using Plugin.Firebase.CloudMessaging;
#endif

namespace PCMonitor.Application.Services.Notifications;

public sealed class PushTokenProvider : IPushTokenProvider
{
#if (ANDROID || IOS) && FIREBASE_CONFIGURED
    public bool IsAvailable => true;

    public async Task<string> RequestPermissionAndGetTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await CrossFirebaseCloudMessaging.Current.CheckIfValidAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var token = await CrossFirebaseCloudMessaging.Current.GetTokenAsync();
        return string.IsNullOrWhiteSpace(token)
            ? throw new InvalidOperationException("Firebase did not provide a notification token.") : token;
    }
#else
    public bool IsAvailable => false;

    public Task<string> RequestPermissionAndGetTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<string>(new NotSupportedException(
            "Push notifications are not configured in this app build."));
#endif
}
