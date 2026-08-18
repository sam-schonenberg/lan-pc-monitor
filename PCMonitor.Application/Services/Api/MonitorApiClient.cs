using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;
using PCMonitor.Application.Models.Api;
using PCMonitor.Application.Services.Storage;
namespace PCMonitor.Application.Services.Api;

public enum MonitorApiFailure { NetworkUnavailable, PcUnavailable, ServiceUnavailable, ApiError, InvalidResponse }
public sealed class MonitorApiException(MonitorApiFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public MonitorApiFailure Failure { get; } = failure;
}

public sealed class MonitorApiClient(IAppSettingsService settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public async Task<ServiceStatusDto> GetStatusAsync(CancellationToken token = default) =>
        await GetAsync<ServiceStatusDto>("api/v1/status", null, token);
    public async Task<ServiceStatusDto> TestStatusAsync(string baseUrl, CancellationToken token = default) =>
        await GetAsync<ServiceStatusDto>("api/v1/status", baseUrl, token);
    public async Task<SensorSnapshotDto> GetSensorsAsync(CancellationToken token = default) =>
        await GetAsync<SensorSnapshotDto>("api/v1/sensors", null, token);
    public async Task<SessionStatusDto> GetSessionAsync(CancellationToken token = default) =>
        await GetAsync<SessionStatusDto>("api/v1/session", null, token);
    public async Task<HistoricalHistoryResponseDto> GetHistoryAsync(DateTimeOffset? from, CancellationToken token = default) =>
        await GetAsync<HistoricalHistoryResponseDto>("api/v1/history" + QueryFrom(from), null, token);
    public async Task<SensorCatalogResponseDto> GetSensorCatalogAsync(CancellationToken token = default) =>
        await GetAsync<SensorCatalogResponseDto>("api/v1/sensors/catalog", null, token);
    public async Task<HistoryManifestResponseDto> GetHistoryManifestAsync(CancellationToken token = default) =>
        await GetAsync<HistoryManifestResponseDto>("api/v1/history/manifest", null, token);
    public async Task<CompactHistoryResponseDto> GetCompactHistoryAsync(long? afterSequence, int limit = 500,
        CancellationToken token = default, long? beforeSequence = null) => await GetAsync<CompactHistoryResponseDto>(
            $"api/v1/history?limit={limit}" +
            (afterSequence is null ? string.Empty : $"&afterSequence={afterSequence}") +
            (beforeSequence is null ? string.Empty : $"&beforeSequence={beforeSequence}"), null, token);
    public async Task<AlertHistoryResponseDto> GetAlertsAsync(DateTimeOffset? from, CancellationToken token = default) =>
        await GetAsync<AlertHistoryResponseDto>("api/v1/alerts" + QueryFrom(from), null, token);
    public async Task<AlertStatusResponseDto> GetAlertStatusAsync(CancellationToken token = default) =>
        await GetAsync<AlertStatusResponseDto>("api/v1/alerts/status", null, token);
    public async Task<NotificationStatusDto> GetNotificationStatusAsync(CancellationToken token = default) =>
        await GetAsync<NotificationStatusDto>("api/v1/notifications/status", null, token);
    public async Task<DeviceRegistrationResponseDto> RegisterNotificationDeviceAsync(
        DeviceRegistrationRequestDto registration, CancellationToken token = default) =>
        await SendAsync<DeviceRegistrationResponseDto>(HttpMethod.Post, "api/v1/notifications/devices", registration, token);
    public async Task UnregisterNotificationDeviceAsync(string installationId, CancellationToken token = default) =>
        await SendAsync<object>(HttpMethod.Delete,
            $"api/v1/notifications/devices/{Uri.EscapeDataString(installationId)}", null, token, allowEmpty: true);

    public async Task<Uri> GetBaseUriAsync() => NormalizeBaseUri(await settings.GetApiBaseUrlAsync()
        ?? throw new MonitorApiException(MonitorApiFailure.ServiceUnavailable, "No PC endpoint is configured."));

    public static Uri NormalizeBaseUri(string input)
    {
        var value = input.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = "http://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp ||
            string.IsNullOrWhiteSpace(uri.Host) || !IsLocalHost(uri.Host))
            throw new MonitorApiException(MonitorApiFailure.ApiError, "Enter a private LAN address using HTTP.");
        return new UriBuilder(uri) { Path = string.Empty, Query = string.Empty, Fragment = string.Empty }.Uri;
    }

    private async Task<T> GetAsync<T>(string path, string? overrideBase, CancellationToken token)
    {
        try
        {
            var baseUri = overrideBase is null ? await GetBaseUriAsync() : NormalizeBaseUri(overrideBase);
            using var response = await _http.GetAsync(new Uri(baseUri, path), token);
            if (!response.IsSuccessStatusCode)
                throw new MonitorApiException(MonitorApiFailure.ApiError, $"Monitoring service returned HTTP {(int)response.StatusCode}.");
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, token)
                   ?? throw new MonitorApiException(MonitorApiFailure.InvalidResponse, "Monitoring service returned an empty response.");
        }
        catch (MonitorApiException) { throw; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new MonitorApiException(MonitorApiFailure.PcUnavailable, "The PC did not respond in time."); }
        catch (HttpRequestException exception)
        { throw new MonitorApiException(exception.HttpRequestError == HttpRequestError.NameResolutionError ? MonitorApiFailure.NetworkUnavailable : MonitorApiFailure.PcUnavailable, "The PC could not be reached.", exception); }
        catch (JsonException exception)
        { throw new MonitorApiException(MonitorApiFailure.InvalidResponse, "The monitoring service returned invalid data.", exception); }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken token,
        bool allowEmpty = false)
    {
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(await GetBaseUriAsync(), path));
            if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
            using var response = await _http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                throw new MonitorApiException(MonitorApiFailure.ApiError,
                    $"Monitoring service returned HTTP {(int)response.StatusCode}.");
            if (allowEmpty) return default!;
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, token)
                   ?? throw new MonitorApiException(MonitorApiFailure.InvalidResponse,
                       "Monitoring service returned an empty response.");
        }
        catch (MonitorApiException) { throw; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new MonitorApiException(MonitorApiFailure.PcUnavailable, "The PC did not respond in time."); }
        catch (HttpRequestException exception)
        { throw new MonitorApiException(MonitorApiFailure.PcUnavailable, "The PC could not be reached.", exception); }
        catch (JsonException exception)
        { throw new MonitorApiException(MonitorApiFailure.InvalidResponse, "The monitoring service returned invalid data.", exception); }
    }

    private static string QueryFrom(DateTimeOffset? from) => from is null ? string.Empty : $"?from={Uri.EscapeDataString(from.Value.ToUniversalTime().ToString("O"))}";
    private static bool IsLocalHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || !host.Contains('.')) return true;
        if (!IPAddress.TryParse(host, out var address)) return false;
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork &&
               (bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168);
    }
}
