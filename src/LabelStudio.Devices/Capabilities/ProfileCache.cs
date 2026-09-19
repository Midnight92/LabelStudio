using LabelStudio.Core;

namespace LabelStudio.Devices.Capabilities;

/// <summary>Remembers which SGD keys a given printer ignores, so reconnects skip their 800 ms timeouts.</summary>
public interface IProfileCache
{
    IReadOnlySet<string> GetUnresponsiveKeys(string serial);
    void SaveUnresponsiveKeys(string serial, IEnumerable<string> keys);
    void Clear(string serial);
}

public sealed class ProfileCache(string directory) : IProfileCache
{
    public IReadOnlySet<string> GetUnresponsiveKeys(string serial) =>
        new HashSet<string>(Store(serial).Load() ?? [], StringComparer.Ordinal);

    public void SaveUnresponsiveKeys(string serial, IEnumerable<string> keys) =>
        Store(serial).Save(keys.Order(StringComparer.Ordinal).ToArray());

    public void Clear(string serial)
    {
        var path = PathFor(serial);
        if (File.Exists(path)) File.Delete(path);
    }

    private JsonFileStore<string[]> Store(string serial) => new(PathFor(serial));

    private string PathFor(string serial) =>
        Path.Combine(directory, string.Concat(serial.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')) + ".unresponsive.json");
}
