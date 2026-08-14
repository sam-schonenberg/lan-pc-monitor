using System.Diagnostics;
using LanPcMonitor.Tray.Services;

namespace LanPcMonitor.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string ServiceName = "PCMonitor";
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly ToolStripMenuItem _restartItem;
    private readonly ToolStripMenuItem _enableItem;
    private readonly ToolStripMenuItem _disableItem;
    private readonly ServiceStatusReader _statusReader = new(ServiceName);
    private readonly MaintenanceScriptRunner _scriptRunner = new();

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();
        var title = new ToolStripMenuItem("LAN PC Monitor") { Enabled = false };
        _statusItem = new ToolStripMenuItem("Status: Checking…") { Enabled = false };
        _startItem = CreateAction("Start Service", "start-service.bat");
        _stopItem = CreateAction("Stop Service", "stop-service.bat");
        _restartItem = CreateAction("Restart Service", "restart-service.bat");
        _enableItem = CreateAction("Enable Automatic Startup", "enable-service.bat");
        _disableItem = CreateAction("Disable Service", "disable-service.bat");

        menu.Items.AddRange([
            title,
            _statusItem,
            new ToolStripSeparator(),
            _startItem,
            _stopItem,
            _restartItem,
            new ToolStripSeparator(),
            _enableItem,
            _disableItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Open Monitoring API", null, (_, _) => OpenUrl("api/sensors")),
            new ToolStripMenuItem("Open Status Endpoint", null, (_, _) => OpenUrl("status")),
            new ToolStripMenuItem("Open Setup & Pairing", null, (_, _) => OpenUrl("setup")),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Uninstall Service", null, UninstallService),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit Tray App", null, (_, _) => ExitThread())
        ]);
        menu.Opening += (_, _) => RefreshStatus();

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "LAN PC Monitor",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenUrl("setup");
        RefreshStatus();
    }

    private ToolStripMenuItem CreateAction(string text, string scriptName)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += async (_, _) => await RunScriptAsync(scriptName);
        return item;
    }

    private async Task RunScriptAsync(string scriptName)
    {
        SetActionsEnabled(false);
        var result = await _scriptRunner.RunElevatedAsync(scriptName);
        if (!result.Succeeded && !result.WasCancelled)
        {
            MessageBox.Show(result.ErrorMessage, "LAN PC Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        RefreshStatus();
    }

    private async void UninstallService(object? sender, EventArgs e)
    {
        var answer = MessageBox.Show(
            "Are you sure you want to uninstall LAN PC Monitor?\n\nThis will stop and remove the Windows service and its firewall rule.",
            "Uninstall LAN PC Monitor",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes)
        {
            await RunScriptAsync("uninstall-service.bat");
        }
    }

    private void RefreshStatus()
    {
        ServiceState state;
        try
        {
            state = _statusReader.GetState();
        }
        catch
        {
            state = ServiceState.Unknown;
        }

        _statusItem.Text = $"Status: {FormatState(state)}";
        _notifyIcon.Text = $"LAN PC Monitor — {FormatState(state)}";
        var installed = state != ServiceState.NotInstalled;
        var pending = state is ServiceState.StartPending or ServiceState.StopPending or
            ServiceState.PausePending or ServiceState.ContinuePending;
        _startItem.Enabled = installed && state == ServiceState.Stopped;
        _stopItem.Enabled = installed && state is ServiceState.Running or ServiceState.StartPending;
        _restartItem.Enabled = installed && state == ServiceState.Running;
        _enableItem.Enabled = installed && state == ServiceState.Disabled;
        _disableItem.Enabled = installed && state != ServiceState.Disabled && !pending;
    }

    private void SetActionsEnabled(bool enabled)
    {
        _startItem.Enabled = enabled;
        _stopItem.Enabled = enabled;
        _restartItem.Enabled = enabled;
        _enableItem.Enabled = enabled;
        _disableItem.Enabled = enabled;
    }

    private static string FormatState(ServiceState state) => state switch
    {
        ServiceState.StartPending => "Starting",
        ServiceState.StopPending => "Stopping",
        ServiceState.PausePending => "Pausing",
        ServiceState.ContinuePending => "Resuming",
        ServiceState.NotInstalled => "Not Installed",
        _ => state.ToString()
    };

    private static void OpenUrl(string path)
    {
        try
        {
            var port = TrayConfiguration.GetPort();
            Process.Start(new ProcessStartInfo($"http://localhost:{port}/{path}") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Could not open the browser: {exception.Message}", "LAN PC Monitor",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        base.ExitThreadCore();
    }
}
