using PCMonitor.Application.Data.Entities;
using SQLite;
namespace PCMonitor.Application.Data;
public sealed class AppDatabase
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SQLiteAsyncConnection? _connection;
    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null) return _connection;
        await _lock.WaitAsync();
        try
        {
            if (_connection is null)
            {
                _connection = new SQLiteAsyncConnection(DatabaseConstants.Path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
                await _connection.CreateTableAsync<AppSettingEntity>();
                await _connection.CreateTableAsync<AlertEntity>();
                await _connection.CreateTableAsync<HistoricalSensorEntity>();
                await _connection.ExecuteAsync("PRAGMA journal_mode=WAL;");
            }
            return _connection;
        }
        finally { _lock.Release(); }
    }
}
