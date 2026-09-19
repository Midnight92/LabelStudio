using LabelStudio.Tests;

namespace LabelStudio.Core.Tests;

public class JsonFileStoreTests
{
    private sealed record Sample(string Name, int Count);

    [Fact]
    public void Save_then_Load_round_trips()
    {
        using var dir = new TempDir();
        var store = new JsonFileStore<Sample>(dir.File("nested/sample.json"));
        store.Save(new Sample("roll", 3));
        Assert.Equal(new Sample("roll", 3), store.Load());
    }

    [Fact]
    public void Load_returns_null_when_file_missing()
    {
        using var dir = new TempDir();
        Assert.Null(new JsonFileStore<Sample>(dir.File("missing.json")).Load());
    }

    [Fact]
    public void Load_returns_null_when_file_corrupt()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("bad.json"), "{ not json");
        Assert.Null(new JsonFileStore<Sample>(dir.File("bad.json")).Load());
    }
}
