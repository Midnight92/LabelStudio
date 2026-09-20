using System.Globalization;
using LabelStudio.Core;

namespace LabelStudio.Devices.Settings;

/// <summary>Readable settings captured before a change (spec §7 "automatic pre-change snapshot"). M2b adds restore.</summary>
public sealed record ConfigurationSnapshot(
    string Serial, string Model, string Firmware, DateTimeOffset TakenAt, string Reason, IReadOnlyDictionary<string, string> Settings);

public interface IConfigurationSnapshotStore
{
    /// <returns>Where the snapshot was written.</returns>
    string Save(ConfigurationSnapshot snapshot);
}

public sealed class ConfigurationSnapshotStore(string directory) : IConfigurationSnapshotStore
{
    public string Save(ConfigurationSnapshot snapshot)
    {
        var reason = SafeFileName.From(snapshot.Reason).Replace('_', '-');
        var name = $"{snapshot.TakenAt.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}-{reason}.json";
        var path = Path.Combine(directory, SafeFileName.From(snapshot.Serial), name);
        new JsonFileStore<ConfigurationSnapshot>(path).Save(snapshot);
        return path;
    }
}
