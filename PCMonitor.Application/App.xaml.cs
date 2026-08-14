using Microsoft.Extensions.DependencyInjection;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Views;

namespace PCMonitor.Application;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider _services;
    private readonly IAppSettingsService _settings;
    public App(IServiceProvider services, IAppSettingsService settings)
    {
        InitializeComponent();
        _services = services;
        _settings = settings;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new ContentPage { Content = new ActivityIndicator { IsRunning = true, VerticalOptions = LayoutOptions.Center } });
        _ = SelectInitialPageAsync(window);
        return window;
    }

    private async Task SelectInitialPageAsync(Window window)
    {
        var endpoint = await _settings.GetApiBaseUrlAsync();
        await MainThread.InvokeOnMainThreadAsync(() => window.Page = string.IsNullOrWhiteSpace(endpoint)
            ? _services.GetRequiredService<SetupPage>() : _services.GetRequiredService<AppShell>());
    }
}
