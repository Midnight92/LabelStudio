using LabelStudio.Devices.Settings;
using LabelStudio.Tests;

namespace LabelStudio.Devices.Tests;

public class ConfigurationSnapshotStoreTests
{
    private static ConfigurationSnapshot Snapshot(DateTimeOffset at, string reason = "before-apply") =>
        new("ABC123456789", "ZD220-200dpi", "V84.20.21Z", at, reason, new Dictionary<string, string> { ["print.tone"] = "16.0" });

    [Fact]
    public void Save_writes_one_file_per_snapshot_under_the_serial()
    {
        using var dir = new TempDir();
        var store = new ConfigurationSnapshotStore(dir.File("backups"));
        var start = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

        var first = store.Save(Snapshot(start));
        var second = store.Save(Snapshot(start.AddSeconds(1), "before-commit"));

        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Contains("ABC123456789", first, StringComparison.Ordinal);
        Assert.Contains("before-commit", second, StringComparison.Ordinal);
    }

    /// <summary>
    /// A snapshot is taken before every apply, and media setup applies on a debounce while a slider moves,
    /// so without a limit one editing session can leave hundreds of files behind and nothing ever removes them.
    /// </summary>
    [Fact]
    public void Old_snapshots_are_pruned_once_the_limit_is_passed()
    {
        using var dir = new TempDir();
        var store = new ConfigurationSnapshotStore(dir.File("backups"), retainedPerSerial: 3);
        var start = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

        string? newest = null;
        for (var i = 0; i < 10; i++) newest = store.Save(Snapshot(start.AddSeconds(i)));

        var folder = Path.GetDirectoryName(newest)!;
        var kept = Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal).ToList();
        Assert.Equal(3, kept.Count);
        // The newest survive: names start with a sortable UTC timestamp, so the last three written are the last three names.
        Assert.Equal(newest, kept[^1]);
        Assert.All(kept, f => Assert.DoesNotContain("120000-000", f, StringComparison.Ordinal));
    }

    [Fact]
    public void Each_printer_keeps_its_own_snapshots()
    {
        using var dir = new TempDir();
        var store = new ConfigurationSnapshotStore(dir.File("backups"), retainedPerSerial: 2);
        var start = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 4; i++) store.Save(Snapshot(start.AddSeconds(i)));
        var other = store.Save(Snapshot(start, "before-apply") with { Serial = "XYZ987654321" });

        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(other)!, "*.json"));
    }
}
