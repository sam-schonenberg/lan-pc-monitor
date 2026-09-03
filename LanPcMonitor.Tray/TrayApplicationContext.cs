using System.Diagnostics;
using LanPcMonitor.Tray.Services;
using Microsoft.Win32;

namespace LanPcMonitor.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string ServiceName = "PCMonitor";
    private readonly Icon? _applicationIcon;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly ToolStripMenuItem _restartItem;
    private readonly ToolStripMenuItem _testNotificationItem;
    private readonly ToolStripMenuItem _serviceStartupItem;
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
        _testNotificationItem = new ToolStripMenuItem("Test GPU Overheating Notification");
        _testNotificationItem.Click += async (_, _) => await SendTestNotificationAsync();
        _serviceStartupItem = new ToolStripMenuItem("Service / Auto Startup");
        _serviceStartupItem.Click += ToggleServiceStartup;

        menu.Items.AddRange([
            title,
            _statusItem,
            new ToolStripSeparator(),
            _startItem,
            _stopItem,
            _restartItem,
            new ToolStripSeparator(),
            _serviceStartupItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Open Monitoring API", null, (_, _) => OpenUrl("api/sensors")),
            new ToolStripMenuItem("Open Status Endpoint", null, (_, _) => OpenUrl("status")),
            new ToolStripMenuItem("Open Windows Diagnostics", null, (_, _) => OpenUrl("diagnostics")),
            new ToolStripMenuItem("Open Setup & Pairing", null, (_, _) => OpenUrl("setup")),
            new ToolStripSeparator(),
            _testNotificationItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("About LAN PC Monitor", null, (_, _) => ShowAboutWindow()),
            new ToolStripMenuItem("Uninstall LAN PC Monitor…", null, UninstallApplication),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit Tray App", null, (_, _) => ExitThread())
        ]);
        menu.Opening += (_, _) => RefreshStatus();

        _applicationIcon = Environment.ProcessPath is { } processPath
            ? Icon.ExtractAssociatedIcon(processPath)
            : null;
        _notifyIcon = new NotifyIcon
        {
            Icon = _applicationIcon ?? SystemIcons.Information,
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

    private async Task SendTestNotificationAsync()
    {
        _testNotificationItem.Enabled = false;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var port = TrayConfiguration.GetPort();
            using var response = await client.PostAsync(
                $"http://localhost:{port}/api/v1/notifications/test-overheating", null);
            response.EnsureSuccessStatusCode();
            _notifyIcon.ShowBalloonTip(3000, "LAN PC Monitor",
                "GPU overheating test notification queued.", ToolTipIcon.Info);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Could not send the test notification: {exception.Message}", "LAN PC Monitor",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            RefreshStatus();
        }
    }

    private void ShowAboutWindow()
    {
        using var about = new AboutForm(_applicationIcon);
        about.ShowDialog();
    }

    private void UninstallApplication(object? sender, EventArgs e)
    {
        var answer = MessageBox.Show(
            "Are you sure you want to uninstall LAN PC Monitor?\n\n" +
            "Windows Installer will remove the application, service, firewall rule, and shortcuts. " +
            "Your configuration and monitoring history will be preserved.",
            "Uninstall LAN PC Monitor",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var productCode = FindInstalledProductCode();
            if (productCode is null)
            {
                MessageBox.Show(
                    "LAN PC Monitor could not be found in Windows Installed Apps. " +
                    "You can still uninstall it from Windows Settings > Apps > Installed apps.",
                    "Uninstall LAN PC Monitor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/x {productCode}",
                UseShellExecute = true
            });
            if (process is null)
            {
                throw new InvalidOperationException("Windows Installer could not be started.");
            }

            ExitThread();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Could not start Windows Installer: {exception.Message}",
                "Uninstall LAN PC Monitor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string? FindInstalledProductCode()
    {
        const string uninstallRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
            using var uninstallKey = localMachine.OpenSubKey(uninstallRegistryPath);
            if (uninstallKey is null)
            {
                continue;
            }

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                using var productKey = uninstallKey.OpenSubKey(subKeyName);
                if (productKey is null ||
                    !string.Equals(productKey.GetValue("DisplayName") as string, "LAN PC Monitor",
                        StringComparison.OrdinalIgnoreCase) ||
                    productKey.GetValue("WindowsInstaller") is not int windowsInstaller || windowsInstaller != 1 ||
                    !Guid.TryParse(subKeyName, out var productCode))
                {
                    continue;
                }

                return productCode.ToString("B");
            }
        }

        return null;
    }

    private async void ToggleServiceStartup(object? sender, EventArgs e)
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

        if (state is ServiceState.NotInstalled or ServiceState.Unknown)
        {
            RefreshStatus();
            return;
        }

        var scriptName = state == ServiceState.Disabled
            ? "enable-service.bat"
            : "disable-service.bat";
        await RunScriptAsync(scriptName);
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
        _testNotificationItem.Enabled = installed && state == ServiceState.Running;
        _serviceStartupItem.Text = state == ServiceState.Disabled
            ? "Enable Service / Auto Startup"
            : "Disable Service / Auto Startup";
        _serviceStartupItem.Enabled = installed && !pending && state != ServiceState.Unknown;
    }

    private void SetActionsEnabled(bool enabled)
    {
        _startItem.Enabled = enabled;
        _stopItem.Enabled = enabled;
        _restartItem.Enabled = enabled;
        _testNotificationItem.Enabled = enabled;
        _serviceStartupItem.Enabled = enabled;
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
        _applicationIcon?.Dispose();
        base.ExitThreadCore();
    }
}
