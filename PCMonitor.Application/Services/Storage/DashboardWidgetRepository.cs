using PCMonitor.Application.Data;
using PCMonitor.Application.Data.Entities;
using PCMonitor.Application.Models;

namespace PCMonitor.Application.Services.Storage;

public sealed class DashboardWidgetRepository(AppDatabase database, TimeProvider timeProvider)
{
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
