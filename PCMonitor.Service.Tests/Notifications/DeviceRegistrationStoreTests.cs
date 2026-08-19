using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PCMonitor.Service.Notifications;
using Xunit;

namespace PCMonitor.Service.Tests.Notifications;

public sealed class DeviceRegistrationStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pcmonitor-devices-{Guid.NewGuid():N}");
    private readonly DateTimeOffset _now = new(2026, 8, 17, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UpsertPersistsAndReplacesInstallation()
    {
        var store = CreateStore();
        store.Upsert(new("phone-1", "old-token", MobilePlatform.Android, "Sam's phone"));
        store.Upsert(new("phone-1", "new-token", MobilePlatform.Android, "Sam's phone"));

        var restored = CreateStore().GetAll();
        var device = Assert.Single(restored);
        Assert.Equal("new-token", device.SendSecret);
        Assert.Equal(_now, device.UpdatedAt);
    }

    [Fact]
    public void RemoveDeletesExpiredRegistration()
    {
        var store = CreateStore();
        store.Upsert(new("phone-1", "expired", MobilePlatform.Ios, null));
        store.Remove("phone-1");

        Assert.Empty(CreateStore().GetAll());
    }

    private DeviceRegistrationStore CreateStore() => new(
        Options.Create(new NotificationOptions { DeviceStoreFile = Path.Combine(_directory, "devices.json") }),
        new FixedTimeProvider(_now), NullLogger<DeviceRegistrationStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
