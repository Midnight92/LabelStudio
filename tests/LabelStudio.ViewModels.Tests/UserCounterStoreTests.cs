using LabelStudio.Tests;
using LabelStudio.ViewModels.Printers;

namespace LabelStudio.ViewModels.Tests;

public class UserCounterStoreTests
{
    [Fact]
    public void Baselines_are_per_serial_and_persist()
    {
        using var dir = new TempDir();
        new UserCounterStore(dir.File("counters.json")).SetBaseline("ABC123456789", 1200);
        var reopened = new UserCounterStore(dir.File("counters.json"));
        Assert.Equal(1200, reopened.GetBaseline("ABC123456789"));
        Assert.Null(reopened.GetBaseline("OTHER"));
    }
}
