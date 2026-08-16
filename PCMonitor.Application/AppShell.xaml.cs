using Microsoft.Extensions.DependencyInjection;
using PCMonitor.Application.Services.Sync;

namespace PCMonitor.Application;

public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services, AppConnectionService connection,
        IHistoryBackgroundScheduler historyBackgroundScheduler)
    {
        InitializeComponent();
        var tabs = new TabBar();
        tabs.Items.Add(Tab("Dashboard", "layout_dashboard.svg", () => services.GetRequiredService<Views.DashboardPage>()));
        tabs.Items.Add(Tab("History", "rotate_ccw_clock.svg", () => services.GetRequiredService<Views.HistoryPage>()));
        tabs.Items.Add(Tab("Alerts", "siren.svg", () => services.GetRequiredService<Views.AlertsPage>()));
        tabs.Items.Add(Tab("Settings", "settings.svg", () => services.GetRequiredService<Views.SettingsPage>()));
        Items.Add(tabs);
        historyBackgroundScheduler.EnsurePeriodicBackfill();
        _ = connection.StartAsync();
    }

    private static ShellContent Tab(string title, string icon, Func<Page> factory) => new()
    {
        Title = title,
        Icon = ImageSource.FromFile(icon),
        ContentTemplate = new DataTemplate(factory)
    };
}
