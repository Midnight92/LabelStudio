using LabelStudio.Devices.Simulation;
using LabelStudio.Tests;
using LabelStudio.ViewModels.Notifications;

namespace LabelStudio.ViewModels.Tests;

public class FaultNotifierTests
{
    private sealed class RecordingNotifications : INotificationService
    {
        public List<FaultToast> Shown { get; } = [];
        public void Show(FaultToast toast) => Shown.Add(toast);
    }

    private sealed class Activity : IAppActivityState
    {
        public bool IsForeground { get; set; }
    }

    [Fact]
    public async Task Media_out_toasts_once_while_in_the_background()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _, time) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        var shown = new RecordingNotifications();
        using var notifier = new FaultNotifier(svc, shown, new Activity(), time);
        await svc.StartAsync(CancellationToken.None);

        printer.PaperOut = true;
        await svc.RefreshAsync(CancellationToken.None);
        await svc.RefreshAsync(CancellationToken.None);

        var toast = Assert.Single(shown.Shown);
        Assert.Equal("Media out", toast.Title);
        Assert.Equal(new PrinterDeepLink(PrinterTab.Overview, PrinterSection.StatusRemedy), toast.Link);
    }

    [Fact]
    public async Task Ready_and_reconnects_never_toast()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, discovery, _, time) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        var shown = new RecordingNotifications();
        using var notifier = new FaultNotifier(svc, shown, new Activity(), time);
        await svc.StartAsync(CancellationToken.None);
        printer.Unplugged = true;
        discovery.RaiseDevicesChanged();
        await TestDevices.WaitUntilAsync(() => svc.Snapshot.Problem == Devices.DeviceProblem.Unplugged);
        printer.Unplugged = false;
        discovery.RaiseDevicesChanged();
        await TestDevices.WaitUntilAsync(() => svc.Snapshot.Connection == Devices.ConnectionState.Connected);
        Assert.Empty(shown.Shown);
    }

    [Fact]
    public async Task Foreground_window_suppresses_toasts()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _, time) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        var shown = new RecordingNotifications();
        using var notifier = new FaultNotifier(svc, shown, new Activity { IsForeground = true }, time);
        await svc.StartAsync(CancellationToken.None);
        printer.HeadUp = true;
        await svc.RefreshAsync(CancellationToken.None);
        Assert.Empty(shown.Shown);
    }

    [Fact]
    public async Task A_pause_the_app_sent_does_not_toast_but_a_later_one_does()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _, time) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        var shown = new RecordingNotifications();
        using var notifier = new FaultNotifier(svc, shown, new Activity(), time);
        await svc.StartAsync(CancellationToken.None);

        await svc.PauseAsync(CancellationToken.None);
        Assert.Empty(shown.Shown);

        await svc.ResumeAsync(CancellationToken.None);
        time.Advance(FaultNotifier.AppPauseGrace);
        printer.Paused = true; // someone pressed the printer's button
        await svc.RefreshAsync(CancellationToken.None);
        Assert.Single(shown.Shown);
    }

    [Fact]
    public async Task Faults_during_calibration_do_not_toast()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter { CalibrationOutcome = new(FindsGap: false, LengthDots: 0) };
        var (svc, _, _, time) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        var shown = new RecordingNotifications();
        using var notifier = new FaultNotifier(svc, shown, new Activity(), time);
        await svc.StartAsync(CancellationToken.None);
        await TestDevices.DriveAsync(svc.CalibrateAsync(null, CancellationToken.None), time);
        Assert.Empty(shown.Shown);
    }
}
