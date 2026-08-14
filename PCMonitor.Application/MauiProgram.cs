using Microsoft.Extensions.Logging;
using PCMonitor.Application.Data;
using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Services.Sync;
using PCMonitor.Application.ViewModels;
using PCMonitor.Application.Views;

namespace PCMonitor.Application;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });
        builder.Services.AddSingleton<AppDatabase>();
        builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
        builder.Services.AddSingleton<AlertRepository>();
        builder.Services.AddSingleton<HistoryRepository>();
        builder.Services.AddSingleton<MonitorApiClient>();
        builder.Services.AddSingleton<MonitorWebSocketClient>();
        builder.Services.AddSingleton<HistorySyncService>();
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
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<AppShell>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
