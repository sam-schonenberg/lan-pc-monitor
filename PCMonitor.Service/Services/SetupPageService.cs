using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Encodings.Web;
using QRCoder;

namespace PCMonitor.Service.Services;

public sealed class SetupPageService(IConfiguration configuration)
{
    public string CreateQrSvg()
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(GetLanServiceUrl(), QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new SvgQRCode(data);
        return qrCode.GetGraphic(8, "#111827", "#ffffff", true);
    }

    public string CreateHtml()
    {
        var encoder = HtmlEncoder.Default;
        var serviceUrl = GetLanServiceUrl();
        var machineName = Environment.MachineName;
        var appStoreUrl = configuration["Setup:AppStoreUrl"];
        var playStoreUrl = configuration["Setup:GooglePlayUrl"];
        var storeLinks = CreateStoreLinks(appStoreUrl, playStoreUrl, encoder);

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Connect to PCMonitor</title>
              <style>
                :root { color-scheme: light dark; font-family: system-ui, -apple-system, "Segoe UI", sans-serif; }
                body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #0f172a; color: #e2e8f0; }
                main { width: min(92vw, 520px); box-sizing: border-box; padding: 2.5rem; text-align: center; background: #1e293b; border: 1px solid #334155; border-radius: 24px; box-shadow: 0 24px 70px #02061780; }
                h1 { margin: 0 0 .5rem; font-size: 2rem; }
                .subtitle { margin: 0 0 1.5rem; color: #94a3b8; }
                .qr { display: block; width: min(72vw, 280px); margin: 0 auto 1.5rem; padding: 14px; background: white; border-radius: 16px; }
                .status { display: inline-flex; align-items: center; gap: .5rem; margin-bottom: 1rem; color: #86efac; font-weight: 600; }
                .status::before { content: ""; width: .65rem; height: .65rem; border-radius: 50%; background: #22c55e; }
                code { display: block; padding: .8rem; overflow-wrap: anywhere; color: #bae6fd; background: #0f172a; border-radius: 10px; }
                .hint { color: #cbd5e1; line-height: 1.5; }
                .stores { display: flex; justify-content: center; flex-wrap: wrap; gap: .75rem; margin-top: 1.25rem; }
                .stores a { padding: .7rem 1rem; color: white; text-decoration: none; background: #2563eb; border-radius: 9px; font-weight: 600; }
              </style>
            </head>
            <body>
              <main>
                <div class="status">PCMonitor is running</div>
                <h1>Connect to {{encoder.Encode(machineName)}}</h1>
                <p class="subtitle">Scan this QR code from a device on the same network.</p>
                <img class="qr" src="/setup/qr.svg" alt="QR code for {{encoder.Encode(serviceUrl)}}">
                <p class="hint">The QR code currently opens this PC's local setup address:</p>
                <code>{{encoder.Encode(serviceUrl)}}</code>
                {{storeLinks}}
              </main>
            </body>
            </html>
            """;
    }

    private string GetLanServiceUrl()
    {
        var port = configuration.GetValue("Server:Port", 5005);
        var address = GetLanAddress()?.ToString() ?? "localhost";
        return $"http://{address}:{port}/setup";
    }

    private static IPAddress? GetLanAddress()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                              network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderByDescending(network => network.GetIPProperties().GatewayAddresses.Count > 0)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
                              !IPAddress.IsLoopback(address.Address))
            .Select(address => address.Address)
            .FirstOrDefault();
    }

    private static string CreateStoreLinks(string? appStoreUrl, string? playStoreUrl, HtmlEncoder encoder)
    {
        var links = new List<string>();
        if (Uri.TryCreate(appStoreUrl, UriKind.Absolute, out var appStore) && appStore.Scheme is "https")
        {
            links.Add($"<a href=\"{encoder.Encode(appStore.AbsoluteUri)}\">Download on the App Store</a>");
        }

        if (Uri.TryCreate(playStoreUrl, UriKind.Absolute, out var playStore) && playStore.Scheme is "https")
        {
            links.Add($"<a href=\"{encoder.Encode(playStore.AbsoluteUri)}\">Get it on Google Play</a>");
        }

        return links.Count == 0 ? string.Empty : $"<div class=\"stores\">{string.Join(string.Empty, links)}</div>";
    }
}
