using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Simulation;
using LabelStudio.Tests;
using LabelStudio.ViewModels.Printers;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelStudio.ViewModels.Tests;

public class MediaSetupViewModelTests
{
    private static async Task<(MediaSetupViewModel Vm, SimulatedPrinter Printer, Microsoft.Extensions.Time.Testing.FakeTimeProvider Time, IAsyncDisposable Svc)> CreateAsync(
        TempDir dir, Action<SimulatedPrinter>? setup = null)
    {
        var printer = new SimulatedPrinter();
        setup?.Invoke(printer);
        var (svc, _, _, time) = TestDevices.CreateTimed(dir, printer);
        await svc.StartAsync(CancellationToken.None);
        var vm = new MediaSetupViewModel(svc, new ImmediateDispatcher(), time, NullLogger<MediaSetupViewModel>.Instance);
        return (vm, printer, time, svc);
    }

    [Fact]
    public async Task Rows_follow_the_probe_and_the_variant()
    {
        using var dir = new TempDir();
        var (vm, _, _, svc) = await CreateAsync(dir);
        await using var _ = svc;
        using var __ = vm;
        Assert.True(vm.IsDarknessVisible);
        Assert.True(vm.IsPrintMethodVisible);   // thermal-transfer simulator
        Assert.False(vm.IsTearOffVisible);      // simulator doesn't answer ezpl.tear_off: hidden, not broken
        Assert.True(vm.IsSpeedVisible);
        Assert.Equal(1, vm.MediaTypeIndex);     // gap/notch
        Assert.Equal(20.0, vm.Darkness);
        Assert.True(vm.CanEdit);
    }

    [Fact]
    public async Task Print_method_is_hidden_on_direct_thermal_printers()
    {
        using var dir = new TempDir();
        var (vm, _, _, svc) = await CreateAsync(dir, p => p.Sgd["ezpl.print_method"] = "direct thermal");
        await using var _ = svc;
        using var __ = vm;
        Assert.False(vm.IsPrintMethodVisible);
    }

    [Fact]
    public async Task Edit_applies_after_the_debounce_and_marks_dirty_until_saved()
    {
        using var dir = new TempDir();
        var (vm, printer, time, svc) = await CreateAsync(dir);
        await using var _ = svc;
        using var __ = vm;

        vm.Darkness = 16;
        Assert.Empty(printer.SetVarRequests);          // not yet: debounced
        time.Advance(MediaSetupViewModel.Debounce);
        await TestDevices.WaitUntilAsync(() => vm.IsDirty);
        Assert.Equal("16.0", printer.Sgd[SgdKeys.Darkness]);

        await vm.SaveToPrinterCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        printer.PowerCycle();
        Assert.Equal("16.0", printer.Sgd[SgdKeys.Darkness]);
    }

    [Fact]
    public async Task Print_width_is_clamped_to_the_model_maximum()
    {
        using var dir = new TempDir();
        var (vm, printer, _, svc) = await CreateAsync(dir);
        await using var _ = svc;
        using var __ = vm;
        vm.PrintWidthDots = 2000;
        Assert.Equal(832, vm.PrintWidthDots);
        await vm.ApplyPendingAsync();
        Assert.Equal("832", printer.Sgd[SgdKeys.PrintWidth]);
    }

    [Fact]
    public async Task Length_only_setup_switches_to_continuous_with_the_length()
    {
        using var dir = new TempDir();
        var (vm, printer, _, svc) = await CreateAsync(dir);
        await using var _ = svc;
        using var __ = vm;
        vm.LengthOnlyDots = 400;
        await vm.ApplyLengthOnlyCommand.ExecuteAsync(null);
        Assert.Equal("continuous", printer.Sgd[SgdKeys.MediaType]);
        Assert.Equal("400", printer.Sgd[SgdKeys.LabelLength]);
        Assert.Equal(0, vm.MediaTypeIndex);
    }

    [Fact]
    public async Task Length_below_the_minimum_is_refused_with_an_explanation()
    {
        using var dir = new TempDir();
        var (vm, printer, _, svc) = await CreateAsync(dir);
        await using var _ = svc;
        using var __ = vm;
        vm.LengthOnlyDots = 100;
        await vm.ApplyLengthOnlyCommand.ExecuteAsync(null);
        Assert.NotNull(vm.CommandError);
        Assert.Empty(printer.SetVarRequests);
    }

    [Fact]
    public async Task Rejected_value_is_reported()
    {
        using var dir = new TempDir();
        var (vm, _, _, svc) = await CreateAsync(dir, p => p.ReadOnlyKeys.Add("print.tone"));
        await using var _ = svc;
        using var __ = vm;
        vm.Darkness = 16;
        await vm.ApplyPendingAsync();
        Assert.NotNull(vm.CommandError);
        Assert.Equal(20.0, vm.Darkness);
    }

    [Fact]
    public async Task Disconnected_shows_the_offline_state()
    {
        using var dir = new TempDir();
        var (svc, _, _, time) = TestDevices.CreateTimed(dir);
        await using var _ = svc;
        using var vm = new MediaSetupViewModel(svc, new ImmediateDispatcher(), time, NullLogger<MediaSetupViewModel>.Instance);
        Assert.False(vm.IsConnected);
        Assert.False(vm.IsDarknessVisible);
        // Slider-bound values must always be a real number: WinUI's RangeBase.Value throws ArgumentException on
        // NaN, which aborts the whole x:Bind Bindings.Update() pass for the page (M2a fix round 1).
        Assert.False(double.IsNaN(vm.Darkness));
        Assert.False(double.IsNaN(vm.TearOff));
    }

    /// <summary>
    /// Reproduces the crash a real run hit: the simulator never answers ezpl.tear_off (see
    /// Rows_follow_the_probe_and_the_variant), so CapabilityProfile.Get returns null for it. Number(null) is
    /// NaN, and that used to be assigned straight to TearOff even though the Slider that binds it is merely
    /// hidden (IsTearOffVisible = false), not absent from the visual tree — so its Value binding still ran and
    /// threw, aborting every binding after it on the page.
    /// </summary>
    [Fact]
    public async Task TearOff_is_never_NaN_even_though_the_key_is_unsupported()
    {
        using var dir = new TempDir();
        var (vm, _, _, svc) = await CreateAsync(dir);
        await using var _ = svc;
        using var __ = vm;
        Assert.False(vm.IsTearOffVisible);
        Assert.False(double.IsNaN(vm.TearOff));
        Assert.False(double.IsNaN(vm.Darkness));
    }
}
