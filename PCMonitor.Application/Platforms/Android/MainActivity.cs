using Android.App;
using Android.Content.PM;
using Android.OS;
#if FIREBASE_CONFIGURED
using Android.Content;
using Plugin.Firebase.CloudMessaging;
#endif

namespace PCMonitor.Application
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
#if FIREBASE_CONFIGURED
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            FirebaseCloudMessagingImplementation.OnNewIntent(Intent);
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                var channelId = $"{PackageName}.critical-alerts";
                var manager = (NotificationManager)GetSystemService(NotificationService)!;
                manager.CreateNotificationChannel(new NotificationChannel(channelId, "Critical sensor alerts",
                    NotificationImportance.High) { Description = "Critical hardware alerts from LAN PC Monitor" });
                FirebaseCloudMessagingImplementation.ChannelId = channelId;
            }
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            if (intent is not null) FirebaseCloudMessagingImplementation.OnNewIntent(intent);
        }
#endif
    }
}
