using LabelStudio.Core;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Discovery;
using LabelStudio.Devices.Settings;
using LabelStudio.Devices.Simulation;
using LabelStudio.Devices.Transport;
using LabelStudio.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LabelStudio.Devices.Tests;

public class DeviceServiceSettingsTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>Records how many setvars the printer had received when each snapshot was taken.</summary>
    private sealed class RecordingStore(SimulatedPrinter printer, bool fail = false) : IConfigurationSnapshotStore
    {
        public List<(ConfigurationSnapshot Snapshot, int SetVarsSoFar)> Saved { get; } = [];

        public string Save(ConfigurationSnapshot snapshot)
        {
            if (fail) throw new IOException("disk full");
            Saved.Add((snapshot, printer.SetVarRequests.Count));
            return "memory";
        }
    }

    /// <summary>Fails the first write containing <paramref name="trigger"/>, modelling a USB write dying mid-apply.</summary>
    private sealed class FailingTransport(SimulatedPrinter printer, string trigger) : IPrinterTransport
    {
        private readonly SimulatedPrinterTransport _inner = new(printer);

        public Task OpenAsync(CancellationToken ct) => _inner.OpenAsync(ct);

        public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct) =>
            System.Text.Encoding.UTF8.GetString(data.Span).Contains(trigger, StringComparison.Ordinal)
                ? throw new IOException("Simulated USB write failure mid-apply.")
                : _inner.WriteAsync(data, ct);

        public Task<int> ReadAsync(Memory<byte> buffer, TimeSpan timeout, CancellationToken ct) => _inner.ReadAsync(buffer, timeout, ct);
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class FailingTransportFactory(SimulatedPrinter printer, string trigger) : ITransportFactory
    {
        public IPrinterTransport Create(UsbPrinterInfo info) => new FailingTransport(printer, trigger);
    }

    [Fact]
    public async Task Apply_snapshots_first_then_writes_and_marks_keys_pending()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var store = new RecordingStore(printer);
        var (svc, _, _, _) = TestDevices.CreateTimed(dir, store, printer);
        await using var _ = svc;
        await svc.StartAsync(None);

        var result = await svc.ApplySettingsAsync(new Dictionary<string, string> { [SgdKeys.Darkness] = "16" }, None);

        var saved = Assert.Single(store.Saved);
        Assert.Equal(0, saved.SetVarsSoFar);
        Assert.Equal("before-apply", saved.Snapshot.Reason);
        Assert.Equal("20.0", saved.Snapshot.Settings[SgdKeys.Darkness]);
        Assert.Empty(result.Rejected);
        Assert.Equal("16.0", svc.Snapshot.Profile!.Get(SgdKeys.Darkness));
        Assert.Contains(SgdKeys.Darkness, svc.Snapshot.PendingCommitKeys);
    }

    [Fact]
    public async Task Backup_failure_aborts_the_write()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _, _) = TestDevices.CreateTimed(dir, new RecordingStore(printer, fail: true), printer);
        await using var _ = svc;
        await svc.StartAsync(None);

        await Assert.ThrowsAsync<ConfigurationBackupException>(() =>
            svc.ApplySettingsAsync(new Dictionary<string, string> { [SgdKeys.Darkness] = "16" }, None));
        Assert.Empty(printer.SetVarRequests);
        Assert.Equal("20.0", printer.Sgd[SgdKeys.Darkness]);
    }

    [Fact]
    public async Task Commit_snapshots_saves_and_clears_pending_keys()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var store = new RecordingStore(printer);
        var (svc, _, _, _) = TestDevices.CreateTimed(dir, store, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        await svc.ApplySettingsAsync(new Dictionary<string, string> { [SgdKeys.Darkness] = "16" }, None);

        await svc.CommitAsync(None);

        Assert.Equal("before-commit", store.Saved[^1].Snapshot.Reason);
        Assert.Empty(svc.Snapshot.PendingCommitKeys);
        printer.PowerCycle();
        Assert.Equal("16.0", printer.Sgd[SgdKeys.Darkness]);
    }

    /// <summary>Models firmware that drops unsaved values; the ZD220t itself keeps them (Task 1 Q1).</summary>
    [Fact]
    public async Task Uncommitted_change_reverts_after_power_cycle()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter { SetvarPersistsWithoutSave = false };
        var (svc, _, _, _) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        await svc.ApplySettingsAsync(new Dictionary<string, string> { [SgdKeys.Darkness] = "16" }, None);
        printer.PowerCycle();
        await svc.ReconnectAsync(None);
        Assert.Equal("20.0", svc.Snapshot.Profile!.Get(SgdKeys.Darkness));
        Assert.Empty(svc.Snapshot.PendingCommitKeys);
    }

    [Fact]
    public async Task Rejected_value_is_reported()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        printer.ReadOnlyKeys.Add(SgdKeys.Darkness);
        var (svc, _, _, time) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        // A key the firmware never accepts is retried, then reported as refused rather than retried forever.
        var result = await TestDevices.DriveAsync(
            svc.ApplySettingsAsync(new Dictionary<string, string> { [SgdKeys.Darkness] = "16" }, None), time);
        Assert.Equal([SgdKeys.Darkness], result.Rejected);
        Assert.Equal(3, printer.SetVarRequests.Count(k => k == SgdKeys.Darkness));
        Assert.Equal("20.0", svc.Snapshot.Profile!.Get(SgdKeys.Darkness));
        Assert.Empty(svc.Snapshot.PendingCommitKeys);
    }

    /// <summary>
    /// A multi-key apply that fails part-way must still publish what the printer actually holds: an earlier
    /// setvar in the same call already reached the printer and, on this hardware, persists without ^JU S, so
    /// leaving the app showing the pre-apply values would be a lie about the device state.
    /// </summary>
    /// <summary>
    /// Measured on the ZD220t during the M2a investigation: a setvar can silently not take, and the value
    /// reads back unchanged. Reporting that as a refusal would leave the app showing a value the printer is
    /// one retry away from holding, so the write is retried after a settle before it counts as refused.
    /// </summary>
    [Fact]
    public async Task A_setvar_that_does_not_take_is_retried_rather_than_reported_as_refused()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        printer.DropNextSetVarFor.Add(SgdKeys.Darkness);
        var (svc, _, _, time) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);

        var result = await TestDevices.DriveAsync(
            svc.ApplySettingsAsync(new Dictionary<string, string> { [SgdKeys.Darkness] = "16" }, None), time);

        Assert.Empty(result.Rejected);
        Assert.Equal(2, printer.SetVarRequests.Count(k => k == SgdKeys.Darkness));
        Assert.Equal("16.0", printer.Sgd[SgdKeys.Darkness]);
        Assert.Equal("16.0", svc.Snapshot.Profile!.Get(SgdKeys.Darkness));
        Assert.Contains(SgdKeys.Darkness, svc.Snapshot.PendingCommitKeys);
    }

    [Fact]
    public async Task Publishes_what_the_printer_holds_when_a_multikey_apply_fails_partway()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var discovery = new SimulatedDiscovery(printer);
        var settings = new SettingsService(new JsonFileStore<AppSettings>(dir.File("settings.json")));
        await using var svc = new DeviceService(
            discovery, new FailingTransportFactory(printer, "setvar \"ezpl.print_width\""), new CapabilityProber(TimeSpan.FromMilliseconds(50)),
            new ProfileCache(dir.File("profiles")), new ConfigurationSnapshotStore(dir.File("backups")), settings,
            new FakeTimeProvider(), NullLogger<DeviceService>.Instance);
        await svc.StartAsync(None);

        await Assert.ThrowsAsync<IOException>(() =>
            svc.ApplySettingsAsync(new Dictionary<string, string> { [SgdKeys.Darkness] = "16", [SgdKeys.PrintWidth] = "320" }, None));

        // Order-independent: whichever key was written before the failure, the published profile must match
        // what the simulator actually holds for both keys.
        foreach (var key in new[] { SgdKeys.Darkness, SgdKeys.PrintWidth })
            Assert.Equal(printer.Sgd[key], svc.Snapshot.Profile!.Get(key));
        // At least one key must have actually landed, so this can't pass vacuously by writing nothing.
        Assert.True(printer.Sgd[SgdKeys.Darkness] != "20.0" || printer.Sgd[SgdKeys.PrintWidth] != "812");
    }

    [Fact]
    public async Task Unsupported_or_unwritable_keys_are_refused_before_anything_is_sent()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _, _) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => svc.ApplySettingsAsync(new Dictionary<string, string> { [SgdKeys.TearOff] = "0" }, None)); // simulator doesn't answer tear-off
        await Assert.ThrowsAnyAsync<ArgumentException>(() => svc.ApplySettingsAsync(new Dictionary<string, string> { [SgdKeys.PrintSpeed] = "4.0" }, None));
        Assert.Empty(printer.SetVarRequests);
    }

    [Fact]
    public async Task Read_settings_refreshes_the_profile()
    {
        using var dir = new TempDir();
        var printer = new SimulatedPrinter();
        var (svc, _, _, _) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        printer.Sgd[SgdKeys.OdometerUserLabels] = "1250";
        var values = await svc.ReadSettingsAsync([SgdKeys.OdometerUserLabels], None);
        Assert.Equal("1250", values[SgdKeys.OdometerUserLabels]);
        Assert.Equal("1250", svc.Snapshot.Profile!.Get(SgdKeys.OdometerUserLabels));
    }

    [Fact]
    public async Task Pause_records_when_the_app_paused_the_printer()
    {
        using var dir = new TempDir();
        var (svc, _, _, time) = TestDevices.CreateTimed(dir, new SimulatedPrinter());
        await using var _ = svc;
        await svc.StartAsync(None);
        await svc.PauseAsync(None);
        Assert.Equal(time.GetUtcNow(), svc.LastAppPauseAt);
    }
}
