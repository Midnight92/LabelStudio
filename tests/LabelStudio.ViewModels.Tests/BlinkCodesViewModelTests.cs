using LabelStudio.Devices.Simulation;
using LabelStudio.Tests;
using LabelStudio.ViewModels.Reference;

namespace LabelStudio.ViewModels.Tests;

public class BlinkCodesViewModelTests
{
    [Fact]
    public void Every_catalog_entry_has_all_text_and_a_source_page()
    {
        foreach (var model in BlinkCodeCatalog.Models)
        {
            var codes = BlinkCodeCatalog.For(model);
            Assert.NotEmpty(codes);
            Assert.All(codes, c => Assert.False(string.IsNullOrWhiteSpace(c.SourcePage)));
        }
        Assert.Contains("ZD220", BlinkCodeCatalog.Models);
    }

    [Fact]
    public async Task Connected_printer_shows_its_model_and_rows_have_resolved_text()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir, new SimulatedPrinter());
        await using var _ = svc;
        await svc.StartAsync(CancellationToken.None);
        var vm = new BlinkCodesViewModel(svc);

        Assert.NotEmpty(vm.Items);
        Assert.All(vm.Items, r =>
        {
            Assert.Equal("ZD220", r.Model);
            Assert.DoesNotContain("[", r.Pattern);   // Strings.Get returns "[key]" when a resx entry is missing
            Assert.DoesNotContain("[", r.Meaning);
            Assert.DoesNotContain("[", r.Action);
        });
    }

    [Fact]
    public async Task Search_filters_on_any_text_case_insensitively()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir, new SimulatedPrinter());
        await using var _ = svc;
        await svc.StartAsync(CancellationToken.None);
        var vm = new BlinkCodesViewModel(svc);
        var total = vm.Items.Count;
        var probe = vm.Items[0].Meaning.Split(' ')[0].ToUpperInvariant();

        vm.SearchText = probe;
        Assert.InRange(vm.Items.Count, 1, total);
        Assert.All(vm.Items, r => Assert.Contains(probe, (r.Pattern + " " + r.Meaning + " " + r.Action).ToUpperInvariant()));

        vm.SearchText = "zzzz-no-such-text";
        Assert.Empty(vm.Items);
        Assert.True(vm.HasNoMatches);
    }

    [Fact]
    public void Without_a_printer_every_catalog_model_is_shown()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir);
        var vm = new BlinkCodesViewModel(svc);
        Assert.NotEmpty(vm.Items);
        Assert.False(vm.HasNoReference);
    }

    [Fact]
    public async Task Unknown_model_shows_the_empty_state()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter { Model = "ZT999-300dpi" };
        printer.Sgd["device.product_name"] = "ZT999";
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(CancellationToken.None);
        var vm = new BlinkCodesViewModel(svc);
        Assert.Empty(vm.Items);
        Assert.True(vm.HasNoReference);
    }
}
