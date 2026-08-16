using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCMonitor.Service.Models;
using PCMonitor.Service.Sensors;
using PCMonitor.Service.Services;
using PCMonitor.Service.SessionDetection;
using PCMonitor.Service.History;
using PCMonitor.Service.Alerts;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);

if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddWindowsService(options => options.ServiceName = "PCMonitor");
}
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection(MonitoringOptions.SectionName));
builder.Services.Configure<SessionDetectionOptions>(builder.Configuration.GetSection(SessionDetectionOptions.SectionName));
builder.Services.Configure<HistoricalMonitoringOptions>(builder.Configuration.GetSection(HistoricalMonitoringOptions.SectionName));
builder.Services.Configure<ProcessMonitoringOptions>(builder.Configuration.GetSection(ProcessMonitoringOptions.SectionName));
builder.Services.Configure<AlertOptions>(builder.Configuration.GetSection(AlertOptions.SectionName));
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
builder.Services.AddSingleton<AlertEvaluator>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<HistoricalHistoryStore>());
builder.Services.AddHostedService<ProcessMonitoringService>();
builder.Services.AddHostedService<SensorMonitoringService>();

var port = builder.Configuration.GetValue("Server:Port", 5005);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();
app.UseResponseCompression();
app.UseWebSockets();

app.MapGet("/status", () => new ServiceStatus(
    "ok",
    "PCMonitor",
    Environment.MachineName,
    DateTimeOffset.UtcNow));

app.MapGet("/api/sensors", (SensorSnapshotStore snapshots) => snapshots.Current);
app.MapGet("/api/sensors/catalog", (HistoricalHistoryStore history) => Results.Ok(history.GetCatalog()));
app.MapGet("/api/history/manifest", (HttpContext context, HistoricalHistoryStore history) =>
{
    var manifest = history.GetManifest();
    var tag = $"\"{manifest.StreamId:N}-{manifest.CatalogVersion}-{manifest.NewestSequence ?? 0}-{manifest.BucketCount}\"";
    context.Response.Headers.ETag = tag;
    context.Response.Headers.CacheControl = "no-cache";
    if (context.Request.Headers.IfNoneMatch.Any(value => value == tag))
        return Results.StatusCode(StatusCodes.Status304NotModified);
    return Results.Ok(manifest);
});
app.MapGet("/api/session", (LoadSessionDetector detector) => detector.GetCurrent());
app.MapGet("/api/session/last", (LoadSessionDetector detector) => detector.GetLast());
app.MapGet("/api/alerts", (
    DateTimeOffset? from,
    AlertSeverity? severity,
    AlertStore alerts) => Results.Ok(alerts.Query(from, severity)));
app.MapGet("/api/history", (
    DateTimeOffset? from,
    DateTimeOffset? to,
    long? afterSequence,
    long? beforeSequence,
    int? limit,
    string? resolution,
    int[]? sensorId,
    Guid? sessionId,
    HistoricalHistoryStore history) =>
{
    if (from is not null && to is not null && from >= to)
    {
        return Results.BadRequest(new { error = "'from' must be earlier than 'to'." });
    }

    var parsedResolution = resolution?.ToLowerInvariant() switch
    {
        null or "minute" => HistoryResolution.Minute,
        "hour" => HistoryResolution.Hour,
        "day" => HistoryResolution.Day,
        _ => (HistoryResolution?)null
    };
    if (parsedResolution is null)
    {
        return Results.BadRequest(new { error = "'resolution' must be minute, hour, or day." });
    }
    if (afterSequence < 0 || beforeSequence < 0 || limit <= 0)
    {
        return Results.BadRequest(new { error = "'afterSequence' cannot be negative and 'limit' must be positive." });
    }

    return Results.Ok(history.QueryCompact(from, to, afterSequence, limit, parsedResolution.Value,
        sensorId, sessionId, beforeSequence));
});

app.MapGet("/setup", (SetupPageService setupPage) => Results.Content(
    setupPage.CreateHtml(),
    "text/html; charset=utf-8"));

app.MapGet("/setup/qr.svg", (SetupPageService setupPage) => Results.Content(
    setupPage.CreateQrSvg(),
    "image/svg+xml; charset=utf-8"));

app.Map("/ws/sensors", async (HttpContext context, SensorSnapshotStore snapshots, LiveEventHub events,
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
});

app.Logger.LogInformation("PCMonitor starting on port {Port}", port);
await app.RunAsync();
