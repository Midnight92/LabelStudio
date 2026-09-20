using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Simulation;
using LabelStudio.Tests;
using LabelStudio.ViewModels.Printers;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelStudio.ViewModels.Tests;

public class CalibrationViewModelTests
{
    [Fact]
    public async Task Ready_printer_can_calibrate_and_states_the_feed_count()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir, new SimulatedPrinter());
        await using var _ = svc;
        await svc.StartAsync(CancellationToken.None);
        using var vm = new CalibrationViewModel(svc, new ImmediateDispatcher(), NullLogger<CalibrationViewModel>.Instance);
        Assert.True(vm.CanCalibrate);
        Assert.Contains("2", vm.FeedNotice);
        Assert.All(vm.Checklist, r => Assert.True(r.Ok));
    }

    [Fact]
    public async Task Open_cover_blocks_calibration_and_shows_in_the_checklist()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter { HeadUp = true };
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(CancellationToken.None);
        using var vm = new CalibrationViewModel(svc, new ImmediateDispatcher(), NullLogger<CalibrationViewModel>.Instance);
        Assert.False(vm.CanCalibrate);
        Assert.Contains(vm.Checklist, r => !r.Ok);
        Assert.False(vm.RunSmartCalCommand.CanExecute(null));
    }

    [Fact]
    public async Task Successful_run_shows_what_was_detected()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter { CalibrationOutcome = new(FindsGap: true, LengthDots: 209) };
        var (svc, _, _, time) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(CancellationToken.None);
        using var vm = new CalibrationViewModel(svc, new ImmediateDispatcher(), NullLogger<CalibrationViewModel>.Instance);

        await TestDevices.DriveAsync(vm.RunSmartCalCommand.ExecuteAsync(null).ContinueWith(_ => true), time);

        Assert.Equal(CalibrationUiState.Succeeded, vm.State);
        Assert.Contains(vm.DetectedRows, r => r.Value.StartsWith("209", StringComparison.Ordinal));
        Assert.False(vm.MeasuredNoChange);
    }

    [Fact]
    public async Task Same_measured_length_is_reported_as_no_change()
    {
        using var dir = new TempDir();
        // Default SimulatedPrinter: CalibrationOutcome.LengthDots (1218) matches the starting zpl.label_length
        // (1218), so the runner never sees a change and only concludes at the model's calibration timeout.
        var printer = new SimulatedPrinter();
        var (svc, _, _, time) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(CancellationToken.None);
        using var vm = new CalibrationViewModel(svc, new ImmediateDispatcher(), NullLogger<CalibrationViewModel>.Instance);

        await TestDevices.DriveAsync(vm.RunSmartCalCommand.ExecuteAsync(null).ContinueWith(_ => true), time);

        Assert.Equal(CalibrationUiState.Succeeded, vm.State);
        Assert.True(vm.MeasuredNoChange);
    }

    [Fact]
    public async Task No_gap_failure_offers_the_compatibility_check()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter { CalibrationOutcome = new(FindsGap: false, LengthDots: 0) };
        var (svc, _, _, time) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(CancellationToken.None);
        using var vm = new CalibrationViewModel(svc, new ImmediateDispatcher(), NullLogger<CalibrationViewModel>.Instance);
        var requested = false;
        vm.CompatibilityCheckRequested += (_, _) => requested = true;

        await TestDevices.DriveAsync(vm.RunSmartCalCommand.ExecuteAsync(null).ContinueWith(_ => true), time);

        Assert.Equal(CalibrationUiState.Failed, vm.State);
        Assert.True(vm.ShowCompatibilityAction);
        Assert.False(string.IsNullOrEmpty(vm.FailureBody));
        vm.CheckCompatibilityCommand.Execute(null);
        Assert.True(requested);
    }

    [Fact]
    public async Task Suggestion_mirrors_the_device_and_can_be_dismissed()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(CancellationToken.None);
        using var vm = new CalibrationViewModel(svc, new ImmediateDispatcher(), NullLogger<CalibrationViewModel>.Instance);
        printer.PaperOut = true;
        await svc.RefreshAsync(CancellationToken.None);
        printer.PaperOut = false;
        await svc.RefreshAsync(CancellationToken.None);
        Assert.True(vm.SuggestCalibration);

        await vm.DismissSuggestionCommand.ExecuteAsync(null);
        Assert.False(vm.SuggestCalibration);
    }
}
