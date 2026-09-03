using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using PCMonitor.Application.Models;
using SkiaSharp;
#if ANDROID
using Android.Content;
using Android.OS;
using Android.Provider;
#endif

namespace PCMonitor.Application.Services.Export;

public sealed record GraphImageExportRequest(
    string Title,
    string PeriodLabel,
    TimeSpan RangeDuration,
    DateTimeOffset RangeEnd,
    string? Unit,
    IReadOnlyList<SensorChartPoint> Points,
    IReadOnlyList<SensorGraphSeries> ComparisonSeries,
    bool ShowAverage,
    bool ShowMinimum,
    bool ShowMaximum,
    string? CurrentValue = null,
    string? DeviceName = null);

public sealed record GraphImageExportResult(
    string Title,
    string FileName,
    string SavedLocation,
    string SavedReference,
    byte[] ImageBytes);

public sealed class GraphImageExportService
{
    public async Task<GraphImageExportResult> GenerateAndSaveAsync(GraphImageExportRequest request,
        CancellationToken cancellationToken = default)
    {
        var capturedAt = DateTimeOffset.Now;
        var safeName = string.Concat(request.Title.Select(character =>
            char.IsLetterOrDigit(character) ? character : '-')).Trim('-');
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "sensor-graph";
        var fileName = $"lan-pc-monitor-{safeName.ToLowerInvariant()}-{capturedAt:yyyyMMdd-HHmmss}.png";
        var appLogo = await ReadAssetAsync("export-appicon.png", cancellationToken);
        var companyLogo = await ReadAssetAsync("logo.png", cancellationToken);
        var png = GraphImageRenderer.Render(request, capturedAt, appLogo, companyLogo);
        var saved = await SaveToDeviceAsync(fileName, png, cancellationToken);
        return new GraphImageExportResult(request.Title, fileName, saved.Location, saved.Reference, png);
    }

    private static async Task<byte[]?> ReadAssetAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(name);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        catch (FileNotFoundException) { return null; }
    }

    public Task ShareAsync(GraphImageExportResult export)
    {
#if ANDROID
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No active Android window.");
        var uri = Android.Net.Uri.Parse(export.SavedReference);
        var intent = new Intent(Intent.ActionSend);
        intent.SetType("image/png");
        intent.PutExtra(Intent.ExtraStream, uri);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        intent.ClipData = ClipData.NewRawUri(export.FileName, uri);
        activity.StartActivity(Intent.CreateChooser(intent, $"Share {export.Title}"));
        return Task.CompletedTask;
#else
        return Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = $"Share {export.Title}", File = new ShareFile(export.SavedReference, "image/png")
        });
#endif
    }

    public Task OpenAsync(GraphImageExportResult export)
    {
#if ANDROID
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No active Android window.");
        var uri = Android.Net.Uri.Parse(export.SavedReference);
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "image/png");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        activity.StartActivity(Intent.CreateChooser(intent, "View graph image"));
        return Task.CompletedTask;
#else
        return Launcher.Default.OpenAsync(new OpenFileRequest("View graph image",
            new ReadOnlyFile(export.SavedReference, "image/png")));
#endif
    }

    private static async Task<(string Location, string Reference)> SaveToDeviceAsync(string fileName, byte[] png,
        CancellationToken cancellationToken)
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            return await SaveToAndroidMediaStoreAsync(fileName, png, cancellationToken);

        var permission = await Permissions.RequestAsync<Permissions.StorageWrite>();
        if (permission != PermissionStatus.Granted)
            throw new InvalidOperationException("Storage permission is needed to save the graph to Photos.");
        return await SaveToAndroidMediaStoreAsync(fileName, png, cancellationToken);
#else
        var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        await File.WriteAllBytesAsync(path, png, cancellationToken);
        return ("LAN PC Monitor files", path);
#endif
    }

#if ANDROID
    private static async Task<(string Location, string Reference)> SaveToAndroidMediaStoreAsync(string fileName,
        byte[] png, CancellationToken cancellationToken)
    {
        var context = Platform.AppContext;
        var resolver = context.ContentResolver ?? throw new InvalidOperationException("Android media storage is unavailable.");
        using var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, "image/png");
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            values.Put(MediaStore.IMediaColumns.RelativePath,
                $"{Android.OS.Environment.DirectoryPictures}/LAN PC Monitor");
            values.Put(MediaStore.IMediaColumns.IsPending, 1);
        }
        else
        {
            var pictures = Android.OS.Environment.GetExternalStoragePublicDirectory(
                Android.OS.Environment.DirectoryPictures)?.AbsolutePath
                ?? throw new InvalidOperationException("The Pictures folder is unavailable.");
            var directory = Path.Combine(pictures, "LAN PC Monitor");
            Directory.CreateDirectory(directory);
            values.Put(MediaStore.IMediaColumns.Data, Path.Combine(directory, fileName));
        }

        var collection = MediaStore.Images.Media.ExternalContentUri
            ?? throw new InvalidOperationException("Android photo storage is unavailable.");
        var uri = resolver.Insert(collection, values)
            ?? throw new IOException("Android could not create the image in Photos.");
        try
        {
            await using (var output = resolver.OpenOutputStream(uri, "w")
                ?? throw new IOException("Android could not open the saved image."))
                await output.WriteAsync(png, cancellationToken);
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                values.Clear();
                values.Put(MediaStore.IMediaColumns.IsPending, 0);
                resolver.Update(uri, values, null, null);
            }
            return ("Pictures/LAN PC Monitor", uri.ToString()!);
        }
        catch
        {
            resolver.Delete(uri, null, null);
            throw;
        }
    }
#endif

}

internal static class GraphImageRenderer
{
    private static readonly SKColor[] SeriesColors = GraphSeriesPalette.DarkSeries.Select(SKColor.Parse).ToArray();

    internal static byte[] Render(GraphImageExportRequest request, DateTimeOffset capturedAt,
        byte[]? appLogoBytes = null, byte[]? companyLogoBytes = null)
    {
        const int width = 1600;
        const int height = 1200;
        using var bitmap = new SKBitmap(width, height, true);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(4, 17, 31));

        DrawLogo(canvas, appLogoBytes, new SKRect(42, 38, 132, 128));
        DrawText(canvas, "LAN PC Monitor", 158, 85, 42, new SKColor(245, 248, 252), true);
        DrawText(canvas, "Schonenberg Developments", 158, 123, 22, new SKColor(151, 165, 185));
        if (!string.IsNullOrWhiteSpace(request.DeviceName))
            DrawText(canvas, FitText(request.DeviceName.ToUpperInvariant(), 30, 420), 1542, 78, 30,
                new SKColor(245, 248, 252), true, SKTextAlign.Right);
        DrawText(canvas, $"{request.Title}  •  {PeriodDescription(request)}", 1542, 122, 21,
            new SKColor(151, 165, 185), false, SKTextAlign.Right);

        var panel = new SKRect(38, 162, 1562, 1074);
        using var panelFill = new SKPaint { Color = new SKColor(7, 25, 43), IsAntialias = true };
        using var panelStroke = new SKPaint { Color = new SKColor(38, 62, 86), StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke, IsAntialias = true };
        var roundedPanel = new SKRoundRect(panel, 26);
        canvas.DrawRoundRect(roundedPanel, panelFill);
        canvas.DrawRoundRect(roundedPanel, panelStroke);
        DrawText(canvas, FitText(request.Title, 34, 800), 78, 232, 34, new SKColor(245, 248, 252), true);

        var dataSeries = BuildSeries(request);
        DrawStatistics(canvas, request, dataSeries);
        DrawLegend(canvas, dataSeries, 78, 286, 1444);
        DrawPlot(canvas, request, dataSeries, new SKRect(145, 430, 1502, 930));
        canvas.DrawLine(78, 978, 1522, 978, panelStroke);
        DrawMetadata(canvas, request, dataSeries, capturedAt);
        DrawBrandFooter(canvas, companyLogoBytes, panelStroke, width / 2f, 1126);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static List<ExportSeries> BuildSeries(GraphImageExportRequest request)
    {
        var result = new List<ExportSeries>();
        if (request.ComparisonSeries.Count > 0)
        {
            for (var index = 0; index < request.ComparisonSeries.Count; index++)
            {
                var item = request.ComparisonSeries[index];
                result.Add(new ExportSeries(item.Name, item.Points.Select(x => (x.Timestamp, x.Average)).ToArray(),
                    SeriesColors[index % SeriesColors.Length]));
            }
            return result;
        }

        if (request.ShowMaximum) result.Add(new("Maximum", request.Points.Select(x => (x.Timestamp, x.Maximum)).ToArray(),
            SKColor.Parse(GraphSeriesPalette.DarkBoundary)));
        if (request.ShowAverage) result.Add(new("Average", request.Points.Select(x => (x.Timestamp, x.Average)).ToArray(),
            SeriesColors[0]));
        if (request.ShowMinimum) result.Add(new("Minimum", request.Points.Select(x => (x.Timestamp, x.Minimum)).ToArray(),
            SKColor.Parse(GraphSeriesPalette.DarkBoundary).WithAlpha(180)));
        return result;
    }

    private static void DrawStatistics(SKCanvas canvas, GraphImageExportRequest request,
        IReadOnlyList<ExportSeries> series)
    {
        var points = request.ComparisonSeries.Count > 0
            ? request.ComparisonSeries.SelectMany(x => x.Points).ToArray()
            : request.Points;
        if (points.Count == 0) return;
        var count = points.Sum(x => Math.Max(1, x.SampleCount));
        var average = points.Sum(x => x.Average * Math.Max(1, x.SampleCount)) / count;
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.CurrentValue)) values.Add($"Current {request.CurrentValue}");
        values.Add($"Min {FormatValue(points.Min(x => x.Minimum), request.Unit)}");
        values.Add($"Avg {FormatValue(average, request.Unit)}");
        values.Add($"Max {FormatValue(points.Max(x => x.Maximum), request.Unit)}");
        DrawText(canvas, string.Join("   •   ", values), 1522, 230, 18, new SKColor(170, 183, 201), false,
            SKTextAlign.Right);
    }

    private static void DrawPlot(SKCanvas canvas, GraphImageExportRequest request,
        IReadOnlyList<ExportSeries> series, SKRect plot)
    {
        var values = series.SelectMany(x => x.Values).Select(x => x.Value).Where(double.IsFinite).ToArray();
        using var grid = new SKPaint { Color = new SKColor(45, 70, 94), StrokeWidth = 1,
            PathEffect = SKPathEffect.CreateDash([5, 6], 0), IsAntialias = true };
        if (values.Length == 0)
        {
            DrawText(canvas, "No history available for this range", plot.MidX, plot.MidY, 25,
                new SKColor(151, 165, 185), false, SKTextAlign.Center);
            return;
        }
        var min = values.Min(); var max = values.Max();
        var padding = Math.Max((max - min) * .1, Math.Max(Math.Abs(max), 1) * .03);
        min -= padding; max += padding;
        var rangeEnd = request.RangeEnd == default ? DateTimeOffset.UtcNow : request.RangeEnd.ToUniversalTime();
        var duration = request.RangeDuration <= TimeSpan.Zero ? TimeSpan.FromHours(24) : request.RangeDuration;
        var rangeStart = rangeEnd - duration;
        for (var index = 0; index <= 4; index++)
        {
            var y = plot.Top + plot.Height * index / 4;
            canvas.DrawLine(plot.Left, y, plot.Right, y, grid);
            DrawText(canvas, FormatValue(max - (max - min) * index / 4, request.Unit), plot.Left - 18, y + 7,
                17, new SKColor(214, 222, 233), false, SKTextAlign.Right);
        }
        for (var index = 0; index <= 5; index++)
        {
            var x = plot.Left + plot.Width * index / 5;
            canvas.DrawLine(x, plot.Top, x, plot.Bottom, grid);
            var timestamp = rangeStart + TimeSpan.FromTicks((long)(duration.Ticks * index / 5d));
            DrawText(canvas, FormatAxisTime(timestamp, duration), x, plot.Bottom + 38, 16,
                new SKColor(214, 222, 233), false, SKTextAlign.Center);
        }

        var expected = ExpectedInterval(duration);
        foreach (var item in series)
        {
            using var line = new SKPaint { Color = item.Color, StrokeWidth = item.Name == "Average" ? 5 : 4,
                IsAntialias = true, StrokeCap = SKStrokeCap.Round, Style = SKPaintStyle.Stroke };
            using var path = new SKPath();
            DateTimeOffset? previous = null; var started = false;
            foreach (var point in item.Values.OrderBy(x => x.Timestamp))
            {
                if (!double.IsFinite(point.Value) || point.Timestamp < rangeStart || point.Timestamp > rangeEnd) continue;
                var x = plot.Left + (float)((point.Timestamp - rangeStart).Ticks / (double)duration.Ticks * plot.Width);
                var y = plot.Bottom - (float)((point.Value - min) / (max - min) * plot.Height);
                if (!started || previous is not null && point.Timestamp - previous > expected * 1.5)
                    path.MoveTo(x, y);
                else path.LineTo(x, y);
                started = true; previous = point.Timestamp;
            }
            canvas.DrawPath(path, line);
        }
    }

    private static void DrawLegend(SKCanvas canvas, IReadOnlyList<ExportSeries> series, float startX, float startY,
        float maximumWidth)
    {
        var x = startX;
        var y = startY;
        foreach (var item in series)
        {
            var label = FitText(TrimSensorName(item.Name), 17, 240);
            var chipWidth = 62 + MeasureText(label, 17);
            if (x + chipWidth > startX + maximumWidth) { x = startX; y += 58; }
            using var chipFill = new SKPaint { Color = new SKColor(9, 30, 50), IsAntialias = true };
            using var chipStroke = new SKPaint { Color = new SKColor(43, 68, 92), StrokeWidth = 1,
                Style = SKPaintStyle.Stroke, IsAntialias = true };
            var chip = new SKRoundRect(new SKRect(x, y - 29, x + chipWidth, y + 14), 9);
            canvas.DrawRoundRect(chip, chipFill);
            canvas.DrawRoundRect(chip, chipStroke);
            using var marker = new SKPaint { Color = item.Color, StrokeWidth = 5, StrokeCap = SKStrokeCap.Round,
                IsAntialias = true };
            canvas.DrawLine(x + 20, y - 8, x + 48, y - 8, marker);
            canvas.DrawCircle(x + 34, y - 8, 5, marker);
            DrawText(canvas, label, x + 57, y, 17, new SKColor(225, 232, 241));
            x += chipWidth + 18;
        }
    }

    private static void DrawMetadata(SKCanvas canvas, GraphImageExportRequest request,
        IReadOnlyList<ExportSeries> series, DateTimeOffset capturedAt)
    {
        var measurementCount = request.ComparisonSeries.Count > 0 ? request.ComparisonSeries.Count : series.Count;
        DrawText(canvas, $"✓   {measurementCount} selected measurement{(measurementCount == 1 ? string.Empty : "s")}",
            92, 1035, 18, new SKColor(151, 165, 185));
        var interval = SampleInterval(request);
        DrawText(canvas, $"◷   Sample interval: {interval}", 800, 1035, 18,
            new SKColor(151, 165, 185), false, SKTextAlign.Center);
        DrawText(canvas, $"Generated {capturedAt:HH:mm}", 1508, 1035, 18,
            new SKColor(151, 165, 185), false, SKTextAlign.Right);
    }

    private static void DrawLogo(SKCanvas canvas, byte[]? logoBytes, SKRect destination)
    {
        if (logoBytes is null || logoBytes.Length == 0) return;
        using var logo = SKBitmap.Decode(logoBytes);
        if (logo is null) return;
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(logo, destination, paint);
    }

    private static void DrawBrandFooter(SKCanvas canvas, byte[]? companyLogoBytes, SKPaint dividerPaint,
        float centerX, float centerY)
    {
        const float fontSize = 19;
        const float textGap = 12;
        const float sectionGap = 24;
        const float dividerGap = 20;
        const float logoSize = 46;
        const float logoTextGap = 16;
        const string prefix = "Generated with";
        const string product = "LAN PC Monitor";
        const string company = "Schonenberg Developments";
        var prefixWidth = MeasureText(prefix, fontSize);
        var productWidth = MeasureText(product, fontSize, true);
        var companyWidth = MeasureText(company, fontSize);
        var totalWidth = prefixWidth + textGap + productWidth + sectionGap + dividerGap * 2 +
                         logoSize + logoTextGap + companyWidth;
        var x = centerX - totalWidth / 2;

        DrawTextCenteredVertically(canvas, prefix, x, centerY, fontSize, new SKColor(151, 165, 185));
        x += prefixWidth + textGap;
        DrawTextCenteredVertically(canvas, product, x, centerY, fontSize, new SKColor(0, 216, 240), true);
        x += productWidth + sectionGap + dividerGap;
        canvas.DrawLine(x, centerY - 18, x, centerY + 18, dividerPaint);
        x += dividerGap;
        DrawLogo(canvas, companyLogoBytes,
            new SKRect(x, centerY - logoSize / 2, x + logoSize, centerY + logoSize / 2));
        x += logoSize + logoTextGap;
        DrawTextCenteredVertically(canvas, company, x, centerY, fontSize, new SKColor(151, 165, 185));
    }

    private static string SampleInterval(GraphImageExportRequest request)
    {
        var points = request.ComparisonSeries.FirstOrDefault(x => x.Points.Count > 1)?.Points ?? request.Points;
        if (points.Count < 2) return "—";
        var ordered = points.OrderBy(x => x.Timestamp).ToArray();
        var intervals = ordered.Zip(ordered.Skip(1), (first, second) => second.Timestamp - first.Timestamp)
            .Where(x => x > TimeSpan.Zero).OrderBy(x => x).ToArray();
        if (intervals.Length == 0) return "—";
        var median = intervals[intervals.Length / 2];
        return median.TotalDays >= 1 ? $"{median.TotalDays:0.#}d" : median.TotalHours >= 1
            ? $"{median.TotalHours:0.#}h" : median.TotalMinutes >= 1 ? $"{median.TotalMinutes:0.#}m"
            : $"{median.TotalSeconds:0.#}s";
    }

    private static string PeriodDescription(GraphImageExportRequest request) => request.RangeDuration switch
    {
        var value when value.TotalDays >= 365 => "Last 1 year",
        var value when value.TotalDays >= 1 => $"Last {value.TotalDays:0} day{(value.TotalDays == 1 ? string.Empty : "s")}",
        var value when value.TotalHours >= 1 => $"Last {value.TotalHours:0} hour{(value.TotalHours == 1 ? string.Empty : "s")}",
        _ => request.PeriodLabel
    };

    private static string FormatRange(GraphImageExportRequest request)
    {
        var end = request.RangeEnd == default ? DateTimeOffset.UtcNow : request.RangeEnd;
        return $"{end.Subtract(request.RangeDuration).ToLocalTime():dd MMM yyyy, HH:mm} – {end.ToLocalTime():dd MMM yyyy, HH:mm}";
    }

    private static string FormatValue(double value, string? unit) =>
        string.IsNullOrWhiteSpace(unit) ? $"{value:0.#}" : $"{value:0.#} {unit}";
    private static string FormatAxisTime(DateTimeOffset value, TimeSpan duration) => duration <= TimeSpan.FromDays(1)
        ? value.ToLocalTime().ToString("HH:mm") : duration <= TimeSpan.FromDays(7)
            ? value.ToLocalTime().ToString("ddd HH:mm") : value.ToLocalTime().ToString("dd MMM");
    private static TimeSpan ExpectedInterval(TimeSpan duration) => duration <= TimeSpan.FromDays(1)
        ? TimeSpan.FromMinutes(1) : duration <= TimeSpan.FromDays(30) ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
    private static string TrimSensorName(string name) => name.EndsWith(" Temperature", StringComparison.OrdinalIgnoreCase)
        ? name[..^" Temperature".Length] : name;

    private static string FitText(string text, float size, float maximumWidth)
    {
        if (MeasureText(text, size) <= maximumWidth) return text;
        const string ellipsis = "…";
        while (text.Length > 1 && MeasureText(text + ellipsis, size) > maximumWidth)
            text = text[..^1];
        return text + ellipsis;
    }

    private static void DrawText(SKCanvas canvas, string text, float x, float y, float size, SKColor color,
        bool bold = false, SKTextAlign align = SKTextAlign.Left)
    {
        var typeface = bold ? SKTypeface.FromFamilyName(null, SKFontStyle.Bold) : SKTypeface.Default;
        using var font = new SKFont(typeface, size);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, y, align, font, paint);
    }

    private static void DrawTextCenteredVertically(SKCanvas canvas, string text, float x, float centerY,
        float size, SKColor color, bool bold = false)
    {
        var typeface = bold ? SKTypeface.FromFamilyName(null, SKFontStyle.Bold) : SKTypeface.Default;
        using var font = new SKFont(typeface, size);
        var metrics = font.Metrics;
        var baseline = centerY - (metrics.Ascent + metrics.Descent) / 2;
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, paint);
    }

    private static float MeasureText(string text, float size, bool bold = false)
    {
        var typeface = bold ? SKTypeface.FromFamilyName(null, SKFontStyle.Bold) : SKTypeface.Default;
        using var font = new SKFont(typeface, size);
        return font.MeasureText(text);
    }

    private sealed record ExportSeries(string Name, IReadOnlyList<(DateTimeOffset Timestamp, double Value)> Values,
        SKColor Color);
}
