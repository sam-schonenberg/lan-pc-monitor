using PCMonitor.Application.Data.Entities;
using SQLite;
namespace PCMonitor.Application.Data;
public sealed class AppDatabase : IDisposable
{
    private readonly string _databasePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SQLiteAsyncConnection? _connection;
    public AppDatabase(string? databasePath = null) => _databasePath = databasePath ?? DatabaseConstants.Path;
    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null) return _connection;
        await _lock.WaitAsync();
        try
        {
            if (_connection is null)
            {
                _connection = new SQLiteAsyncConnection(_databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
                await _connection.CreateTableAsync<AppSettingEntity>();
                await _connection.CreateTableAsync<AlertEntity>();
                await _connection.CreateTableAsync<HistoricalSensorEntity>();
                await _connection.CreateTableAsync<SensorCatalogEntity>();
                await _connection.CreateTableAsync<HistoryCoverageEntity>();
                await _connection.CreateTableAsync<DashboardWidgetEntity>();
                await _connection.ExecuteAsync(
                    "CREATE INDEX IF NOT EXISTS IX_HistoricalSensors_Sensor_Start ON HistoricalSensors (SensorId, BucketStartUtcTicks DESC)");
            }
            return _connection;
        }
        finally { _lock.Release(); }
    }

    public void Dispose()
    {
        _connection?.CloseAsync().GetAwaiter().GetResult();
        _lock.Dispose();
    }
}
