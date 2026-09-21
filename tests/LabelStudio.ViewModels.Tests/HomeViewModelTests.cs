using LabelStudio.Core;
using LabelStudio.Devices;
using LabelStudio.Devices.Simulation;
using LabelStudio.Tests;
using LabelStudio.ViewModels.Home;
using LabelStudio.ViewModels.Printers;
using LabelStudio.ViewModels.Status;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelStudio.ViewModels.Tests;

public class HomeViewModelTests
{
    private static HomeViewModel Create(DeviceService svc, ISettingsService settings, StartupState startup, RecordingNavigation nav)
    {
        var ui = new ImmediateDispatcher();
        var firstRun = new FirstRunViewModel(svc, ui, nav, settings,
            new CalibrationViewModel(svc, ui, NullLogger<CalibrationViewModel>.Instance),
            new CompatibilityCheckerViewModel(svc, ui), NullLogger<FirstRunViewModel>.Instance);
        return new HomeViewModel(svc, ui, nav, startup, settings, firstRun, NullLogger<HomeViewModel>.Instance);
    }

    [Fact]
    public void First_run_shows_only_for_a_never_connected_install()
    {
        using var dir = new TempDir();
        var (svc, _, settings) = TestDevices.Create(dir);
        Assert.True(StartupState.From(new AppSettings()).IsFirstRun);
        Assert.False(StartupState.From(new AppSettings { LastPrinterSerial = "ABC123456789" }).IsFirstRun);
        Assert.False(StartupState.From(new AppSettings { FirstRunCompleted = true }).IsFirstRun);

        using var vm = Create(svc, settings, new StartupState(true), new RecordingNavigation());
        Assert.True(vm.ShowFirstRun);
        vm.FirstRun.SkipSetupCommand.Execute(null);
        Assert.False(vm.ShowFirstRun);
    }

    [Fact]
    public async Task Printer_cards_show_status_and_calibrate_deep_links()
    {
        using var dir = new TempDir();
        var (svc, _, settings) = TestDevices.Create(dir, new SimulatedPrinter());
        await using var _ = svc;
        var nav = new RecordingNavigation();
        using var vm = Create(svc, settings, new StartupState(false), nav);
        await svc.StartAsync(CancellationToken.None);

        Assert.False(vm.ShowFirstRun);
        var card = Assert.Single(vm.Printers);
        Assert.Equal(StatusTone.Success, card.Status!.Tone);
        Assert.True(card.CanCalibrate);

        vm.CalibrateCommand.Execute(card);
        Assert.Equal(PageKeys.Printers, Assert.Single(nav.Visited));
        Assert.Equal(new PrinterDeepLink(PrinterTab.Calibration, PrinterSection.SmartCal), nav.Parameters[0]);
    }

    [Fact]
    public void No_printers_shows_the_empty_state()
    {
        using var dir = new TempDir();
        var (svc, _, settings) = TestDevices.Create(dir);
        using var vm = Create(svc, settings, new StartupState(false), new RecordingNavigation());
        Assert.True(vm.HasNoPrinters);
    }
}
