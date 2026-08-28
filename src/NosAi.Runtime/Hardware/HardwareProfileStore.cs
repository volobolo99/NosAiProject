using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NosAi.Runtime.Hardware;

public sealed record PersistedHardwareProfile(string Fingerprint, HardwareFingerprint Hardware, RuntimeSettings Settings, string PolicyVersion, DateTimeOffset CreatedAtUtc);

public sealed class HardwareProfileStore
{
    private readonly string _path;
    private readonly HardwareAutoSettings _autoSettings;

    public HardwareProfileStore(string path, HardwareAutoSettings autoSettings)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _autoSettings = autoSettings ?? throw new ArgumentNullException(nameof(autoSettings));
    }

    public RuntimeSettings LoadOrCreate(HardwareFingerprint hardware)
    {
        var fingerprint = ComputeFingerprint(hardware);
        if (File.Exists(_path))
        {
            try
            {
                var profile = JsonSerializer.Deserialize<PersistedHardwareProfile>(File.ReadAllText(_path));
                if (profile is not null && profile.Fingerprint == fingerprint)
                    return profile.Settings;
            }
            catch (JsonException) { }
        }

        var settings = _autoSettings.Calculate(hardware);
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        var persisted = new PersistedHardwareProfile(fingerprint, hardware, settings, "1.0", DateTimeOffset.UtcNow);
        File.WriteAllText(_path, JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true }));
        return settings;
    }

    public static string ComputeFingerprint(HardwareFingerprint h)
    {
        var normalized = string.Join("|", h.Platform.Trim().ToUpperInvariant(), h.Cpu.Trim().ToUpperInvariant(), h.LogicalCores, h.RamMb, h.Gpu.Trim().ToUpperInvariant(), h.GpuMemoryMb, h.DisplayRefreshHz, h.OsVersion.Trim().ToUpperInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
