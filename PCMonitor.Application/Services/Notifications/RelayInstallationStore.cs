using System.Text.Json;

namespace PCMonitor.Application.Services.Notifications;

public sealed class RelayInstallationStore
{
    private const string StorageKey = "notifications.relay.installation.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RelayInstallation?> GetAsync()
    {
        try
        {
            var value = await SecureStorage.Default.GetAsync(StorageKey);
            return string.IsNullOrWhiteSpace(value)
                ? null
                : JsonSerializer.Deserialize<RelayInstallation>(value, JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            SecureStorage.Default.Remove(StorageKey);
            return null;
        }
    }

    public Task SaveAsync(RelayInstallation installation) => SecureStorage.Default.SetAsync(
        StorageKey, JsonSerializer.Serialize(installation, JsonOptions));

    public void Clear() => SecureStorage.Default.Remove(StorageKey);
}
