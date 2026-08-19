using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PCMonitor.Service.Alerts;

namespace PCMonitor.Service.Notifications;

public sealed class NotificationRelayPushProvider : IPushNotificationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly NotificationOptions _options;
    private readonly Uri? _relayBaseUri;

    public NotificationRelayPushProvider(HttpClient httpClient, IOptions<NotificationOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        if (Uri.TryCreate(_options.RelayBaseUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo))
            _relayBaseUri = uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + '/');
    }

    public bool IsConfigured => _options.Enabled && _relayBaseUri is not null;

    public async Task<PushDeliveryResult> SendAsync(DeviceRegistration device, MonitorAlert alert,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("The notification relay is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_relayBaseUri!, "v1/notifications"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", device.SendSecret);
        request.Content = JsonContent.Create(new RelayNotification(
            device.InstallationId,
            EventType(alert),
            DisplaySensor(alert),
            alert.Value,
            alert.Unit ?? string.Empty), options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return PushDeliveryResult.Delivered;
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            return PushDeliveryResult.InvalidDestination;
        var detail = await ErrorDetailAsync(response, cancellationToken);
        throw new HttpRequestException(
            $"Notification relay rejected the alert ({(int)response.StatusCode}){detail}.", null,
            response.StatusCode);
    }

    private static string EventType(MonitorAlert alert)
    {
        var suffix = alert.Severity == AlertSeverity.Warning ? "warning" : "critical";
        if (alert.SensorType.Equals("Temperature", StringComparison.OrdinalIgnoreCase)) return $"temperature-{suffix}";
        if (alert.SensorType.Equals("Fan", StringComparison.OrdinalIgnoreCase)) return $"fan-{suffix}";
        if (alert.SensorName.Contains("Memory", StringComparison.OrdinalIgnoreCase) ||
            alert.SensorName.Contains("VRAM", StringComparison.OrdinalIgnoreCase)) return $"memory-{suffix}";
        return $"utilization-{suffix}";
    }

    private static string DisplaySensor(MonitorAlert alert)
    {
        var name = string.IsNullOrWhiteSpace(alert.SensorName) ? alert.Hardware : alert.SensorName;
        return name.Length <= 80 ? name : name[..80];
    }

    private static async Task<string> ErrorDetailAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<RelayError>(JsonOptions, cancellationToken);
            return string.IsNullOrWhiteSpace(error?.Detail) ? string.Empty : $": {error.Detail}";
        }
        catch (JsonException) { return string.Empty; }
    }

    private sealed record RelayNotification(
        [property: JsonPropertyName("installation_id")] string InstallationId,
        [property: JsonPropertyName("event_type")] string EventType,
        [property: JsonPropertyName("sensor")] string Sensor,
        [property: JsonPropertyName("value")] double Value,
        [property: JsonPropertyName("unit")] string Unit);
    private sealed record RelayError([property: JsonPropertyName("detail")] string? Detail);
}
