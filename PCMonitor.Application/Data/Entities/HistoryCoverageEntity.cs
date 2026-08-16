using SQLite;
namespace PCMonitor.Application.Data.Entities;

[Table("HistoryCoverage")]
public sealed class HistoryCoverageEntity
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed("IX_HistoryCoverage_Stream_From", 1)] public string StreamId { get; set; } = string.Empty;
    [Indexed("IX_HistoryCoverage_Stream_From", 2)] public long FromSequence { get; set; }
    public long ToSequence { get; set; }
    public long UpdatedUtcTicks { get; set; }
}
