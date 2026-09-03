using System.Text.Json;
using PCMonitor.Application.Models.Api;
using Xunit;

namespace PCMonitor.Application.Tests;

public sealed class WindowsDiagnosticsApiDtoTests
{
    [Fact]
    public void DeserializesReadableDiagnosticEventResponse()
    {
        const string json = """
            {
              "fromSequence": 12,
              "toSequence": 12,
              "hasMore": false,
              "events": [{
                "sequence": 12,
                "timestamp": "2026-09-02T10:15:00Z",
                "channel": "System",
                "provider": "Microsoft-Windows-Kernel-Power",
                "eventId": 41,
                "version": 9,
                "windowsLevel": 1,
                "recordId": 182934,
                "severity": "critical",
                "category": "unexpected-shutdown",
                "occurrenceCount": 1,
                "title": "Unexpected shutdown",
                "summary": "Windows detected an unclean restart."
              }]
            }
            """;

        var response = JsonSerializer.Deserialize<WindowsDiagnosticEventsResponseDto>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var item = Assert.Single(response!.Events);
        Assert.Equal("Unexpected shutdown", item.Title);
        Assert.Equal("Windows detected an unclean restart.", item.Summary);
        Assert.Equal(41, item.EventId);
        Assert.Equal("critical", item.Severity);
    }
}
