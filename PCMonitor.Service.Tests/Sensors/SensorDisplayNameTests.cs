using LibreHardwareMonitor.Hardware;
using PCMonitor.Service.Sensors;
using Xunit;

namespace PCMonitor.Service.Tests.Sensors;

public sealed class SensorDisplayNameTests
{
    [Theory]
    [InlineData("CPU Core #1 Thread #2", SensorType.Load, "CPU Core 1, Thread 2 Usage")]
    [InlineData("CPU Core #3", SensorType.Load, "CPU Core 3 Usage")]
    [InlineData("Core #1", SensorType.Temperature, "CPU Core 1 Temperature")]
    [InlineData("CPU Total", SensorType.Load, "Overall CPU Usage")]
    [InlineData("CPU Package", SensorType.Temperature, "CPU Package Temperature")]
    public void MakesCpuNamesEasyToUnderstand(string rawName, SensorType type, string expected)
    {
        Assert.Equal(expected, SensorDisplayName.Format(HardwareType.Cpu, rawName, type));
    }

    [Theory]
    [InlineData("GPU Core", SensorType.Temperature, "GPU Core Temperature")]
    [InlineData("GPU Hot Spot", SensorType.Temperature, "GPU Hotspot")]
    [InlineData("GPU Memory", SensorType.Load, "GPU Memory Usage")]
    public void MakesGpuNamesEasyToUnderstand(string rawName, SensorType type, string expected)
    {
        Assert.Equal(expected, SensorDisplayName.Format(HardwareType.GpuNvidia, rawName, type));
    }

    [Fact]
    public void PreservesUnknownVendorNameAndAddsMeaningfulMeasurement()
    {
        Assert.Equal("VR VOUT Voltage",
            SensorDisplayName.Format(HardwareType.Motherboard, "VR VOUT", SensorType.Voltage));
    }

    [Fact]
    public void DoesNotRepeatMeasurementAlreadyPresentInName()
    {
        Assert.Equal("Core Temperature",
            SensorDisplayName.Format(HardwareType.Motherboard, "Core Temperature", SensorType.Temperature));
    }
}
