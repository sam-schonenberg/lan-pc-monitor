using System.Globalization;
using System.Text;
using PCMonitor.Application.Data.Entities;

namespace PCMonitor.Application.Services.Export;

public static class HistoryCsvFormatter
{
    public static string Format(IEnumerable<HistoricalSensorEntity> rows)
    {
        var output = new StringBuilder("timestamp_utc,bucket_end_utc,hardware,sensor_name,sensor_type,unit,minimum,average,maximum,sample_count,dominant_process\n");
        foreach (var row in rows)
        {
            Append(output, row.BucketStartTime.ToString("O", CultureInfo.InvariantCulture));
            Append(output, row.BucketEndTime.ToString("O", CultureInfo.InvariantCulture));
            Append(output, row.Hardware);
            Append(output, row.SensorName);
            Append(output, row.SensorType);
            Append(output, row.Unit);
            Append(output, row.Min.ToString("R", CultureInfo.InvariantCulture));
            Append(output, row.Average.ToString("R", CultureInfo.InvariantCulture));
            Append(output, row.Max.ToString("R", CultureInfo.InvariantCulture));
            Append(output, row.SampleCount.ToString(CultureInfo.InvariantCulture));
            Append(output, row.DominantProcessName, last: true);
        }
        return output.ToString();
    }

    private static void Append(StringBuilder output, string? value, bool last = false)
    {
        value ??= string.Empty;
        if (value.IndexOfAny([',', '"', '\r', '\n']) >= 0)
            output.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
        else
            output.Append(value);
        output.Append(last ? '\n' : ',');
    }
}
