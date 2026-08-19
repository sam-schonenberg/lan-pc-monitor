using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCMonitor.Application.Services.Notifications;

public sealed class NotificationRelayClient
{
    // Centralized so it can be replaced with a first-party domain without changing the registration protocol.
    private static readonly Uri RelayBaseUri = new("https://138-201-94-167.sslip.io/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { BaseAddress = RelayBaseUri, Timeout = TimeSpan.FromSeconds(12) };

    public async Task<RelayInstallation> CreateInstallationAsync(string fcmToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("v1/installations",
            new InstallationCreateRequest(fcmToken), JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RelayInstallation>(JsonOptions, cancellationToken)
               ?? throw new NotificationRelayException("The notification relay returned an empty response.");
    }

    public async Task<bool> UpdateTokenAsync(RelayInstallation installation, string fcmToken,
        CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Put,
            $"v1/installations/{Uri.EscapeDataString(installation.InstallationId)}/token",
            installation.DeleteSecret);
        request.Content = JsonContent.Create(new TokenUpdateRequest(fcmToken), options: JsonOptions);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return false;
        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    public async Task DeleteInstallationAsync(RelayInstallation installation,
        CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Delete,
            $"v1/installations/{Uri.EscapeDataString(installation.InstallationId)}",
            installation.DeleteSecret);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return;
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string secret)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = string.Empty;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<RelayError>(JsonOptions, cancellationToken);
            detail = error?.Detail ?? string.Empty;
        }
        catch (JsonException) { }
        throw new NotificationRelayException(string.IsNullOrWhiteSpace(detail)
            ? $"Notification relay returned HTTP {(int)response.StatusCode}."
            : detail);
    }

    private sealed record InstallationCreateRequest(
        [property: JsonPropertyName("fcm_token")] string FcmToken,
        [property: JsonPropertyName("platform")] string Platform = "android",
        [property: JsonPropertyName("minimum_severity")] string MinimumSeverity = "critical");
    private sealed record TokenUpdateRequest([property: JsonPropertyName("fcm_token")] string FcmToken);
    private sealed record RelayError([property: JsonPropertyName("detail")] string? Detail);
}

public sealed record RelayInstallation(
    [property: JsonPropertyName("installation_id")] string InstallationId,
    [property: JsonPropertyName("send_secret")] string SendSecret,
    [property: JsonPropertyName("delete_secret")] string DeleteSecret,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

public sealed class NotificationRelayException(string message, Exception? inner = null) : Exception(message, inner);
