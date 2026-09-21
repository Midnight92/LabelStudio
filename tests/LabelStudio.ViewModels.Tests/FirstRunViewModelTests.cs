using LabelStudio.Core;
using LabelStudio.Devices;
using LabelStudio.Devices.Simulation;
using LabelStudio.Tests;
using LabelStudio.ViewModels.Home;
using LabelStudio.ViewModels.Printers;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelStudio.ViewModels.Tests;

public class FirstRunViewModelTests
{
    private static FirstRunViewModel Create(DeviceService svc, ISettingsService settings, RecordingNavigation? nav = null)
    {
        var ui = new ImmediateDispatcher();
        return new FirstRunViewModel(svc, ui, nav ?? new RecordingNavigation(), settings,
            new CalibrationViewModel(svc, ui, NullLogger<CalibrationViewModel>.Instance),
            new CompatibilityCheckerViewModel(svc, ui), NullLogger<FirstRunViewModel>.Instance);
    }

    [Fact]
    public async Task Walks_find_media_calibrate_test_print_and_completes()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, settings, time) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        using var vm = Create(svc, settings);
        var completed = false;
        vm.Completed += (_, _) => completed = true;

        Assert.Equal(FirstRunStep.FindPrinter, vm.Step);
        Assert.False(vm.NextCommand.CanExecute(null));
        await svc.StartAsync(CancellationToken.None);
        Assert.True(vm.NextCommand.CanExecute(null));

        vm.NextCommand.Execute(null);
        Assert.True(vm.IsMediaStep);
        Assert.NotEmpty(vm.MediaRows);

        vm.NextCommand.Execute(null);
        Assert.True(vm.IsCalibrateStep);
        Assert.False(vm.NextCommand.CanExecute(null));
        await TestDevices.DriveAsync(vm.Calibration.RunSmartCalCommand.ExecuteAsync(null).ContinueWith(_ => true), time);
        Assert.True(vm.NextCommand.CanExecute(null));

        vm.NextCommand.Execute(null);
        Assert.True(vm.IsTestPrintStep);
        await vm.PrintTestLabelCommand.ExecuteAsync(null);
        Assert.True(vm.HasTestPrinted);
        Assert.Single(printer.ReceivedJobs);

        vm.PrintedCorrectlyCommand.Execute(null);
        Assert.True(completed);
        Assert.True(settings.Current.FirstRunCompleted);
    }

    [Fact]
    public async Task Printed_wrong_opens_the_compatibility_check()
    {
        using var dir = new TempDir();
        var (svc, _, settings) = TestDevices.Create(dir, new SimulatedPrinter());
        await using var _ = svc;
        await svc.StartAsync(CancellationToken.None);
        using var vm = Create(svc, settings);
        vm.PrintedWrongCommand.Execute(null);
        Assert.True(vm.ShowChecker);
        Assert.False(settings.Current.FirstRunCompleted);
    }

    [Fact]
    public async Task Back_returns_to_the_previous_step_and_skip_completes()
    {
        using var dir = new TempDir();
        var (svc, _, settings) = TestDevices.Create(dir, new SimulatedPrinter());
        await using var _ = svc;
        await svc.StartAsync(CancellationToken.None);
        using var vm = Create(svc, settings);
        vm.NextCommand.Execute(null);
        vm.BackCommand.Execute(null);
        Assert.Equal(FirstRunStep.FindPrinter, vm.Step);
        Assert.False(vm.BackCommand.CanExecute(null));

        vm.SkipSetupCommand.Execute(null);
        Assert.True(settings.Current.FirstRunCompleted);
    }

    [Fact]
    public async Task Length_only_request_goes_to_the_printers_page()
    {
        using var dir = new TempDir();
        var (svc, _, settings) = TestDevices.Create(dir, new SimulatedPrinter());
        await using var _ = svc;
        await svc.StartAsync(CancellationToken.None);
        var nav = new RecordingNavigation();
        using var vm = Create(svc, settings, nav);
        vm.Checker.UseLengthOnlyCommand.Execute(null);
        Assert.Equal(PageKeys.Printers, Assert.Single(nav.Visited));
        Assert.Equal(new PrinterDeepLink(PrinterTab.Calibration, PrinterSection.LengthOnly), nav.Parameters[0]);
    }
}
