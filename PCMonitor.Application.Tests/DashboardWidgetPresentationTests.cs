using System.Globalization;
using PCMonitor.Application.Data;
using PCMonitor.Application.Data.Entities;
using PCMonitor.Application.Models;
using PCMonitor.Application.Services.Storage;
using Xunit;

namespace PCMonitor.Application.Tests;

public sealed class DashboardWidgetPresentationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pcmonitor-dashboard-{Guid.NewGuid():N}.db3");
    private AppDatabase? _database;

    [Fact]
    public void PacksHalfHalfFullHalfHalfIntoThreeRows()
    {
        var widgets = new[]
        {
            Widget(DashboardWidgetWidth.Half), Widget(DashboardWidgetWidth.Half), Widget(DashboardWidgetWidth.Full),
            Widget(DashboardWidgetWidth.Half), Widget(DashboardWidgetWidth.Half)
        };
        var rows = DashboardWidgetLayout.Pack(widgets, x => x.Width);
        Assert.Equal(3, rows.Count);
        Assert.NotNull(rows[0].Second);
        Assert.True(rows[1].IsFullWidth);
        Assert.NotNull(rows[2].Second);
    }

    [Fact]
    public void DisabledWidgetIsHiddenNormallyButRemainsAvailableInEditMode()
    {
        var widget = Widget(DashboardWidgetWidth.Half) with { IsEnabled = false };
        Assert.False(DashboardWidgetPresentation.ShouldRender(widget, false));
        Assert.True(DashboardWidgetPresentation.ShouldRender(widget, true));
    }

    [Fact]
    public void CustomTitleOverridesGeneratedSensorTitle()
    {
        var custom = Widget(DashboardWidgetWidth.Half) with { Title = "My GPU" };
        Assert.Equal("My GPU", DashboardWidgetPresentation.ResolveTitle(custom, "GPU Core"));
        Assert.Equal("GPU Core", DashboardWidgetPresentation.ResolveTitle(Widget(DashboardWidgetWidth.Half), "GPU Core"));
    }

    [Fact]
    public void CurrentValuePrecisionIsRespected()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal("43 °C", DashboardWidgetPresentation.FormatValue(43.246, "°C", 0));
            Assert.Equal("43.2 °C", DashboardWidgetPresentation.FormatValue(43.246, "°C", 1));
            Assert.Equal("43.25 °C", DashboardWidgetPresentation.FormatValue(43.246, "°C", 2));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void GraphConfigurationPreservesSharedChartLineVisibility()
    {
        var config = new GraphWidgetConfiguration("gpu", TimeSpan.FromHours(6), true, false, true);
        Assert.True(config.ShowAverage); Assert.False(config.ShowMinimum); Assert.True(config.ShowMaximum);
        Assert.Equal(TimeSpan.FromHours(6), config.EffectiveRange);
    }

    [Fact]
    public async Task AlertFiltersApplySensorSeverityAndItemLimit()
    {
        _database = new AppDatabase(_path);
        var connection = await _database.GetConnectionAsync();
        await connection.InsertAllAsync(new[]
        {
            Alert("1", "gpu", "Warning", 3), Alert("2", "gpu", "Critical", 2),
            Alert("3", "cpu", "Critical", 1), Alert("4", "gpu", "Information", 0)
        });
        var result = await new AlertRepository(_database).GetRecentAsync("gpu", "Warning", 1);
        Assert.Single(result);
        Assert.Equal("Critical", result[0].Severity);
        Assert.Equal("gpu", result[0].SensorId);
    }

    [Fact]
    public void OfflineFallbackUsesLatestLocalValueAndMarksItNonLive()
    {
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
        var resolved = DashboardWidgetPresentation.ResolveCurrent(null, null, 42.9, timestamp);
        Assert.Equal(42.9, resolved.Value); Assert.False(resolved.IsLive); Assert.Equal(timestamp, resolved.Timestamp);
    }

    private static DashboardWidgetDefinition Widget(DashboardWidgetWidth width) =>
        DashboardWidgetCatalog.Create(DashboardWidgetType.CurrentValue, 0) with { Width = width };
    private static AlertEntity Alert(string id, string sensor, string severity, int minutes) => new()
    {
        Id = id, SensorId = sensor, SensorName = sensor, Severity = severity,
        TimestampUtcTicks = DateTimeOffset.UtcNow.AddMinutes(-minutes).UtcTicks, Value = 80, Unit = "°C"
    };

    public void Dispose()
    {
        _database?.Dispose();
        if (File.Exists(_path)) File.Delete(_path);
    }
}
