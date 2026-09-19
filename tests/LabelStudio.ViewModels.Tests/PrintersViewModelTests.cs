using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Simulation;
using LabelStudio.Tests;
using LabelStudio.ViewModels.Printers;

namespace LabelStudio.ViewModels.Tests;

public class PrintersViewModelTests
{
    [Fact]
    public async Task Shows_identity_and_only_responding_media_settings()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = new PrintersViewModel(svc, new ImmediateDispatcher());
        await svc.StartAsync(CancellationToken.None);

        Assert.True(vm.IsConnected);
        Assert.Equal("ZD220t", vm.Heading);
        Assert.Contains(vm.Identity, r => r.Value == printer.Serial);
        Assert.Contains(vm.Media, r => r.Value == "gap/notch");
        Assert.DoesNotContain(vm.Media, r => r.Label == "Tear-off position"); // simulator answers "?" → hidden, not broken
        Assert.Contains(vm.ProbedKeys, k => k.Key == SgdKeys.TearOff && !k.Responded);
        Assert.Single(vm.Printers);
    }

    [Fact]
    public async Task Probed_key_rows_have_distinct_nonempty_glyphs()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = new PrintersViewModel(svc, new ImmediateDispatcher());
        await svc.StartAsync(CancellationToken.None);

        Assert.NotEmpty(vm.ProbedKeys);
        Assert.All(vm.ProbedKeys, k => Assert.False(string.IsNullOrEmpty(k.Glyph)));
        var respondedGlyph = vm.ProbedKeys.First(k => k.Responded).Glyph;
        var noResponseGlyph = vm.ProbedKeys.First(k => !k.Responded).Glyph;
        Assert.NotEqual(respondedGlyph, noResponseGlyph);
    }

    [Fact]
    public async Task Test_label_command_prints()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = new PrintersViewModel(svc, new ImmediateDispatcher());
        await svc.StartAsync(CancellationToken.None);
        await vm.PrintTestLabelCommand.ExecuteAsync(null);
        Assert.Single(printer.ReceivedJobs);
        Assert.Null(vm.CommandError);
    }

    [Fact]
    public async Task No_printers_shows_empty_state()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir);
        await using var _ = svc;
        using var vm = new PrintersViewModel(svc, new ImmediateDispatcher());
        await svc.StartAsync(CancellationToken.None);
        Assert.True(vm.HasNoPrinters);
        Assert.False(vm.PrintTestLabelCommand.CanExecute(null));
        Assert.False(vm.FeedCommand.CanExecute(null));
        Assert.Equal("Connect a printer to use these actions.", vm.ActionHint);
    }

    [Fact]
    public async Task Fault_disables_print_and_feed_but_not_reprobe()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = new PrintersViewModel(svc, new ImmediateDispatcher());
        await svc.StartAsync(CancellationToken.None);

        printer.PaperOut = true;
        await svc.RefreshAsync(CancellationToken.None);

        Assert.False(vm.PrintTestLabelCommand.CanExecute(null));
        Assert.False(vm.FeedCommand.CanExecute(null));
        Assert.True(vm.ReprobeCommand.CanExecute(null));
        Assert.Equal("Clear the printer fault to print or feed.", vm.ActionHint);
    }

    [Fact]
    public async Task Paused_allows_feed_but_not_print()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = new PrintersViewModel(svc, new ImmediateDispatcher());
        await svc.StartAsync(CancellationToken.None);

        printer.Paused = true;
        await svc.RefreshAsync(CancellationToken.None);

        Assert.True(vm.FeedCommand.CanExecute(null));
        Assert.False(vm.PrintTestLabelCommand.CanExecute(null));
        Assert.Equal("Resume the printer to print a test label.", vm.ActionHint);
    }

    [Fact]
    public async Task Ready_allows_print_and_feed_with_no_hint()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = new PrintersViewModel(svc, new ImmediateDispatcher());
        await svc.StartAsync(CancellationToken.None);

        Assert.True(vm.PrintTestLabelCommand.CanExecute(null));
        Assert.True(vm.FeedCommand.CanExecute(null));
        Assert.Null(vm.ActionHint);
    }

    [Fact]
    public async Task Connecting_state_reports_IsConnecting()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        using var vm = new PrintersViewModel(svc, new ImmediateDispatcher());
        var seen = new List<bool>();
        svc.SnapshotChanged += (_, _) => seen.Add(vm.IsConnecting);

        await svc.StartAsync(CancellationToken.None);

        Assert.Contains(true, seen);
        Assert.False(vm.IsConnecting);
    }
}
