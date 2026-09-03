using PCMonitor.Application.Data.Entities;
using PCMonitor.Application.Services.Export;
using Xunit;

namespace PCMonitor.Application.Tests;

public sealed class HistoryCsvFormatterTests
{
    [Fact]
    public void FormatsInvariantValuesAndEscapesDescriptiveFields()
    {
        var start = new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);
        var csv = HistoryCsvFormatter.Format([new HistoricalSensorEntity
        {
            BucketStartUtcTicks = start.UtcTicks, BucketEndUtcTicks = start.AddMinutes(1).UtcTicks,
            Hardware = "CPU, Package", SensorName = "Core \"Average\"", SensorType = "Temperature",
            Unit = "°C", Min = 40.25f, Average = 41.5, Max = 42.75f, SampleCount = 12,
            DominantProcessName = "game.exe"
        }]);

        Assert.StartsWith("timestamp_utc,bucket_end_utc,hardware", csv);
        Assert.Contains("2026-08-25T10:00:00.0000000+00:00", csv);
        Assert.Contains("\"CPU, Package\",\"Core \"\"Average\"\"\"", csv);
        Assert.Contains(",40.25,41.5,42.75,12,game.exe", csv);
    }
}
