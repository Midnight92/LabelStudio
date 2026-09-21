using LabelStudio.Devices.Calibration;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Simulation;
using LabelStudio.Tests;

namespace LabelStudio.Devices.Tests;

public class DeviceServiceCalibrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task Calibrate_marks_activity_and_updates_the_profile()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter { CalibrationOutcome = new(FindsGap: true, LengthDots: 209) };
        var (svc, _, _, time) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        var activities = new List<DeviceActivity>();
        svc.SnapshotChanged += (_, s) => activities.Add(s.Activity);

        var result = await TestDevices.DriveAsync(svc.CalibrateAsync(null, None), time);

        Assert.True(result.Succeeded);
        Assert.Contains(DeviceActivity.Calibrating, activities);
        Assert.Equal(DeviceActivity.None, svc.Snapshot.Activity);
        Assert.Equal("209", svc.Snapshot.Profile!.Get(SgdKeys.LabelLength));
    }

    [Fact]
    public async Task Media_reload_raises_and_calibration_clears_the_suggestion()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _, time) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);

        printer.PaperOut = true;
        await svc.RefreshAsync(None);
        printer.PaperOut = false;
        await svc.RefreshAsync(None);
        Assert.True(svc.Snapshot.CalibrationSuggested);

        await TestDevices.DriveAsync(svc.CalibrateAsync(null, None), time);
        Assert.False(svc.Snapshot.CalibrationSuggested);
    }

    [Fact]
    public async Task Suggestion_can_be_dismissed()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _, _) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        printer.PaperOut = true;
        await svc.RefreshAsync(None);
        printer.PaperOut = false;
        await svc.RefreshAsync(None);

        await svc.DismissCalibrationSuggestionAsync(None);

        Assert.False(svc.Snapshot.CalibrationSuggested);
        await svc.RefreshAsync(None);
        Assert.False(svc.Snapshot.CalibrationSuggested);
    }
}
