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
}
public sealed class AppSettingsService(AppDatabase database) : IAppSettingsService
{
    private const string ApiBaseUrlKey = "ApiBaseUrl";
    private const string HistorySensorKey = "History.SelectedSensor";
    private const string HiddenSensorsKey = "Sensors.Hidden";
    private const string LastHistorySyncKey = "History.LastSuccessfulSync";
    private readonly SemaphoreSlim _hiddenSensorsLock = new(1, 1);
    public async Task<string?> GetApiBaseUrlAsync() => (await (await database.GetConnectionAsync()).FindAsync<AppSettingEntity>(ApiBaseUrlKey))?.Value;
    public async Task SetApiBaseUrlAsync(string url) => await SetAsync(ApiBaseUrlKey, url);
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
    private async Task SetAsync(string key, string value) => await (await database.GetConnectionAsync())
        .InsertOrReplaceAsync(new AppSettingEntity { Key = key, Value = value });
}
