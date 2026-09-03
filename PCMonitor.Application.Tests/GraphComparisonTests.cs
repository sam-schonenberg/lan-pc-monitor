using PCMonitor.Application.Models;
using Xunit;

namespace PCMonitor.Application.Tests;

public sealed class GraphComparisonTests
{
    [Theory]
    [InlineData("Temperature", "°C", "temperature", "C")]
    [InlineData("Load", "%", "load", "percentage")]
    [InlineData("Power", "W", "power", "watts")]
    public void CompatibleMeasurementsShareAKey(string firstType, string firstUnit, string secondType, string secondUnit) =>
        Assert.True(GraphCompatibility.AreCompatible(firstType, firstUnit, secondType, secondUnit));

    [Theory]
    [InlineData("Temperature", "°C", "Load", "%")]
    [InlineData("Temperature", "°C", "Temperature", "°F")]
    [InlineData("Clock", "MHz", "Clock", "GHz")]
    public void IncompatibleMeasurementsUseDifferentKeys(string firstType, string firstUnit, string secondType, string secondUnit) =>
        Assert.False(GraphCompatibility.AreCompatible(firstType, firstUnit, secondType, secondUnit));

    [Fact]
    public void GraphConfigurationRoundTripsComparisonSensors()
    {
        var configuration = new GraphWidgetConfiguration("gpu", TimeSpan.FromHours(6), true, false, true,
            ["cpu", "board"]);
        var json = DashboardWidgetConfigurationCodec.Serialize(DashboardWidgetType.Graph, configuration);
        var restored = Assert.IsType<GraphWidgetConfiguration>(
            DashboardWidgetConfigurationCodec.Deserialize(DashboardWidgetType.Graph, json));

        Assert.Equal(["cpu", "board"], restored.EffectiveComparisonSensorIds);
    }
}
