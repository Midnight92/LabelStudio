using LabelStudio.Core;

namespace LabelStudio.Devices.Capabilities;

/// <summary>
/// Remembers which SGD keys a given printer ignores, so reconnects skip their 800 ms timeouts.
/// Keyed on serial and firmware: a firmware update can change the supported key set, so a mismatch
/// forces a full probe (M1 carry-in).
/// </summary>
public interface IProfileCache
{
    IReadOnlySet<string> GetUnresponsiveKeys(string serial, string firmware);
    void SaveUnresponsiveKeys(string serial, string firmware, IEnumerable<string> keys);
    void Clear(string serial);
}

public sealed record CachedProbe(string Firmware, string[] UnresponsiveKeys);

public sealed class ProfileCache(string directory) : IProfileCache
{
    public IReadOnlySet<string> GetUnresponsiveKeys(string serial, string firmware)
    {
        var cached = Store(serial).Load();
        return cached is not null && cached.Firmware == firmware
            ? new HashSet<string>(cached.UnresponsiveKeys, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    public void SaveUnresponsiveKeys(string serial, string firmware, IEnumerable<string> keys)
    {
        Store(serial).Save(new CachedProbe(firmware, keys.Order(StringComparer.Ordinal).ToArray()));
        DeleteIfExists(LegacyPathFor(serial));
    }

    public void Clear(string serial)
    {
        DeleteIfExists(PathFor(serial));
        DeleteIfExists(LegacyPathFor(serial));
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private JsonFileStore<CachedProbe> Store(string serial) => new(PathFor(serial));
    private string PathFor(string serial) => Path.Combine(directory, SafeFileName.From(serial) + ".probe.json");
    /// <summary>M1 format (no firmware): ignored on read, removed on the next save.</summary>
    private string LegacyPathFor(string serial) => Path.Combine(directory, SafeFileName.From(serial) + ".unresponsive.json");
}
