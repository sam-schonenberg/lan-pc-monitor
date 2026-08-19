using PCMonitor.Application.Data;
using PCMonitor.Application.Data.Entities;
using PCMonitor.Application.Models;

namespace PCMonitor.Application.Services.Storage;

public sealed class DashboardWidgetRepository(AppDatabase database, TimeProvider timeProvider)
{
    public async Task InitializeDefaultsIfPendingAsync(IReadOnlyCollection<HistoricalSensorEntity> sensors)
    {
        if (sensors.Count == 0) return;
        var connection = await database.GetConnectionAsync();
        if (await connection.FindAsync<AppSettingEntity>(AppSettingsService.DashboardDefaultsPendingKey) is null) return;

        await connection.RunInTransactionAsync(transaction =>
        {
            var existing = transaction.Table<DashboardWidgetEntity>().Take(1).Count();
            if (existing == 0)
            {
                foreach (var widget in DefaultDashboardWidgets.Create(sensors))
                    transaction.Insert(ToEntity(widget));
            }
            transaction.Delete<AppSettingEntity>(AppSettingsService.DashboardDefaultsPendingKey);
        });
    }

    public async Task<IReadOnlyList<DashboardWidgetDefinition>> GetAllAsync()
    {
        var rows = await (await database.GetConnectionAsync()).Table<DashboardWidgetEntity>()
            .OrderBy(x => x.Position).ToListAsync();
        return rows.Select(ToDefinition).ToArray();
    }

    public async Task<DashboardWidgetDefinition?> GetAsync(Guid id)
    {
        var row = await (await database.GetConnectionAsync()).FindAsync<DashboardWidgetEntity>(id.ToString("N"));
        return row is null ? null : ToDefinition(row);
    }

    public async Task SaveAsync(DashboardWidgetDefinition widget)
    {
        DashboardWidgetConfigurationCodec.Validate(widget);
        await (await database.GetConnectionAsync()).InsertOrReplaceAsync(ToEntity(widget));
    }

    public async Task DeleteAsync(Guid id) =>
        await (await database.GetConnectionAsync()).DeleteAsync<DashboardWidgetEntity>(id.ToString("N"));

    public async Task ReorderAsync(IReadOnlyList<Guid> orderedIds)
    {
        if (orderedIds.Count != orderedIds.Distinct().Count())
            throw new ArgumentException("A widget can appear only once in an ordering.", nameof(orderedIds));
        var connection = await database.GetConnectionAsync();
        await connection.RunInTransactionAsync(transaction =>
        {
            for (var position = 0; position < orderedIds.Count; position++)
            {
                var row = transaction.Find<DashboardWidgetEntity>(orderedIds[position].ToString("N"))
                    ?? throw new InvalidOperationException("Cannot reorder a widget that does not exist.");
                row.Position = position;
                row.UpdatedUtcTicks = timeProvider.GetUtcNow().UtcTicks;
                transaction.Update(row);
            }
        });
    }

    private DashboardWidgetEntity ToEntity(DashboardWidgetDefinition widget) => new()
    {
        Id = widget.Id.ToString("N"), Type = (int)widget.Type, Title = widget.Title.Trim(),
        Position = widget.Position, Width = (int)widget.Width, IsEnabled = widget.IsEnabled,
        ConfigurationJson = DashboardWidgetConfigurationCodec.Serialize(widget.Type, widget.Configuration),
        UpdatedUtcTicks = timeProvider.GetUtcNow().UtcTicks
    };

    private static DashboardWidgetDefinition ToDefinition(DashboardWidgetEntity row)
    {
        var type = (DashboardWidgetType)row.Type;
        return new(Guid.ParseExact(row.Id, "N"), type, row.Title, row.Position,
            (DashboardWidgetWidth)row.Width, row.IsEnabled,
            DashboardWidgetConfigurationCodec.Deserialize(type, row.ConfigurationJson));
    }
}

public static class DefaultDashboardWidgets
{
    public static IReadOnlyList<DashboardWidgetDefinition> Create(
        IReadOnlyCollection<HistoricalSensorEntity> sensors)
    {
        var widgets = new List<DashboardWidgetDefinition>();
        var cpu = Best(sensors, CpuTemperatureScore);
        var gpu = Best(sensors, GpuTemperatureScore);
        var memory = Best(sensors, MemoryUsageScore);

        if (cpu is not null) widgets.Add(Current("CPU temperature", cpu.SensorId, widgets.Count));
        if (gpu is not null) widgets.Add(Current("GPU temperature", gpu.SensorId, widgets.Count));
        if (memory is not null) widgets.Add(Graph("RAM usage · last hour", memory.SensorId, widgets.Count));
        widgets.Add(DashboardWidgetCatalog.Create(DashboardWidgetType.Alerts, widgets.Count) with
        {
            Title = "Recent alerts",
            Configuration = new AlertWidgetConfiguration(null, null, 5)
        });
        return widgets;
    }

    private static DashboardWidgetDefinition Current(string title, string sensorId, int position) =>
        DashboardWidgetCatalog.Create(DashboardWidgetType.CurrentValue, position) with
        {
            Title = title,
            Configuration = new CurrentValueWidgetConfiguration(sensorId, 1)
        };

    private static DashboardWidgetDefinition Graph(string title, string sensorId, int position) =>
        DashboardWidgetCatalog.Create(DashboardWidgetType.Graph, position) with
        {
            Title = title,
            Configuration = new GraphWidgetConfiguration(sensorId, TimeSpan.FromHours(1), true, false, true)
        };

    private static HistoricalSensorEntity? Best(IEnumerable<HistoricalSensorEntity> sensors,
        Func<HistoricalSensorEntity, int> score) => sensors
        .Select(sensor => (Sensor: sensor, Score: score(sensor)))
        .Where(candidate => candidate.Score > 0)
        .OrderByDescending(candidate => candidate.Score)
        .ThenBy(candidate => candidate.Sensor.SensorName, StringComparer.OrdinalIgnoreCase)
        .Select(candidate => candidate.Sensor)
        .FirstOrDefault();

    private static int CpuTemperatureScore(HistoricalSensorEntity sensor)
    {
        if (!IsTemperature(sensor) || !ContainsAny(sensor.Hardware, "cpu", "intel", "amd", "ryzen")) return 0;
        return Contains(sensor.SensorName, "package") ? 100 : Contains(sensor.SensorName, "average") ? 90
            : Contains(sensor.SensorName, "core max") ? 80 : 50;
    }

    private static int GpuTemperatureScore(HistoricalSensorEntity sensor)
    {
        if (!IsTemperature(sensor) || !ContainsAny(sensor.Hardware, "gpu", "nvidia", "radeon", "graphics")) return 0;
        return Contains(sensor.SensorName, "core") ? 100 : Contains(sensor.SensorName, "hotspot") ? 80 : 50;
    }

    private static int MemoryUsageScore(HistoricalSensorEntity sensor)
    {
        if (!sensor.SensorType.Equals("Load", StringComparison.OrdinalIgnoreCase) || sensor.Unit != "%") return 0;
        if (ContainsAny(sensor.SensorName, "memory usage", "ram usage", "memory load"))
            return Contains(sensor.Hardware, "total memory") ? 120 : Contains(sensor.Hardware, "virtual") ? 80 : 100;
        return ContainsAny(sensor.Hardware, "memory", "ram") && Contains(sensor.SensorName, "used") ? 70 : 0;
    }

    private static bool IsTemperature(HistoricalSensorEntity sensor) =>
        sensor.SensorType.Equals("Temperature", StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string value, string term) =>
        value.Contains(term, StringComparison.OrdinalIgnoreCase);
    private static bool ContainsAny(string value, params string[] terms) => terms.Any(term => Contains(value, term));
}
