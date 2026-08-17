namespace LanPcMonitor.Tray;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\LanPcMonitor.Tray.SingleInstance";
    private const string DisableServiceAutoStartupArgument = "--disable-service-auto-start";

    [STAThread]
    private static async Task Main(string[] args)
    {
        if (args.Contains(DisableServiceAutoStartupArgument, StringComparer.OrdinalIgnoreCase))
        {
            var result = await new Services.MaintenanceScriptRunner().RunElevatedAsync("disable-service.bat");
            if (!result.Succeeded && !result.WasCancelled)
            {
                MessageBox.Show(result.ErrorMessage, "LAN PC Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return;
        }

        using var singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
