using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Settings;
using LabelStudio.Devices.Simulation;
using LabelStudio.Tests;

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
        var (svc, _, _, _) = TestDevices.CreateTimed(dir, printer);
        await using var _ = svc;
        await svc.StartAsync(None);
        var result = await svc.ApplySettingsAsync(new Dictionary<string, string> { [SgdKeys.Darkness] = "16" }, None);
        Assert.Equal([SgdKeys.Darkness], result.Rejected);
        Assert.Equal("20.0", svc.Snapshot.Profile!.Get(SgdKeys.Darkness));
        Assert.Empty(svc.Snapshot.PendingCommitKeys);
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
