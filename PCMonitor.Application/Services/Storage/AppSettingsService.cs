using PCMonitor.Application.Data;
using PCMonitor.Application.Data.Entities;
namespace PCMonitor.Application.Services.Storage;
public interface IAppSettingsService
{
    Task<string?> GetApiBaseUrlAsync();
    Task SetApiBaseUrlAsync(string url);
    Task ClearApiBaseUrlAsync();
}
public sealed class AppSettingsService(AppDatabase database) : IAppSettingsService
{
    private const string Key = "ApiBaseUrl";
    public async Task<string?> GetApiBaseUrlAsync() => (await (await database.GetConnectionAsync()).FindAsync<AppSettingEntity>(Key))?.Value;
    public async Task SetApiBaseUrlAsync(string url) => await (await database.GetConnectionAsync()).InsertOrReplaceAsync(new AppSettingEntity { Key = Key, Value = url });
    public async Task ClearApiBaseUrlAsync() => await (await database.GetConnectionAsync()).DeleteAsync<AppSettingEntity>(Key);
}
