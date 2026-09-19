using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Simulation;
using LabelStudio.Devices.Status;
using LabelStudio.Tests;

namespace LabelStudio.Devices.Tests;

public class DeviceServiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task Single_printer_auto_connects_and_reports_ready()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, settings) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        Assert.Equal(ConnectionState.Connected, svc.Snapshot.Connection);
        Assert.Equal(PrinterState.Ready, svc.Snapshot.State);
        Assert.Equal("ZD220t", svc.Snapshot.Profile!.VariantName);
        Assert.Equal(printer.Serial, settings.Current.LastPrinterSerial);
    }

    [Fact]
    public async Task Poll_reports_media_out()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        printer.PaperOut = true;
        await svc.RefreshAsync(None);
        Assert.Equal(PrinterState.MediaOut, svc.Snapshot.State);
    }

    [Fact]
    public async Task Three_failed_polls_mark_disconnected()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        printer.Unplugged = true; // no DevicesChanged event: the printer just stops answering
        await svc.RefreshAsync(None);
        await svc.RefreshAsync(None);
        Assert.Equal(ConnectionState.Connected, svc.Snapshot.Connection);
        await svc.RefreshAsync(None);
        Assert.Equal(ConnectionState.Disconnected, svc.Snapshot.Connection);
        Assert.Equal(DeviceProblem.NotResponding, svc.Snapshot.Problem);
    }

    [Fact]
    public async Task Unplug_event_disconnects_and_replug_reconnects()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, discovery, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);

        printer.Unplugged = true;
        discovery.RaiseDevicesChanged();
        await TestDevices.WaitUntilAsync(() => svc.Snapshot.Problem == DeviceProblem.Unplugged);

        printer.Unplugged = false;
        discovery.RaiseDevicesChanged();
        await TestDevices.WaitUntilAsync(() => svc.Snapshot.Connection == ConnectionState.Connected);
    }

    [Fact]
    public async Task Remembered_printer_wins_when_several_present()
    {
        using var dir = new TempDir();
        var a = new SimulatedPrinter { Serial = "AAA" };
        var b = new SimulatedPrinter { Serial = "BBB" };
        var (svc, _, settings) = TestDevices.Create(dir, a, b);
        await using var _ = svc;
        settings.Update(s => s with { LastPrinterSerial = "BBB" });
        await svc.StartAsync(None);
        Assert.Equal("BBB", svc.Snapshot.Printer!.Serial);
    }

    [Fact]
    public async Task Several_printers_and_no_memory_waits_for_a_choice()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir, new SimulatedPrinter { Serial = "AAA" }, new SimulatedPrinter { Serial = "BBB" });
        await using var _ = svc;
        await svc.StartAsync(None);
        Assert.Equal(ConnectionState.NoPrinter, svc.Snapshot.Connection);
        Assert.Equal(2, svc.Printers.Count);
    }

    [Fact]
    public async Task Claimed_device_is_reported()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir, new SimulatedPrinter { Claimed = true });
        await using var _ = svc;
        await svc.StartAsync(None);
        Assert.Equal(ConnectionState.Disconnected, svc.Snapshot.Connection);
        Assert.Equal(DeviceProblem.Claimed, svc.Snapshot.Problem);
    }

    [Fact]
    public async Task Test_label_is_sent()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        await svc.PrintTestLabelAsync(None);
        Assert.Contains(printer.Serial, Assert.Single(printer.ReceivedJobs));
    }

    [Fact]
    public async Task Pause_and_resume_round_trip()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir, new SimulatedPrinter());
        await using var _ = svc;
        await svc.StartAsync(None);
        await svc.PauseAsync(None);
        Assert.Equal(PrinterState.Paused, svc.Snapshot.State);
        await svc.ResumeAsync(None);
        Assert.Equal(PrinterState.Ready, svc.Snapshot.State);
    }

    [Fact]
    public async Task Unresponsive_keys_are_cached_until_reprobe()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        printer.SilentKeys.Add(SgdKeys.PowerUpAction);
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        await svc.ConnectAsync(printer.Info, None);
        Assert.Equal(1, printer.GetVarRequests.Count(k => k == SgdKeys.PowerUpAction));
        await svc.ReprobeAsync(None);
        Assert.Equal(2, printer.GetVarRequests.Count(k => k == SgdKeys.PowerUpAction));
    }
}
