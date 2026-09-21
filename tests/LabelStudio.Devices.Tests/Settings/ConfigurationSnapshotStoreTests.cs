using LabelStudio.Devices.Settings;
using LabelStudio.Tests;

namespace LabelStudio.Devices.Tests.Settings;

public class ConfigurationSnapshotStoreTests
{
    [Fact]
    public void Writes_a_timestamped_file_per_serial()
    {
        using var dir = new TempDir();
        var store = new ConfigurationSnapshotStore(dir.Path);
        var path = store.Save(new ConfigurationSnapshot("ABC/123", "ZD220-200dpi", "V1",
            new DateTimeOffset(2026, 9, 19, 10, 30, 5, 123, TimeSpan.Zero), "before-apply",
            new Dictionary<string, string> { ["print.tone"] = "15.0" }));
        Assert.Equal(Path.Combine(dir.Path, "ABC_123", "20260919-103005-123-before-apply.json"), path);
        Assert.Contains("\"print.tone\": \"15.0\"", File.ReadAllText(path));
    }
}
