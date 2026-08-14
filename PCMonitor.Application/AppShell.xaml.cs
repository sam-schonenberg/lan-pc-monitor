using Microsoft.Extensions.DependencyInjection;
using PCMonitor.Application.Services.Sync;

namespace PCMonitor.Application;

public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services, AppConnectionService connection)
    {
        InitializeComponent();
        var tabs = new TabBar();
        tabs.Items.Add(Tab("Dashboard", () => services.GetRequiredService<Views.DashboardPage>()));
        tabs.Items.Add(Tab("History", () => services.GetRequiredService<Views.HistoryPage>()));
        tabs.Items.Add(Tab("Alerts", () => services.GetRequiredService<Views.AlertsPage>()));
        tabs.Items.Add(Tab("Settings", () => services.GetRequiredService<Views.SettingsPage>()));
        Items.Add(tabs);
        _ = connection.StartAsync();
    }

    private static ShellContent Tab(string title, Func<Page> factory) => new() { Title = title, ContentTemplate = new DataTemplate(factory) };
}
