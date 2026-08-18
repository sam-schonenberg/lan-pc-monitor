using Microsoft.Extensions.DependencyInjection;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Services.Sync;
using PCMonitor.Application.Views;
using PCMonitor.Application.Services.Notifications;

namespace PCMonitor.Application;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider _services;
    private readonly IAppSettingsService _settings;
    private readonly ForegroundHistorySyncCoordinator _foregroundSync;
    private readonly NotificationRegistrationService _notifications;
    public App(IServiceProvider services, IAppSettingsService settings,
        ForegroundHistorySyncCoordinator foregroundSync, NotificationRegistrationService notifications)
    {
        InitializeComponent();
        _services = services;
        _settings = settings;
        _foregroundSync = foregroundSync;
        _notifications = notifications;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new ContentPage { Content = new ActivityIndicator { IsRunning = true, VerticalOptions = LayoutOptions.Center } });
        window.Activated += (_, _) =>
        {
            _ = _foregroundSync.SynchronizeAsync(window);
            _ = RefreshNotificationsAsync();
        };
        _ = SelectInitialPageAsync(window);
        return window;
    }

    private async Task SelectInitialPageAsync(Window window)
    {
        try
        {
            var endpoint = await _settings.GetApiBaseUrlAsync();
            await MainThread.InvokeOnMainThreadAsync(() => window.Page = string.IsNullOrWhiteSpace(endpoint)
                ? _services.GetRequiredService<SetupPage>() : _services.GetRequiredService<AppShell>());
            _ = _foregroundSync.SynchronizeAsync(window);
        }
        catch (Exception exception)
        {
            await MainThread.InvokeOnMainThreadAsync(() => window.Page = new ContentPage
            {
                Content = new VerticalStackLayout
                {
                    Padding = 28,
                    Spacing = 16,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = "LAN PC Monitor could not initialize", FontSize = 24, FontAttributes = FontAttributes.Bold },
                        new Label { Text = exception.Message },
                        new Label { Text = "Close the app and try again. If this persists, clear the app's local data." }
                    }
                }
            });
        }
    }

    private async Task RefreshNotificationsAsync()
    {
        try { await _notifications.RefreshAsync(); }
        catch { /* Registration will be retried the next time the app becomes active. */ }
    }
}
