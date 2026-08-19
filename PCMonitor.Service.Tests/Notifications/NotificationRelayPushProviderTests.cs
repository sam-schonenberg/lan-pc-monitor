using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PCMonitor.Service.Alerts;
using PCMonitor.Service.Notifications;
using Xunit;

namespace PCMonitor.Service.Tests.Notifications;

public sealed class NotificationRelayPushProviderTests
{
    [Fact]
    public async Task SendsStructuredAlertWithCapability()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new DelegateHandler(async request =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        var provider = Provider(handler);

        var result = await provider.SendAsync(Device(), Alert("Temperature", "GPU Core Temperature",
            AlertSeverity.Critical, 96, "°C"), CancellationToken.None);

        Assert.Equal(PushDeliveryResult.Delivered, result);
        Assert.Equal("https://relay.example/v1/notifications", captured!.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("send-secret-that-is-long-enough-for-the-relay", captured.Headers.Authorization.Parameter);
        using var payload = JsonDocument.Parse(body!);
        Assert.Equal("temperature-critical", payload.RootElement.GetProperty("event_type").GetString());
        Assert.Equal("GPU Core Temperature", payload.RootElement.GetProperty("sensor").GetString());
        Assert.Equal(96, payload.RootElement.GetProperty("value").GetDouble());
    }

    [Theory]
    [InlineData("Load", "Memory Used", AlertSeverity.Warning, "memory-warning")]
    [InlineData("Fan", "GPU Fan", AlertSeverity.Critical, "fan-critical")]
    [InlineData("Load", "GPU Core", AlertSeverity.Critical, "utilization-critical")]
    public async Task MapsAlertKinds(string sensorType, string sensorName, AlertSeverity severity,
        string expectedEventType)
    {
        string? body = null;
        var provider = Provider(new DelegateHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }));

        await provider.SendAsync(Device(), Alert(sensorType, sensorName, severity, 99, "%"), CancellationToken.None);

        using var payload = JsonDocument.Parse(body!);
        Assert.Equal(expectedEventType, payload.RootElement.GetProperty("event_type").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task MissingDestinationIsReported(HttpStatusCode status)
    {
        var provider = Provider(new DelegateHandler(_ => Task.FromResult(new HttpResponseMessage(status))));
        var result = await provider.SendAsync(Device(), Alert("Temperature", "CPU Package",
            AlertSeverity.Critical, 98, "°C"), CancellationToken.None);
        Assert.Equal(PushDeliveryResult.InvalidDestination, result);
    }

    [Fact]
    public void RequiresEnabledHttpsRelay()
    {
        var provider = new NotificationRelayPushProvider(new HttpClient(new DelegateHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))), Options.Create(new NotificationOptions
        {
            Enabled = true,
            RelayBaseUrl = "http://relay.example"
        }));
        Assert.False(provider.IsConfigured);
    }

    private static NotificationRelayPushProvider Provider(HttpMessageHandler handler) => new(
        new HttpClient(handler), Options.Create(new NotificationOptions
        {
            Enabled = true,
            RelayBaseUrl = "https://relay.example/"
        }));

    private static DeviceRegistration Device() => new("12345678-1234-1234-1234-123456789abc",
        "send-secret-that-is-long-enough-for-the-relay", MobilePlatform.Android, "Phone", DateTimeOffset.UtcNow);

    private static MonitorAlert Alert(string type, string name, AlertSeverity severity, double value, string? unit) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, severity, "/sensor/1", "GPU", name, type, value, 95, unit,
            "Test alert");

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
