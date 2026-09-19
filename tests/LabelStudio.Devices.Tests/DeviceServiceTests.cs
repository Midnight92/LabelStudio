using System.Diagnostics;
using System.Reflection;
using LabelStudio.Core;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Simulation;
using LabelStudio.Devices.Status;
using LabelStudio.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

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

    // --- Fix round 1 (review findings 1-4) ---

    [Fact]
    public async Task Cancelling_a_connect_disposes_the_transport_and_allows_a_later_reconnect()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        printer.SilentKeys.Add(SgdKeys.ApplName); // never answers -> the probe loop keeps awaiting until cancelled or timed out
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;

        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.ConnectAsync(printer.Info, cts.Token));
        }
        Assert.NotEqual(ConnectionState.Connecting, svc.Snapshot.Connection);

        // A later connect must succeed: the cancelled attempt must not have left the transport (or the service) in a bad state.
        await svc.ConnectAsync(printer.Info, None);
        Assert.Equal(ConnectionState.Connected, svc.Snapshot.Connection);
    }

    [Fact]
    public async Task Dispose_waits_for_an_in_flight_connect_before_closing_the_session()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        printer.SilentKeys.Add(SgdKeys.ApplName); // makes the (re)probe take ~50 ms of real wall-clock time
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await svc.StartAsync(None); // ApplName gets cached as unresponsive

        var reprobeTask = svc.ReprobeAsync(None); // clears the cache, so ApplName is queried for real (~50 ms) again
        var sw = Stopwatch.StartNew();
        await svc.DisposeAsync();
        sw.Stop();

        var reprobeException = await Record.ExceptionAsync(() => reprobeTask);
        Assert.Null(reprobeException);
        Assert.True(sw.ElapsedMilliseconds >= 30,
            $"DisposeAsync returned after {sw.ElapsedMilliseconds} ms; it must wait for the in-flight reprobe " +
            "(and its session) before closing the session, not race it.");
    }

    [Fact]
    public async Task Devices_changed_handler_does_not_throw_after_dispose()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await svc.StartAsync(None);
        await svc.DisposeAsync();

        // The event is unsubscribed by DisposeAsync, so exercise the handler directly to prove the
        // (private) fire-and-forget path used to reach a disposed CancellationTokenSource is now guarded.
        var method = typeof(DeviceService).GetMethod("HandleDevicesChangedAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task)method.Invoke(svc, null)!;
        var ex = await Record.ExceptionAsync(() => task);
        Assert.Null(ex);
    }

    [Fact]
    public async Task Background_reconnect_attempts_do_not_flicker_through_connecting()
    {
        using var dir = new TempDir();
        var (svc, _, _) = TestDevices.Create(dir, new SimulatedPrinter { Claimed = true });
        await using var _ = svc;
        await svc.StartAsync(None);

        var seen = new List<ConnectionState>();
        svc.SnapshotChanged += (_, snap) => seen.Add(snap.Connection);

        await svc.RefreshAsync(None);
        await svc.RefreshAsync(None);

        Assert.DoesNotContain(ConnectionState.Connecting, seen);
    }

    // --- Fix round 2 (final whole-branch review items 2, 3) ---

    [Fact]
    public async Task Connects_even_when_the_profile_cache_directory_cannot_be_created()
    {
        using var dir = new TempDir();
        var badProfilesPath = dir.File("profiles");
        File.WriteAllText(badProfilesPath, "occupies the path a directory would need"); // a FILE, not a directory
        var printer = new SimulatedPrinter();
        var discovery = new SimulatedDiscovery(printer);
        var settings = new SettingsService(new JsonFileStore<AppSettings>(dir.File("settings.json")));
        var svc = new DeviceService(
            discovery, new SimulatedTransportFactory(discovery), new CapabilityProber(TimeSpan.FromMilliseconds(50)),
            new ProfileCache(badProfilesPath), settings, new FakeTimeProvider(), NullLogger<DeviceService>.Instance);
        await using var _ = svc;

        await svc.StartAsync(None);

        Assert.Equal(ConnectionState.Connected, svc.Snapshot.Connection);
    }

    [Fact]
    public async Task Connects_even_when_the_settings_save_fails()
    {
        using var dir = new TempDir();
        var badSettingsParent = dir.File("settingsdir");
        File.WriteAllText(badSettingsParent, "occupies the path a directory would need"); // a FILE, not a directory
        var settingsPath = Path.Combine(badSettingsParent, "settings.json"); // its parent can never be created
        var printer = new SimulatedPrinter();
        var discovery = new SimulatedDiscovery(printer);
        var settings = new SettingsService(new JsonFileStore<AppSettings>(settingsPath));
        var svc = new DeviceService(
            discovery, new SimulatedTransportFactory(discovery), new CapabilityProber(TimeSpan.FromMilliseconds(50)),
            new ProfileCache(dir.File("profiles")), settings, new FakeTimeProvider(), NullLogger<DeviceService>.Instance);
        await using var _ = svc;

        await svc.StartAsync(None);

        Assert.Equal(ConnectionState.Connected, svc.Snapshot.Connection);
    }

    [Fact]
    public async Task All_unresponsive_probe_is_not_cached_and_is_retried_on_the_next_connect()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        foreach (var key in SgdKeys.ProbeList) printer.SilentKeys.Add(key); // nothing responds -> profile.Settings is empty
        var (svc, _, _) = TestDevices.Create(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        var firstAttempts = printer.GetVarRequests.Count;
        Assert.True(firstAttempts > 0);

        await svc.ConnectAsync(printer.Info, None);

        Assert.True(printer.GetVarRequests.Count > firstAttempts,
            $"Expected the second connect to re-query every key instead of trusting a cached all-unresponsive result " +
            $"(first attempt count: {firstAttempts}, second: {printer.GetVarRequests.Count}).");
    }
}
