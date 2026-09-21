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

/// <param name="retainedPerSerial">
/// How many snapshots to keep for each printer. A snapshot is taken before every apply, and media setup
/// applies on a debounce while a slider moves, so without a limit the directory grows without bound.
/// </param>
public sealed class ConfigurationSnapshotStore(string directory, int retainedPerSerial = 50) : IConfigurationSnapshotStore
{
    public string Save(ConfigurationSnapshot snapshot)
    {
        var reason = SafeFileName.From(snapshot.Reason).Replace('_', '-');
        var name = $"{snapshot.TakenAt.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}-{reason}.json";
        var folder = Path.Combine(directory, SafeFileName.From(snapshot.Serial));
        var path = Path.Combine(folder, name);
        new JsonFileStore<ConfigurationSnapshot>(path).Save(snapshot);
        Prune(folder);
        return path;
    }

    /// <summary>Deletes the oldest snapshots past the limit. Names sort chronologically, so ordinal order is age order.</summary>
    private void Prune(string folder)
    {
        try
        {
            var files = Directory.GetFiles(folder, "*.json");
            if (files.Length <= retainedPerSerial) return;
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var stale in files.Take(files.Length - retainedPerSerial)) File.Delete(stale);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Pruning is housekeeping: never fail a write, which would abort the settings change behind it.
        }
    }
}
