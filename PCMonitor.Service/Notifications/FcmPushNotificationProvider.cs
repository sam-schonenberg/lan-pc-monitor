using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PCMonitor.Service.Alerts;

namespace PCMonitor.Service.Notifications;

public sealed class FcmPushNotificationProvider : IPushNotificationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly NotificationOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public FcmPushNotificationProvider(HttpClient httpClient, IOptions<NotificationOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.FirebaseProjectId) &&
                                !string.IsNullOrWhiteSpace(_options.FirebaseServiceAccountFile);

    public async Task<PushDeliveryResult> SendAsync(DeviceRegistration device, MonitorAlert alert,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Firebase notifications are not configured.");
        var accessToken = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(_options.FirebaseProjectId)}/messages:send");
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                token = device.Token,
                notification = new { title = $"{alert.Severity}: {alert.SensorName}", body = alert.Message },
                data = new Dictionary<string, string>
                {
                    ["type"] = "sensorAlert", ["alertId"] = alert.Id.ToString(),
                    ["severity"] = alert.Severity.ToString().ToLowerInvariant(), ["sensorId"] = alert.SensorId,
                    ["value"] = alert.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["threshold"] = alert.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)
                },
                android = new { priority = "high" },
                apns = new { payload = new { aps = new { sound = "default" } } }
            }
        }, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return PushDeliveryResult.Delivered;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest &&
            body.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase)) return PushDeliveryResult.InvalidToken;
        throw new HttpRequestException($"FCM rejected the notification ({(int)response.StatusCode}): {body}",
            null, response.StatusCode);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt) return _accessToken;
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt) return _accessToken;
            var accountPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_options.FirebaseServiceAccountFile));
            var account = JsonSerializer.Deserialize<ServiceAccount>(await File.ReadAllTextAsync(accountPath, cancellationToken),
                JsonOptions) ?? throw new InvalidOperationException("Invalid Firebase service-account JSON.");
            var now = DateTimeOffset.UtcNow;
            var assertion = CreateAssertion(account, now);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = assertion
            });
            using var response = await _httpClient.PostAsync(account.TokenUri, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            var token = JsonSerializer.Deserialize<TokenResponse>(
                await response.Content.ReadAsStringAsync(cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Firebase token response was empty.");
            _accessToken = token.AccessToken;
            _accessTokenExpiresAt = now.AddSeconds(Math.Max(60, token.ExpiresIn - 300));
            return _accessToken;
        }
        finally { _tokenLock.Release(); }
    }

    private static string CreateAssertion(ServiceAccount account, DateTimeOffset now)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = account.ClientEmail, scope = "https://www.googleapis.com/auth/firebase.messaging",
            aud = account.TokenUri, iat = now.ToUnixTimeSeconds(), exp = now.AddMinutes(60).ToUnixTimeSeconds()
        }));
        var unsigned = $"{header}.{payload}";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(account.PrivateKey);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return $"{unsigned}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record ServiceAccount(
        [property: JsonPropertyName("client_email")] string ClientEmail,
        [property: JsonPropertyName("private_key")] string PrivateKey,
        [property: JsonPropertyName("token_uri")] string TokenUri);
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
