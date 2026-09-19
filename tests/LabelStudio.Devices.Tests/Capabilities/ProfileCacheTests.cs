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
        cache.SaveUnresponsiveKeys("D5J/213", ["b.key", "a.key"]);
        Assert.Equal(["a.key", "b.key"], cache.GetUnresponsiveKeys("D5J/213").Order());
        Assert.Empty(cache.GetUnresponsiveKeys("OTHER"));
        cache.Clear("D5J/213");
        Assert.Empty(cache.GetUnresponsiveKeys("D5J/213"));
    }
}
