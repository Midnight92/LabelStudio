using LabelStudio.Devices.Capabilities;
using LabelStudio.Tests;

namespace LabelStudio.Devices.Tests.Capabilities;

public class ProfileCacheTests
{
    [Fact]
    public void Round_trips_and_clears_per_serial()
    {
        using var dir = new TempDir();
        var cache = new ProfileCache(dir.Path);
        cache.SaveUnresponsiveKeys("ABC/123", "V1", ["b.key", "a.key"]);
        Assert.Equal(["a.key", "b.key"], cache.GetUnresponsiveKeys("ABC/123", "V1").Order());
        Assert.Empty(cache.GetUnresponsiveKeys("OTHER", "V1"));
        cache.Clear("ABC/123");
        Assert.Empty(cache.GetUnresponsiveKeys("ABC/123", "V1"));
    }

    [Fact]
    public void Firmware_change_invalidates_the_cached_keys()
    {
        using var dir = new TempDir();
        var cache = new ProfileCache(dir.Path);
        cache.SaveUnresponsiveKeys("ABC123456789", "V1", ["a.key"]);
        Assert.Empty(cache.GetUnresponsiveKeys("ABC123456789", "V2"));
    }

    [Fact]
    public void Legacy_unversioned_file_is_ignored_and_removed_on_save()
    {
        using var dir = new TempDir();
        var legacy = Path.Combine(dir.Path, "ABC123456789.unresponsive.json");
        File.WriteAllText(legacy, "[\"a.key\"]");
        var cache = new ProfileCache(dir.Path);
        Assert.Empty(cache.GetUnresponsiveKeys("ABC123456789", "V1"));
        cache.SaveUnresponsiveKeys("ABC123456789", "V1", ["b.key"]);
        Assert.False(File.Exists(legacy));
    }
}
