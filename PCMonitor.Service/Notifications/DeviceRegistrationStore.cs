using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PCMonitor.Service.Notifications;

public sealed class DeviceRegistrationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _sync = new();
    private readonly Dictionary<string, DeviceRegistration> _devices = new(StringComparer.Ordinal);
    private readonly string _path;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeviceRegistrationStore> _logger;

    public DeviceRegistrationStore(IOptions<NotificationOptions> options, TimeProvider timeProvider,
        ILogger<DeviceRegistrationStore> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _path = string.IsNullOrWhiteSpace(options.Value.DeviceStoreFile)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LanPcMonitor", "notifications", "devices.json")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.Value.DeviceStoreFile));
        Restore();
    }

    public IReadOnlyList<DeviceRegistration> GetAll()
    {
        lock (_sync) return _devices.Values.ToArray();
    }

    public DeviceRegistration Upsert(DeviceRegistrationRequest request)
    {
        var registration = new DeviceRegistration(request.InstallationId.Trim(), request.Token.Trim(),
            request.Platform, string.IsNullOrWhiteSpace(request.DeviceName) ? null : request.DeviceName.Trim(),
            _timeProvider.GetUtcNow());
        lock (_sync)
        {
            _devices[registration.InstallationId] = registration;
            PersistLocked();
        }
        return registration;
    }

    public bool Remove(string installationId)
    {
        lock (_sync)
        {
            if (!_devices.Remove(installationId)) return false;
            PersistLocked();
            return true;
        }
    }

    public void RemoveByToken(string token)
    {
        lock (_sync)
        {
            var ids = _devices.Values.Where(x => x.Token == token).Select(x => x.InstallationId).ToArray();
            if (ids.Length == 0) return;
            foreach (var id in ids) _devices.Remove(id);
            PersistLocked();
        }
    }

    private void Restore()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var devices = JsonSerializer.Deserialize<DeviceRegistration[]>(File.ReadAllText(_path), JsonOptions) ?? [];
            foreach (var device in devices.Where(x => !string.IsNullOrWhiteSpace(x.InstallationId) &&
                                                       !string.IsNullOrWhiteSpace(x.Token)))
                _devices[device.InstallationId] = device;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not restore notification device registrations from {Path}", _path);
        }
    }

    private void PersistLocked()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_devices.Values.ToArray(), JsonOptions));
            File.Move(temporary, _path, true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not persist notification device registrations to {Path}", _path);
            throw;
        }
    }
}
