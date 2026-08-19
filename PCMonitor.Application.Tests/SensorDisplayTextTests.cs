using PCMonitor.Application.Models;
using Xunit;

namespace PCMonitor.Application.Tests;

public sealed class SensorDisplayTextTests
{
    [Theory]
    [InlineData("GPU Core", "Temperature", "GPU Core · Temperature")]
    [InlineData("GPU Core", "Load", "GPU Core · Usage")]
    [InlineData("GPU Core", "Clock", "GPU Core · Clock speed")]
    [InlineData("GPU Fan", "Fan", "GPU Fan · Speed")]
    [InlineData("GPU Fan", "Control", "GPU Fan · Control")]
    [InlineData("GPU Memory", "SmallData", "GPU Memory · Amount")]
    [InlineData("GPU Memory Junction", "Temperature", "GPU Memory Junction · Temperature")]
    public void AddsAConciseMeasurementToAmbiguousNames(string name, string type, string expected)
    {
        Assert.Equal(expected, SensorDisplayText.PickerLabel(name, type));
    }

    [Theory]
    [InlineData("GPU Core Voltage", "Voltage")]
    [InlineData("GPU Memory Free", "SmallData")]
    [InlineData("CPU Package Temperature", "Temperature")]
    public void DoesNotRepeatMeasurementsAlreadyInFriendlyName(string name, string type)
    {
        Assert.Equal(name, SensorDisplayText.PickerLabel(name, type));
    }

    [Theory]
    [InlineData("Intel Core i7", "CPU Package Temperature", "Temperature", "°C", 1000)]
    [InlineData("NVIDIA GeForce", "GPU Core Temperature", "Temperature", "°C", 990)]
    [InlineData("Total Memory", "System Memory Usage", "Load", "%", 880)]
    [InlineData("Virtual Memory", "System Memory Usage", "Load", "%", 0)]
    [InlineData("Intel Core i7", "CPU Core 1 Temperature", "Temperature", "°C", 0)]
    public void RanksOnlyCommonlyRequestedSensors(string hardware, string name, string type, string unit,
        int expected)
    {
        Assert.Equal(expected, SensorDisplayText.CommonSensorPriority(hardware, name, type, unit));
    }

    [Fact]
    public void MarksCommonPickerEntriesWithAStar()
    {
        Assert.Equal("★ CPU Package Temperature",
            SensorDisplayText.PickerLabel("Intel Core i7", "CPU Package Temperature", "Temperature", "°C"));
    }
}
