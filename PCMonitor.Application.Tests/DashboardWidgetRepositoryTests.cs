using PCMonitor.Application.Data;
using PCMonitor.Application.Models;
using PCMonitor.Application.Services.Storage;
using Xunit;

namespace PCMonitor.Application.Tests;

public sealed class DashboardWidgetRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pcmonitor-widgets-{Guid.NewGuid():N}.db3");
    private readonly DateTimeOffset _now = new(2026, 8, 15, 18, 0, 0, TimeSpan.Zero);
    private AppDatabase? _database;

    [Fact]
    public async Task StoresTypedConfigurationsAndOrdersWidgets()
    {
        var repository = Create();
        var graph = DashboardWidgetCatalog.Create(DashboardWidgetType.Graph, 1) with
        {
            Title = "GPU history",
            Configuration = new GraphWidgetConfiguration("gpu-temp", TimeSpan.FromHours(6), true, false, true)
        };
        var value = DashboardWidgetCatalog.Create(DashboardWidgetType.CurrentValue, 0) with
        {
            Title = "GPU temperature",
            Configuration = new CurrentValueWidgetConfiguration("gpu-temp", 1, true)
        };

        await repository.SaveAsync(graph);
        await repository.SaveAsync(value);
        var saved = await repository.GetAllAsync();

        Assert.Equal([value.Id, graph.Id], saved.Select(x => x.Id));
        Assert.Equal("gpu-temp", Assert.IsType<CurrentValueWidgetConfiguration>(saved[0].Configuration).SensorId);
        Assert.Equal(TimeSpan.FromHours(6), Assert.IsType<GraphWidgetConfiguration>(saved[1].Configuration).EffectiveRange);
    }

    [Fact]
    public async Task ReorderAndDeletePersistWithoutUiState()
    {
        var repository = Create();
        var first = DashboardWidgetCatalog.Create(DashboardWidgetType.Alerts, 0);
        var second = DashboardWidgetCatalog.Create(DashboardWidgetType.Graph, 1);
        await repository.SaveAsync(first);
        await repository.SaveAsync(second);

        await repository.ReorderAsync([second.Id, first.Id]);
        Assert.Equal([second.Id, first.Id], (await repository.GetAllAsync()).Select(x => x.Id));

        await repository.DeleteAsync(second.Id);
        Assert.Equal(first.Id, Assert.Single(await repository.GetAllAsync()).Id);
    }

    [Fact]
    public void RejectsConfigurationThatDoesNotMatchWidgetType()
    {
        var widget = DashboardWidgetCatalog.Create(DashboardWidgetType.Graph, 0) with
        {
            Configuration = new AlertWidgetConfiguration()
        };

        Assert.Throws<ArgumentException>(() => DashboardWidgetConfigurationCodec.Validate(widget));
    }

    private DashboardWidgetRepository Create()
    {
        _database = new AppDatabase(_path);
        return new DashboardWidgetRepository(_database, new FixedTimeProvider(_now));
    }

    public void Dispose()
    {
        _database?.Dispose();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
