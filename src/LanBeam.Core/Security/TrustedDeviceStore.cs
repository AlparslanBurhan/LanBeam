using System.Text.Json;

namespace LanBeam.Core.Security;

public sealed record TrustedDevice(string DeviceId, string Name, string CertFingerprint, DateTimeOffset PairedAt);

/// <summary>Eşleştirilmiş (güvenilir) cihazların kalıcı deposu: %APPDATA%\LanBeam\trusted.json</summary>
public sealed class TrustedDeviceStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private List<TrustedDevice> _devices;

    public event Action? Changed;

    public TrustedDeviceStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "trusted.json");
        _devices = Load();
    }

    private List<TrustedDevice> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<List<TrustedDevice>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception) { }
        return [];
    }

    public IReadOnlyList<TrustedDevice> All()
    {
        lock (_gate) return _devices.ToList();
    }

    public TrustedDevice? FindByFingerprint(string certFingerprint)
    {
        lock (_gate)
            return _devices.FirstOrDefault(d =>
                string.Equals(d.CertFingerprint, certFingerprint, StringComparison.OrdinalIgnoreCase));
    }

    public TrustedDevice? FindByDeviceId(string deviceId)
    {
        lock (_gate) return _devices.FirstOrDefault(d => d.DeviceId == deviceId);
    }

    public bool IsTrusted(string certFingerprint) => FindByFingerprint(certFingerprint) is not null;

    public void AddOrUpdate(string deviceId, string name, string certFingerprint)
    {
        lock (_gate)
        {
            _devices.RemoveAll(d => d.DeviceId == deviceId);
            _devices.Add(new TrustedDevice(deviceId, name, certFingerprint.ToUpperInvariant(), DateTimeOffset.Now));
            Persist();
        }
        Changed?.Invoke();
    }

    public void Remove(string deviceId)
    {
        bool removed;
        lock (_gate)
        {
            removed = _devices.RemoveAll(d => d.DeviceId == deviceId) > 0;
            if (removed) Persist();
        }
        if (removed) Changed?.Invoke();
    }

    private void Persist()
    {
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_devices, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}
