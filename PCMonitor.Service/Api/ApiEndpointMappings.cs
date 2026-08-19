using Microsoft.Extensions.Options;
using PCMonitor.Service.Alerts;
using PCMonitor.Service.History;
using PCMonitor.Service.Models;
using PCMonitor.Service.Notifications;
using PCMonitor.Service.Services;
using PCMonitor.Service.SessionDetection;

namespace PCMonitor.Service.Api;

public static class ApiEndpointMappings
{
    public static void MapPublicApi(this WebApplication app)
    {
        MapStatus(app.MapGet("/api/v1/status", Status), true);
        MapStatus(app.MapGet("/status", Status), false).ExcludeFromDescription();
        MapApiGroup(app.MapGroup("/api/v1"), true);
        MapApiGroup(app.MapGroup("/api").ExcludeFromDescription(), false);
    }

    private static RouteHandlerBuilder MapStatus(RouteHandlerBuilder endpoint, bool documented)
    {
        if (documented) endpoint.WithName("GetServiceStatus").WithTags("Service")
            .WithSummary("Get service status and API capabilities")
            .WithDescription("Use this endpoint for discovery and compatibility checks before calling other routes.")
            .Produces<ServiceStatus>();
        return endpoint;
    }

    private static ServiceStatus Status() => new("ok", "PCMonitor", Environment.MachineName,
        DateTimeOffset.UtcNow, typeof(ApiEndpointMappings).Assembly.GetName().Version?.ToString(3) ?? "0.0.0", "1",
        ["sensors", "history", "sessions", "alerts", "custom-alert-rules", "push-notifications", "websocket"]);

    private static void MapApiGroup(RouteGroupBuilder group, bool documented)
    {
        Describe(group.MapGet("/sensors", (SensorSnapshotStore snapshots) => snapshots.Current), documented,
            "GetSensors", "Sensors", "Get the latest sensor snapshot",
            "Returns the most recently collected complete snapshot without triggering a hardware poll.")
            .Produces<SensorSnapshot>();

        Describe(group.MapGet("/sensors/catalog", (HistoricalHistoryStore history) => history.GetCatalog()), documented,
            "GetSensorCatalog", "Sensors", "Get the history sensor catalog",
            "Maps compact numeric history sensor IDs to stable live sensor keys and metadata.")
            .Produces<SensorCatalogResponse>();

        Describe(group.MapGet("/history/manifest", HistoryManifest), documented,
            "GetHistoryManifest", "History", "Get the retained-history inventory",
            "Supports conditional requests with If-None-Match and returns 304 when unchanged.")
            .Produces<HistoryManifestResponse>().Produces(StatusCodes.Status304NotModified);

        Describe(group.MapGet("/session", (LoadSessionDetector detector) => detector.GetCurrent()), documented,
            "GetCurrentSession", "Sessions", "Get the current load session", "Returns idle, candidate, or active state.")
            .Produces<LoadSessionStatus>();
        Describe(group.MapGet("/session/last", (LoadSessionDetector detector) => detector.GetLast()), documented,
            "GetLastSession", "Sessions", "Get the most recently completed session",
            "The completed-session summary is held in memory and is cleared by service restart.")
            .Produces<CompletedLoadSessionStatus>();

        Describe(group.MapGet("/alerts", (DateTimeOffset? from, AlertSeverity? severity, AlertStore alerts) =>
                alerts.Query(from, severity)), documented, "GetAlerts", "Alerts", "Get retained alerts",
            "Filters alerts using an exclusive timestamp lower bound and optional exact severity.")
            .Produces<AlertHistoryResponse>();
        Describe(group.MapGet("/alerts/status", (SensorSnapshotStore snapshots, AlertEvaluator evaluator) =>
                evaluator.GetStatus(snapshots.Current)), documented, "GetAlertStatus", "Alerts",
            "Get live alert metrics and thresholds",
            "Returns normalized progress, headroom, direction, and evaluator state for every exposed alert metric.")
            .Produces<AlertStatusResponse>();
        Describe(group.MapGet("/alert-rules", (CustomAlertRuleStore rules) =>
                new CustomAlertRulesResponse(rules.GetAll())), documented, "GetCustomAlertRules", "Alerts",
            "Get custom alert rules", "Returns all persisted user-defined sensor alert rules.")
            .Produces<CustomAlertRulesResponse>();
        Describe(group.MapPost("/alert-rules", CreateAlertRule), documented, "CreateCustomAlertRule", "Alerts",
            "Create a custom alert rule", "Creates and persists a threshold rule for one stable sensor ID.")
            .Accepts<CustomAlertRuleRequest>("application/json").Produces<CustomAlertRule>(StatusCodes.Status201Created)
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
        Describe(group.MapPut("/alert-rules/{id:guid}", UpdateAlertRule), documented, "UpdateCustomAlertRule", "Alerts",
            "Update a custom alert rule", "Replaces an existing custom rule while retaining its identity.")
            .Accepts<CustomAlertRuleRequest>("application/json").Produces<CustomAlertRule>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);
        Describe(group.MapDelete("/alert-rules/{id:guid}", (Guid id, CustomAlertRuleStore rules) =>
                rules.Remove(id) ? Results.NoContent() : Results.NotFound()), documented, "DeleteCustomAlertRule", "Alerts",
            "Delete a custom alert rule", "Stops evaluating and permanently removes the rule.")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        Describe(group.MapGet("/notifications/status", NotificationStatus), documented, "GetNotificationStatus",
            "Notifications", "Get push-notification status", "Does not expose relay capability secrets.")
            .Produces<Notifications.NotificationStatus>();
        Describe(group.MapPost("/notifications/test-overheating", TestOverheatingNotification), documented,
            "TestOverheatingNotification", "Notifications", "Send a simulated GPU overheating notification",
            "Queues a critical GPU temperature alert through the normal push-notification pipeline. " +
            "For safety, this endpoint accepts requests only from the local computer.")
            .Produces(StatusCodes.Status202Accepted).Produces(StatusCodes.Status403Forbidden);
        Describe(group.MapPost("/notifications/devices", RegisterDevice), documented, "RegisterNotificationDevice",
            "Notifications", "Register or refresh a mobile installation",
            "Idempotently replaces the relay send capability and metadata associated with the installation ID.")
            .Accepts<DeviceRegistrationRequest>("application/json").Produces<DeviceRegistrationResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
        Describe(group.MapDelete("/notifications/devices/{installationId}", (string installationId,
                DeviceRegistrationStore devices) => devices.Remove(installationId) ? Results.NoContent() : Results.NotFound()),
            documented, "UnregisterNotificationDevice", "Notifications", "Unregister a mobile installation",
            "Returns 404 when the installation ID is not registered.")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        Describe(group.MapGet("/history", History), documented, "GetHistory", "History", "Query retained history",
            "Supports cursor paging, time bounds, resolution aggregation, sensor filtering, and session filtering.")
            .Produces<CompactHistoryResponse>().Produces<ApiError>(StatusCodes.Status400BadRequest);
    }

    private static RouteHandlerBuilder Describe(RouteHandlerBuilder endpoint, bool documented, string name,
        string tag, string summary, string description)
    {
        if (documented) endpoint.WithName(name).WithTags(tag).WithSummary(summary).WithDescription(description);
        return endpoint;
    }

    private static IResult HistoryManifest(HttpContext context, HistoricalHistoryStore history)
    {
        var manifest = history.GetManifest();
        var tag = $"\"{manifest.StreamId:N}-{manifest.CatalogVersion}-{manifest.NewestSequence ?? 0}-{manifest.BucketCount}\"";
        context.Response.Headers.ETag = tag; context.Response.Headers.CacheControl = "no-cache";
        return context.Request.Headers.IfNoneMatch.Any(value => value == tag)
            ? Results.StatusCode(StatusCodes.Status304NotModified) : Results.Ok(manifest);
    }

    private static Notifications.NotificationStatus NotificationStatus(IOptions<NotificationOptions> options,
        DeviceRegistrationStore devices, IPushNotificationProvider provider) => new(options.Value.Enabled,
        provider.IsConfigured, devices.GetAll().Count, options.Value.MinimumSeverity);

    private static IResult TestOverheatingNotification(HttpContext context, INotificationDispatcher notifications)
    {
        var remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress is not null && !System.Net.IPAddress.IsLoopback(remoteAddress))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        notifications.Enqueue(new MonitorAlert(Guid.NewGuid(), DateTimeOffset.UtcNow, AlertSeverity.Critical,
            "/test/gpu/0/temperature/0", "GPU", "GPU Core Temperature", "Temperature", 96, 90, "°C",
            "GPU Core temperature reached 96°C."));
        return Results.Accepted();
    }

    private static IResult RegisterDevice(DeviceRegistrationRequest request, DeviceRegistrationStore devices)
    {
        if (!Guid.TryParseExact(request.InstallationId, "D", out _) ||
            string.IsNullOrWhiteSpace(request.SendSecret) || request.SendSecret.Length is < 32 or > 512 ||
            request.DeviceName?.Length > 128)
            return Results.BadRequest(new ApiError("A valid installationId and sendSecret are required."));
        var registration = devices.Upsert(request);
        return Results.Ok(new DeviceRegistrationResponse(registration.InstallationId, registration.Platform,
            registration.DeviceName, registration.UpdatedAt));
    }

    private static IResult CreateAlertRule(CustomAlertRuleRequest request, CustomAlertRuleStore rules,
        SensorSnapshotStore snapshots)
    {
        var error = CustomAlertRuleStore.Validate(request);
        var sensor = snapshots.Current.Sensors.FirstOrDefault(candidate =>
            candidate.Id.Equals(request.SensorId, StringComparison.OrdinalIgnoreCase));
        error ??= sensor is null ? "The selected sensor is not present in the current snapshot." :
            rules.ValidateForSensor(request, sensor);
        if (error is not null) return Results.BadRequest(new ApiError(error));
        var created = rules.Create(request);
        return Results.Created($"/api/v1/alert-rules/{created.Id}", created);
    }

    private static IResult UpdateAlertRule(Guid id, CustomAlertRuleRequest request, CustomAlertRuleStore rules,
        SensorSnapshotStore snapshots)
    {
        if (rules.Get(id) is null) return Results.NotFound();
        var error = CustomAlertRuleStore.Validate(request);
        var sensor = snapshots.Current.Sensors.FirstOrDefault(candidate =>
            candidate.Id.Equals(request.SensorId, StringComparison.OrdinalIgnoreCase));
        error ??= sensor is null ? "The selected sensor is not present in the current snapshot." :
            rules.ValidateForSensor(request, sensor, id);
        if (error is not null) return Results.BadRequest(new ApiError(error));
        var updated = rules.Update(id, request);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }

    private static IResult History(DateTimeOffset? from, DateTimeOffset? to, long? afterSequence,
        long? beforeSequence, int? limit, string? resolution, int[]? sensorId, Guid? sessionId,
        HistoricalHistoryStore history)
    {
        if (from is not null && to is not null && from >= to)
            return Results.BadRequest(new ApiError("'from' must be earlier than 'to'."));
        var parsed = resolution?.ToLowerInvariant() switch { null or "minute" => HistoryResolution.Minute,
            "hour" => HistoryResolution.Hour, "day" => HistoryResolution.Day, _ => (HistoryResolution?)null };
        if (parsed is null) return Results.BadRequest(new ApiError("'resolution' must be minute, hour, or day."));
        if (afterSequence < 0 || beforeSequence < 0 || limit <= 0)
            return Results.BadRequest(new ApiError("Cursors cannot be negative and limit must be positive."));
        return Results.Ok(history.QueryCompact(from, to, afterSequence, limit, parsed.Value, sensorId, sessionId, beforeSequence));
    }
}
