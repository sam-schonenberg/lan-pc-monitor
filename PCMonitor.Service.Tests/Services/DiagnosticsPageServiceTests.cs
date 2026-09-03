using PCMonitor.Service.Services;
using Xunit;

namespace PCMonitor.Service.Tests.Services;

public sealed class DiagnosticsPageServiceTests
{
    [Fact]
    public void PageUsesDiagnosticsApisAndContainsNoExternalDependencies()
    {
        var html = new DiagnosticsPageService().CreateHtml();

        Assert.Contains("/api/v1/diagnostics", html);
        Assert.Contains("Windows diagnostics", html);
        Assert.Contains("Older events", html);
        Assert.DoesNotContain("https://", html);
    }
}
