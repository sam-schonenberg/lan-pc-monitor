using System.Text.Json;

namespace LanPcMonitor.Tray.Services;

internal static class TrayConfiguration
{
    public static int GetPort()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.GetProperty("Server").GetProperty("Port").GetInt32() is var port and > 0 and <= 65535
                ? port
                : 5005;
        }
        catch
        {
            return 5005;
        }
    }
}
