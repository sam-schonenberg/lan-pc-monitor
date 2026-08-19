using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCMonitor.Service.Models;
using PCMonitor.Service.Sensors;
using PCMonitor.Service.Services;
using PCMonitor.Service.SessionDetection;
using PCMonitor.Service.History;
using PCMonitor.Service.Alerts;
using PCMonitor.Service.Notifications;
using PCMonitor.Service.Api;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "appsettings.json")),
    optional: true,
    reloadOnChange: true);

if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddWindowsService(options => options.ServiceName = "PCMonitor");
}
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection(MonitoringOptions.SectionName));
builder.Services.Configure<SessionDetectionOptions>(builder.Configuration.GetSection(SessionDetectionOptions.SectionName));
builder.Services.Configure<HistoricalMonitoringOptions>(builder.Configuration.GetSection(HistoricalMonitoringOptions.SectionName));
builder.Services.Configure<ProcessMonitoringOptions>(builder.Configuration.GetSection(ProcessMonitoringOptions.SectionName));
builder.Services.Configure<AlertOptions>(builder.Configuration.GetSection(AlertOptions.SectionName));
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.PostConfigure<NotificationOptions>(options =>
{
    // v0.1.2 and earlier preserved a direct-Firebase configuration without RelayBaseUrl.
    if (!string.IsNullOrWhiteSpace(options.RelayBaseUrl)) return;
    options.RelayBaseUrl = NotificationOptions.DefaultRelayBaseUrl;
    options.Enabled = true;
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "LAN PC Monitor API";
        document.Info.Version = "v1";
        document.Info.Description = "Read PC hardware telemetry, retained history, sessions, alerts, and manage optional mobile push registrations on a trusted LAN.";
        return Task.CompletedTask;
    });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.AddSingleton<ISensorProvider, LibreHardwareMonitorSensorProvider>();
builder.Services.AddSingleton<SensorSnapshotStore>();
builder.Services.AddSingleton<SetupPageService>();
builder.Services.AddSingleton<LoadSensorSelector>();
builder.Services.AddSingleton<SessionRuntimeContext>();
builder.Services.AddSingleton<LoadSessionDetector>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<HistoricalHistoryStore>();
builder.Services.AddSingleton<HistoricalSensorAggregator>();
builder.Services.AddSingleton<LiveEventHub>();
builder.Services.AddSingleton<AlertStore>();
builder.Services.AddSingleton<CustomAlertRuleStore>();
builder.Services.AddSingleton<AlertEvaluator>();
builder.Services.AddSingleton<DeviceRegistrationStore>();
builder.Services.AddHttpClient<IPushNotificationProvider, NotificationRelayPushProvider>();
builder.Services.AddSingleton<NotificationDispatcher>();
builder.Services.AddSingleton<INotificationDispatcher>(serviceProvider =>
    serviceProvider.GetRequiredService<NotificationDispatcher>());
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<NotificationDispatcher>());
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<HistoricalHistoryStore>());
builder.Services.AddHostedService<ProcessMonitoringService>();
builder.Services.AddHostedService<SensorMonitoringService>();

var port = builder.Configuration.GetValue("Server:Port", 5005);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();
app.UseResponseCompression();
app.UseWebSockets();
app.MapOpenApi("/openapi/{documentName}.json");
app.MapOpenApi("/openapi/{documentName}.yaml");
app.MapPublicApi();

app.MapGet("/setup", (SetupPageService setupPage) => Results.Content(
    setupPage.CreateHtml(),
    "text/html; charset=utf-8")).ExcludeFromDescription();

app.MapGet("/setup/qr.svg", (SetupPageService setupPage) => Results.Content(
    setupPage.CreateQrSvg(),
    "image/svg+xml; charset=utf-8")).ExcludeFromDescription();

var sensorWebSocketHandler = async (HttpContext context, SensorSnapshotStore snapshots, LiveEventHub events,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("SensorWebSocket");
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    logger.LogInformation("WebSocket client connected from {RemoteAddress}", context.Connection.RemoteIpAddress);

    try
    {
        async Task SendAsync(LiveEventEnvelope message)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(message, app.Services
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
                .Value.SerializerOptions);
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, context.RequestAborted);
        }

        await SendAsync(new LiveEventEnvelope("sensors", snapshots.Current));
        using var subscription = events.Subscribe();
        await foreach (var message in subscription.Events.ReadAllAsync(context.RequestAborted))
        {
            if (socket.State != WebSocketState.Open) break;
            await SendAsync(message);
        }
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
    }
    catch (WebSocketException exception)
    {
        logger.LogDebug(exception, "WebSocket client disconnected unexpectedly");
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Unexpected WebSocket error");
    }
    finally
    {
        logger.LogInformation("WebSocket client disconnected from {RemoteAddress}", context.Connection.RemoteIpAddress);
    }
};
app.MapGet("/api/v1/ws/sensors", sensorWebSocketHandler).ExcludeFromDescription();
app.MapGet("/ws/sensors", sensorWebSocketHandler).ExcludeFromDescription();

app.Logger.LogInformation("PCMonitor starting on port {Port}", port);
await app.RunAsync();
