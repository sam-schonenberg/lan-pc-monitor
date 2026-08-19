using PCMonitor.Application.Data;
using PCMonitor.Application.Data.Entities;
namespace PCMonitor.Application.Services.Storage;
public interface IAppSettingsService
{
    Task<string?> GetApiBaseUrlAsync();
    Task SetApiBaseUrlAsync(string url);
    Task ClearApiBaseUrlAsync();
    Task<string?> GetHistorySensorIdAsync();
    Task SetHistorySensorIdAsync(string sensorId);
    Task<IReadOnlySet<string>> GetHiddenSensorIdsAsync();
    Task SetSensorHiddenAsync(string sensorId, bool hidden);
    Task<DateTimeOffset?> GetLastHistorySyncAsync();
    Task SetLastHistorySyncAsync(DateTimeOffset timestamp);
    Task<bool> GetNotificationsEnabledAsync();
    Task SetNotificationsEnabledAsync(bool enabled);
    Task<string> GetNotificationInstallationIdAsync();
}
public sealed class AppSettingsService(AppDatabase database) : IAppSettingsService
{
    private const string ApiBaseUrlKey = "ApiBaseUrl";
    private const string HistorySensorKey = "History.SelectedSensor";
    private const string HiddenSensorsKey = "Sensors.Hidden";
    private const string LastHistorySyncKey = "History.LastSuccessfulSync";
    private const string NotificationsEnabledKey = "Notifications.Enabled";
    private const string NotificationInstallationIdKey = "Notifications.InstallationId";
    internal const string DashboardDefaultsPendingKey = "Dashboard.DefaultsPending";
    private readonly SemaphoreSlim _installationIdLock = new(1, 1);
    private readonly SemaphoreSlim _hiddenSensorsLock = new(1, 1);
    public async Task<string?> GetApiBaseUrlAsync() => (await (await database.GetConnectionAsync()).FindAsync<AppSettingEntity>(ApiBaseUrlKey))?.Value;
    public async Task SetApiBaseUrlAsync(string url)
    {
        var connection = await database.GetConnectionAsync();
        var isFirstPairing = await connection.FindAsync<AppSettingEntity>(ApiBaseUrlKey) is null;
        await SetAsync(ApiBaseUrlKey, url);
        if (isFirstPairing) await SetAsync(DashboardDefaultsPendingKey, bool.TrueString);
    }
    public async Task ClearApiBaseUrlAsync() => await (await database.GetConnectionAsync()).DeleteAsync<AppSettingEntity>(ApiBaseUrlKey);
    public async Task<string?> GetHistorySensorIdAsync() => (await (await database.GetConnectionAsync()).FindAsync<AppSettingEntity>(HistorySensorKey))?.Value;
    public async Task SetHistorySensorIdAsync(string sensorId) => await SetAsync(HistorySensorKey, sensorId);
    public async Task<IReadOnlySet<string>> GetHiddenSensorIdsAsync()
    {
        var value = (await (await database.GetConnectionAsync()).FindAsync<AppSettingEntity>(HiddenSensorsKey))?.Value;
        if (string.IsNullOrWhiteSpace(value)) return new HashSet<string>(StringComparer.Ordinal);
        try
        {
            return (System.Text.Json.JsonSerializer.Deserialize<string[]>(value) ?? [])
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (System.Text.Json.JsonException) { return new HashSet<string>(StringComparer.Ordinal); }
    }
    public async Task SetSensorHiddenAsync(string sensorId, bool hidden)
    {
        await _hiddenSensorsLock.WaitAsync();
        try
        {
            var ids = (await GetHiddenSensorIdsAsync()).ToHashSet(StringComparer.Ordinal);
            if (hidden) ids.Add(sensorId); else ids.Remove(sensorId);
            await SetAsync(HiddenSensorsKey, System.Text.Json.JsonSerializer.Serialize(ids.OrderBy(x => x)));
        }
        finally { _hiddenSensorsLock.Release(); }
    }
    public async Task<DateTimeOffset?> GetLastHistorySyncAsync()
    {
        var value = (await (await database.GetConnectionAsync()).FindAsync<AppSettingEntity>(LastHistorySyncKey))?.Value;
        return DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var timestamp)
            ? timestamp : null;
    }
    public async Task SetLastHistorySyncAsync(DateTimeOffset timestamp) =>
        await SetAsync(LastHistorySyncKey, timestamp.ToUniversalTime().ToString("O"));
    public async Task<bool> GetNotificationsEnabledAsync() => bool.TryParse(
        (await (await database.GetConnectionAsync()).FindAsync<AppSettingEntity>(NotificationsEnabledKey))?.Value,
        out var enabled) && enabled;
    public async Task SetNotificationsEnabledAsync(bool enabled) =>
        await SetAsync(NotificationsEnabledKey, enabled.ToString());
    public async Task<string> GetNotificationInstallationIdAsync()
    {
        await _installationIdLock.WaitAsync();
        try
        {
            var connection = await database.GetConnectionAsync();
            var existing = (await connection.FindAsync<AppSettingEntity>(NotificationInstallationIdKey))?.Value;
            if (!string.IsNullOrWhiteSpace(existing)) return existing;
            var created = Guid.NewGuid().ToString();
            await SetAsync(NotificationInstallationIdKey, created);
            return created;
        }
        finally { _installationIdLock.Release(); }
    }
    private async Task SetAsync(string key, string value) => await (await database.GetConnectionAsync())
        .InsertOrReplaceAsync(new AppSettingEntity { Key = key, Value = value });
}
