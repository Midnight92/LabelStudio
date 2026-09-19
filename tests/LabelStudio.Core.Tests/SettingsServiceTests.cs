using LabelStudio.Tests;

namespace LabelStudio.Core.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void Update_persists_and_reloads()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        new SettingsService(new JsonFileStore<AppSettings>(path)).Update(s => s with { LastPrinterSerial = "ABC123456789" });
        Assert.Equal("ABC123456789", new SettingsService(new JsonFileStore<AppSettings>(path)).Current.LastPrinterSerial);
    }

    [Fact]
    public void Unchanged_update_does_not_write()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        new SettingsService(new JsonFileStore<AppSettings>(path)).Update(s => s);
        Assert.False(File.Exists(path));
    }
}
