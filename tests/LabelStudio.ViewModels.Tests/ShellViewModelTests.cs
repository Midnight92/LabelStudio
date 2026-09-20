using LabelStudio.Devices;
using LabelStudio.Devices.Simulation;
using LabelStudio.Tests;
using LabelStudio.ViewModels.Status;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelStudio.ViewModels.Tests;

public class ShellViewModelTests
{
    [Fact]
    public async Task Pill_tracks_device_and_navigates_when_connected()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir, new SimulatedPrinter());
        await using var _ = svc;
        var nav = new RecordingNavigation();
        var shell = new ShellViewModel(svc, new ImmediateDispatcher(), nav, NullLogger<ShellViewModel>.Instance);
        await svc.StartAsync(CancellationToken.None);
        Assert.Equal(StatusTone.Success, shell.Status.Tone);
        await shell.PillActionCommand.ExecuteAsync(null);
        Assert.Equal([PageKeys.Printers], nav.Visited);
    }

    [Fact]
    public async Task Pill_reconnects_when_disconnected()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter { Claimed = true };
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        var shell = new ShellViewModel(svc, new ImmediateDispatcher(), new RecordingNavigation(), NullLogger<ShellViewModel>.Instance);
        await svc.StartAsync(CancellationToken.None);
        Assert.Equal(StatusAction.Reconnect, shell.Status.Action);
        printer.Claimed = false;
        await shell.PillActionCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionState.Connected, svc.Snapshot.Connection);
    }

    [Fact]
    public async Task Refresh_after_the_device_service_is_disposed_does_not_throw_and_sets_CommandError()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir, new SimulatedPrinter());
        var shell = new ShellViewModel(svc, new ImmediateDispatcher(), new RecordingNavigation(), NullLogger<ShellViewModel>.Instance);
        await svc.StartAsync(CancellationToken.None);
        await svc.DisposeAsync(); // RefreshCommand will now hit ObjectDisposedException inside DeviceService

        var ex = await Record.ExceptionAsync(() => shell.RefreshCommand.ExecuteAsync(null));

        Assert.Null(ex);
        Assert.NotNull(shell.CommandError);
    }
}
