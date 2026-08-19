using Microsoft.Extensions.Logging;
using PCMonitor.Application.Data;
using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Services.Sync;
using PCMonitor.Application.ViewModels;
using PCMonitor.Application.Views;
using LiveChartsCore.SkiaSharpView.Maui;
using SkiaSharp.Views.Maui.Controls.Hosting;
using PCMonitor.Application.Services;
using ZXing.Net.Maui.Controls;
using Microsoft.Maui.LifecycleEvents;
using PCMonitor.Application.Services.Notifications;
#if ANDROID && FIREBASE_CONFIGURED
using Plugin.Firebase.Core.Platforms.Android;
#elif IOS && FIREBASE_CONFIGURED
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.Core.Platforms.iOS;
#endif

namespace PCMonitor.Application;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().UseBarcodeReader().UseSkiaSharp().UseLiveCharts().ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });
#if ANDROID && FIREBASE_CONFIGURED
        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android => android.OnCreate((activity, _) =>
            CrossFirebase.Initialize(activity, () => Platform.CurrentActivity!))));
#elif IOS && FIREBASE_CONFIGURED
        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios.WillFinishLaunching((_, _) =>
        {
            CrossFirebase.Initialize();
            FirebaseCloudMessagingImplementation.Initialize();
            return false;
        })));
#endif
        builder.Services.AddSingleton<AppDatabase>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
        builder.Services.AddSingleton<AlertRepository>();
        builder.Services.AddSingleton<HistoryRepository>();
        builder.Services.AddSingleton<DashboardWidgetRepository>();
        builder.Services.AddSingleton<MonitorApiClient>();
        builder.Services.AddSingleton<MonitorWebSocketClient>();
        builder.Services.AddSingleton<IPushTokenProvider, PushTokenProvider>();
        builder.Services.AddSingleton<NotificationRelayClient>();
        builder.Services.AddSingleton<RelayInstallationStore>();
        builder.Services.AddSingleton<NotificationRegistrationService>();
        builder.Services.AddSingleton<CurrentSensorStateService>();
#if ANDROID
        builder.Services.AddSingleton<IHistoryBackgroundScheduler, Platforms.Android.AndroidHistoryBackgroundScheduler>();
#else
        builder.Services.AddSingleton<IHistoryBackgroundScheduler, NoOpHistoryBackgroundScheduler>();
#endif
        builder.Services.AddSingleton<HistorySyncService>();
        builder.Services.AddSingleton<ForegroundHistorySyncCoordinator>();
        builder.Services.AddSingleton<AlertSyncService>();
        builder.Services.AddSingleton<AppConnectionService>();
        builder.Services.AddTransient<SetupViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<HistoryViewModel>();
        builder.Services.AddTransient<AlertsViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SetupPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<HistoryPage>();
        builder.Services.AddTransient<AlertsPage>();
        builder.Services.AddTransient<AlertRulesPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<AppShell>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
