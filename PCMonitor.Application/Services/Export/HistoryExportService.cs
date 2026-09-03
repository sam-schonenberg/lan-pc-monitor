using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using PCMonitor.Application.Services.Storage;

namespace PCMonitor.Application.Services.Export;

public sealed class HistoryExportService(HistoryRepository historyRepository, TimeProvider timeProvider)
{
    public async Task<int> ExportAndShareAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var to = timeProvider.GetUtcNow();
        var from = to - duration;
        var rows = await historyRepository.GetAllHistoryAsync(from, to, cancellationToken);
        if (rows.Count == 0) return 0;

        var hours = (int)duration.TotalHours;
        var fileName = $"lan-pc-monitor-sensors-{hours}h-{to:yyyyMMdd-HHmmss}Z.csv";
        var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        await File.WriteAllTextAsync(path, HistoryCsvFormatter.Format(rows), new UTF8Encoding(true), cancellationToken);
        var previous = await GetLatestAsync(cancellationToken);
        if (previous is not null && !string.Equals(previous.FilePath, path, StringComparison.OrdinalIgnoreCase))
            TryDelete(previous.FilePath);
        var latest = new LatestHistoryExport(path, fileName, to, hours, rows.Count);
        await File.WriteAllTextAsync(MetadataPath, JsonSerializer.Serialize(latest), cancellationToken);
        await ShareAsync(latest);
        return rows.Count;
    }

    public async Task<LatestHistoryExport?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(MetadataPath)) return null;
        try
        {
            var latest = JsonSerializer.Deserialize<LatestHistoryExport>(
                await File.ReadAllTextAsync(MetadataPath, cancellationToken));
            return latest is not null && File.Exists(latest.FilePath) ? latest : null;
        }
        catch (JsonException) { return null; }
    }

    public async Task<bool> ShareLatestAsync(CancellationToken cancellationToken = default)
    {
        var latest = await GetLatestAsync(cancellationToken);
        if (latest is null) return false;
        await ShareAsync(latest);
        return true;
    }

    private static string MetadataPath => Path.Combine(FileSystem.AppDataDirectory, "latest-sensor-export.json");
    private static Task ShareAsync(LatestHistoryExport export) => Share.Default.RequestAsync(new ShareFileRequest
    {
        Title = $"Share sensor history from the last {export.Hours} hours",
        File = new ShareFile(export.FilePath)
    });
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}

public sealed record LatestHistoryExport(string FilePath, string FileName, DateTimeOffset CreatedAtUtc,
    int Hours, int ReadingCount);
