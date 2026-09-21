using LabelStudio.Core;
using LabelStudio.Devices.Calibration;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Commands;
using LabelStudio.Devices.Discovery;
using LabelStudio.Devices.Models;
using LabelStudio.Devices.Settings;
using LabelStudio.Devices.Status;
using LabelStudio.Devices.Transport;
using Microsoft.Extensions.Logging;

namespace LabelStudio.Devices;

/// <summary>
/// App-facing device layer: discovery, auto-connect to the remembered printer, hot-plug,
/// capability probing and status polling. Every state change runs under one exclusive gate.
/// </summary>
public sealed class DeviceService : IAsyncDisposable
{
    /// <summary>How long to let a setvar settle before reading it back again after a mismatch.</summary>
    private static readonly TimeSpan SetvarSettle = TimeSpan.FromMilliseconds(300);
    private const int SetvarAttempts = 3;

    private readonly IPrinterDiscovery _discovery;
    private readonly ITransportFactory _transports;
    private readonly CapabilityProber _prober;
    private readonly IProfileCache _profiles;
    private readonly IConfigurationSnapshotStore _snapshots;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<DeviceService> _log;
    private readonly CalibrationRunner _calibration;
    private readonly MediaChangeDetector _mediaChanges;
    private readonly SemaphoreSlim _ops = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private PrinterSession? _session;
    private UsbPrinterInfo? _target;
    private int _failures;
    private Task? _loop;
    private volatile bool _disposed;

    public DeviceService(IPrinterDiscovery discovery, ITransportFactory transports, CapabilityProber prober, IProfileCache profiles,
        IConfigurationSnapshotStore snapshots, ISettingsService settings, TimeProvider time, ILogger<DeviceService> log)
    {
        (_discovery, _transports, _prober, _profiles, _snapshots, _settings, _time, _log) =
            (discovery, transports, prober, profiles, snapshots, settings, time, log);
        _calibration = new CalibrationRunner(time);
        _mediaChanges = new MediaChangeDetector(time);
    }

    public DeviceSnapshot Snapshot { get; private set; } = DeviceSnapshot.Initial;
    public IReadOnlyList<UsbPrinterInfo> Printers { get; private set; } = [];
    /// <summary>Set by the job queue (M4) to switch polling to 500 ms.</summary>
    public bool JobActive { get; set; }

    public event EventHandler<DeviceSnapshot>? SnapshotChanged;
    public event EventHandler? PrintersChanged;

    public async Task StartAsync(CancellationToken ct)
    {
        await RunExclusiveAsync(async () =>
        {
            await RefreshPrintersAsync(ct);
            if (ChooseAutoTarget() is { } target) await ConnectCoreAsync(target, ct);
        }, ct);
        _discovery.DevicesChanged += OnDevicesChanged;
        _discovery.StartWatching();
        _loop = Task.Run(() => PollLoopAsync(_lifetime.Token));
    }

    public Task ConnectAsync(UsbPrinterInfo printer, CancellationToken ct) => RunExclusiveAsync(() => ConnectCoreAsync(printer, ct), ct);

    public Task ReconnectAsync(CancellationToken ct) => RunExclusiveAsync(async () =>
    {
        await RefreshPrintersAsync(ct);
        var target = Printers.FirstOrDefault(p => p.Serial == _target?.Serial) ?? ChooseAutoTarget();
        if (target is not null) await ConnectCoreAsync(target, ct);
        else Publish(DeviceSnapshot.Initial with { Problem = DeviceProblem.NotFound });
    }, ct);

    public Task ReprobeAsync(CancellationToken ct) => RunExclusiveAsync(async () =>
    {
        if (_target is null) return;
        _profiles.Clear(_target.Serial);
        await ConnectCoreAsync(_target, ct);
    }, ct);

    public Task RefreshAsync(CancellationToken ct) => RunExclusiveAsync(() => PollCoreAsync(ct), ct);

    public Task SendRawAsync(string commands, CancellationToken ct) => RunExclusiveAsync(() => SendCoreAsync(commands, ct), ct);

    public Task PrintTestLabelAsync(CancellationToken ct) => RunExclusiveAsync(() =>
        SendCoreAsync(TestLabel.Build(Snapshot.Profile ?? throw new InvalidOperationException("No printer connected."), _time.GetLocalNow()), ct), ct);

    public Task FeedAsync(CancellationToken ct) => SendRawAsync(ZplCommands.Feed, ct);

    public Task PauseAsync(CancellationToken ct)
    {
        LastAppPauseAt = _time.GetUtcNow();
        return SendThenPollAsync(ZplCommands.Pause, ct);
    }

    public Task ResumeAsync(CancellationToken ct) => SendThenPollAsync(ZplCommands.Resume, ct);

    public ModelTraits Traits => ModelCatalog.For(Snapshot.Profile);

    /// <summary>When the app last sent ~PP itself — the notifier must not toast a pause the user just asked for.</summary>
    public DateTimeOffset? LastAppPauseAt { get; private set; }

    /// <summary>
    /// Writes media settings: validate everything, snapshot the current configuration, write, read each key back.
    /// Nothing is sent if validation or the snapshot fails. Accepted keys stay pending until <see cref="CommitAsync"/>.
    /// </summary>
    public Task<ApplyResult> ApplySettingsAsync(IReadOnlyDictionary<string, string> changes, CancellationToken ct) => RunExclusiveAsync(async () =>
    {
        var (session, profile) = RequireConnected();
        var traits = ModelCatalog.For(profile);
        var writes = changes.Select(c =>
        {
            if (!profile.Supports(c.Key)) throw new ArgumentException($"The printer did not answer '{c.Key}' when probed.", nameof(changes));
            return (c.Key, Value: MediaSettingWriter.Normalise(c.Key, c.Value, traits));
        }).ToList();

        await SaveSnapshotAsync(session, profile, "before-apply", ct);
        var readBack = new Dictionary<string, string?>(StringComparer.Ordinal);
        var rejected = new List<string>();
        var settings = new Dictionary<string, string>(profile.Settings, StringComparer.Ordinal);
        var pending = new HashSet<string>(Snapshot.PendingCommitKeys, StringComparer.Ordinal);
        try
        {
            foreach (var (key, value) in writes)
            {
                // Read each key back as it is written, not in a second pass. If a later write fails, the
                // finally below still publishes what the printer actually holds — these values persist
                // without ^JU S, so leaving the app showing the old ones would be a lie about the device.
                var actual = await WriteVerifiedAsync(session, traits, key, value, ct);
                readBack[key] = actual;
                if (actual is not null) settings[key] = actual;
                if (MediaSettingWriter.Matches(value, actual)) pending.Add(key);
                else rejected.Add(key);
            }
        }
        finally
        {
            Publish(Snapshot with { Profile = profile with { Settings = settings }, PendingCommitKeys = pending });
        }
        return new ApplyResult(readBack, rejected);
    }, ct);

    /// <summary>Saves the current settings to non-volatile memory with ^JU S, after a snapshot.</summary>
    public Task CommitAsync(CancellationToken ct) => RunExclusiveAsync(async () =>
    {
        var (session, profile) = RequireConnected();
        await SaveSnapshotAsync(session, profile, "before-commit", ct);
        await session.SendRawAsync(ZplCommands.SaveSettings, ct);
        Publish(Snapshot with { PendingCommitKeys = new HashSet<string>() });
    }, ct);

    /// <summary>Re-reads keys the printer already answered (counters, calibration results) into the profile.</summary>
    public Task<IReadOnlyDictionary<string, string>> ReadSettingsAsync(IEnumerable<string> keys, CancellationToken ct) => RunExclusiveAsync(async () =>
    {
        var (session, profile) = RequireConnected();
        var values = await ReadSupportedAsync(session, profile, keys, ct);
        PublishSettings(profile, values);
        return (IReadOnlyDictionary<string, string>)values;
    }, ct);

    private static async Task<Dictionary<string, string>> ReadSupportedAsync(PrinterSession session, CapabilityProfile profile, IEnumerable<string> keys, CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys.Where(profile.Supports).ToList())
            if (await session.GetSgdAsync(key, SgdKeys.ProbeTimeout, ct) is { } v) values[key] = v;
        return values;
    }

    private void PublishSettings(CapabilityProfile profile, IReadOnlyDictionary<string, string> values)
    {
        var settings = new Dictionary<string, string>(profile.Settings, StringComparer.Ordinal);
        foreach (var (k, v) in values) settings[k] = v;
        Publish(Snapshot with { Profile = profile with { Settings = settings } });
    }

    /// <summary>
    /// Writes one setting and returns what the printer actually holds afterwards.
    /// A setvar can silently not take — measured during the M2a hardware investigation, where a restore read
    /// back as the old value — so a mismatch is retried after a settle rather than reported as a refusal.
    /// The first read-back is immediate, so the common case costs nothing.
    /// </summary>
    private async Task<string?> WriteVerifiedAsync(PrinterSession session, ModelTraits traits, string key, string value, CancellationToken ct)
    {
        string? actual = null;
        for (var attempt = 1; attempt <= SetvarAttempts; attempt++)
        {
            if (attempt > 1) await Task.Delay(SetvarSettle, _time, ct);
            if (traits.WritableKeys[key] == WriteStrategy.Sgd) await session.SetSgdAsync(key, value, ct);
            else await session.SendRawAsync(MediaSettingWriter.ToZpl(key, value), ct);
            actual = await session.GetSgdAsync(key, SgdKeys.ProbeTimeout, ct);
            if (MediaSettingWriter.Matches(value, actual)) return actual;
            _log.LogWarning("Setting {Key} to {Value} read back as {Actual} (attempt {Attempt} of {Attempts})",
                key, value, actual, attempt, SetvarAttempts);
        }
        return actual;
    }

    private async Task SaveSnapshotAsync(PrinterSession session, CapabilityProfile profile, string reason, CancellationToken ct)
    {
        var values = await ReadSupportedAsync(session, profile, profile.Settings.Keys, ct);
        try
        {
            var path = _snapshots.Save(new ConfigurationSnapshot(profile.Serial, profile.Model, profile.Firmware, _time.GetUtcNow(), reason, values));
            _log.LogInformation("Configuration snapshot ({Reason}) written to {Path}", reason, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationBackupException("Couldn't save a configuration backup, so no settings were changed.", ex);
        }
    }

    private (PrinterSession Session, CapabilityProfile Profile) RequireConnected() =>
        (_session ?? throw new InvalidOperationException("No printer connected."),
         Snapshot.Profile ?? throw new InvalidOperationException("No printer connected."));

    private async Task<T> RunExclusiveAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        T result = default!;
        // Explicitly typed so overload resolution can't match the generic overload against itself
        // (an inferred `async () => result = await action()` is ambiguous between Func<Task> and
        // Func<Task<T>> and previously resolved to this same generic overload, recursing forever).
        Func<Task> body = async () => result = await action();
        await RunExclusiveAsync(body, ct);
        return result;
    }

    private Task SendThenPollAsync(string command, CancellationToken ct) => RunExclusiveAsync(async () =>
    {
        await SendCoreAsync(command, ct);
        await PollCoreAsync(ct);
    }, ct);

    private Task SendCoreAsync(string commands, CancellationToken ct) =>
        (_session ?? throw new InvalidOperationException("No printer connected.")).SendRawAsync(commands, ct);

    private IReadOnlySet<string> GetUnresponsiveKeysBestEffort(string serial, string firmware)
    {
        try
        {
            return _profiles.GetUnresponsiveKeys(serial, firmware);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Could not read the cached unresponsive-key list for {Serial}; probing every key", serial);
            return new HashSet<string>();
        }
    }

    private void SaveUnresponsiveKeysBestEffort(string serial, string firmware, IEnumerable<string> keys)
    {
        try
        {
            _profiles.SaveUnresponsiveKeys(serial, firmware, keys);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Could not cache the unresponsive-key list for {Serial}", serial);
        }
    }

    private void UpdateLastPrinterSerialBestEffort(string serial)
    {
        try
        {
            _settings.Update(s => s with { LastPrinterSerial = serial });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Could not persist the last-connected printer serial for {Serial}", serial);
        }
    }

    private UsbPrinterInfo? ChooseAutoTarget()
    {
        var last = _settings.Current.LastPrinterSerial;
        return Printers.FirstOrDefault(p => p.Serial == last) ?? (Printers.Count == 1 ? Printers[0] : null);
    }

    /// <param name="announce">
    /// Whether to publish the transient <see cref="ConnectionState.Connecting"/> snapshot. False for background
    /// retries (poll-driven and hot-plug-driven reconnects) so a still-absent/claimed printer doesn't flicker
    /// Connecting/Disconnected on every attempt; the final Connected/Disconnected snapshot is always published.
    /// </param>
    private async Task ConnectCoreAsync(UsbPrinterInfo printer, CancellationToken ct, bool announce = true)
    {
        await CloseSessionAsync();
        _mediaChanges.Reset();
        _target = printer;
        if (announce) Publish(new DeviceSnapshot(ConnectionState.Connecting, printer, null, null, null, DeviceProblem.None));
        var session = new PrinterSession(_transports.Create(printer), _log);
        var assigned = false;
        try
        {
            await session.OpenAsync(ct);
            var profile = await _prober.ProbeAsync(session, printer.Serial, firmware => GetUnresponsiveKeysBestEffort(printer.Serial, firmware), ct);
            // A cache/settings write must never decide connectivity: a directory that can't be created or a
            // locked file only costs us the next reconnect's skip-list optimisation, not this connection.
            // Also: never persist an all-unresponsive probe (profile.Settings.Count == 0) — that almost
            // certainly means the probe itself failed to talk to the printer, not that every key is genuinely
            // unsupported, and caching it would wrongly skip every key forever.
            if (profile.Settings.Count > 0) SaveUnresponsiveKeysBestEffort(printer.Serial, profile.Firmware, profile.UnresponsiveKeys);
            var status = await session.GetHostStatusAsync(ct);
            if (_disposed) return; // about to be (or already being) torn down: let the finally below dispose it, don't publish or adopt it.
            _session = session;
            assigned = true;
            _failures = 0;
            UpdateLastPrinterSerialBestEffort(printer.Serial);
            Publish(new DeviceSnapshot(ConnectionState.Connected, printer, profile, status, PrinterStateResolver.Resolve(status), DeviceProblem.None));
        }
        catch (OperationCanceledException)
        {
            Publish(new DeviceSnapshot(ConnectionState.Disconnected, printer, null, null, null, DeviceProblem.NotResponding));
            throw;
        }
        catch (Exception ex) when (IsDeviceFailure(ex))
        {
            _failures++;
            _log.LogWarning(ex, "Connecting to printer {Serial} failed", printer.Serial);
            var problem = ex is PrinterUnavailableException u
                ? u.Reason switch
                {
                    PrinterUnavailableReason.Claimed => DeviceProblem.Claimed,
                    PrinterUnavailableReason.NotFound => DeviceProblem.NotFound,
                    _ => DeviceProblem.NotResponding,
                }
                : DeviceProblem.NotResponding;
            Publish(new DeviceSnapshot(ConnectionState.Disconnected, printer, null, null, null, problem));
        }
        finally
        {
            // Every non-success path (device failure, cancellation, an unexpected exception, or losing the
            // race with DisposeAsync) must dispose the local transport instead of leaking it.
            if (!assigned) await session.DisposeAsync();
        }
    }

    private async Task PollCoreAsync(CancellationToken ct)
    {
        if (_session is null)
        {
            if (_target is not null && Printers.Any(p => p.Serial == _target.Serial)) await ConnectCoreAsync(_target, ct, announce: false);
            return;
        }
        try
        {
            var status = await _session.GetHostStatusAsync(ct);
            _failures = 0;
            Publish(Snapshot with { Connection = ConnectionState.Connected, Status = status, State = PrinterStateResolver.Resolve(status), Problem = DeviceProblem.None });
        }
        catch (Exception ex) when (IsDeviceFailure(ex))
        {
            _failures++;
            _log.LogWarning(ex, "Status poll failed ({Failures} in a row)", _failures);
            if (_failures >= PollingPolicy.FailuresBeforeBackoff)
            {
                await CloseSessionAsync();
                Publish(Snapshot with { Connection = ConnectionState.Disconnected, Status = null, State = null, Problem = DeviceProblem.NotResponding });
            }
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollingPolicy.NextDelay(JobActive, _failures), _time, ct);
                await RefreshAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected error in the status poll loop");
            }
        }
    }

    private void OnDevicesChanged(object? sender, EventArgs e) => _ = HandleDevicesChangedAsync();

    private async Task HandleDevicesChangedAsync()
    {
        // This runs fire-and-forget off a discovery event, possibly racing DisposeAsync: bail out before
        // touching _lifetime (which DisposeAsync disposes) so a stray in-flight callback can't fault silently.
        if (_disposed) return;
        try
        {
            var ct = _lifetime.Token;
            await RunExclusiveAsync(async () =>
            {
                await RefreshPrintersAsync(ct);
                if (_session is not null && _target is not null && Printers.All(p => p.Serial != _target.Serial))
                {
                    await CloseSessionAsync();
                    Publish(Snapshot with { Connection = ConnectionState.Disconnected, Status = null, State = null, Problem = DeviceProblem.Unplugged });
                }
                else if (_session is null)
                {
                    var candidate = _target is not null ? Printers.FirstOrDefault(p => p.Serial == _target.Serial) : ChooseAutoTarget();
                    if (candidate is not null) await ConnectCoreAsync(candidate, ct, announce: false);
                }
            }, ct);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "Handling a USB device change failed");
        }
    }

    private async Task RefreshPrintersAsync(CancellationToken ct)
    {
        var found = await _discovery.FindAllAsync(ct);
        if (found.SequenceEqual(Printers)) return;
        Printers = found;
        PrintersChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task CloseSessionAsync()
    {
        if (_session is null) return;
        await _session.DisposeAsync();
        _session = null;
    }

    private void Publish(DeviceSnapshot snapshot)
    {
        if (_mediaChanges.Observe(snapshot)) snapshot = snapshot with { CalibrationSuggested = true };
        Snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Runs SmartCal under the exclusive gate, so the poll loop waits instead of counting the printer's
    /// busy silence as failures. Live ~HS readings are published as they arrive.
    /// </summary>
    public Task<CalibrationResult> CalibrateAsync(IProgress<CalibrationProgress>? progress, CancellationToken ct) => RunExclusiveAsync(async () =>
    {
        if (_session is null || Snapshot.Profile is null) return CalibrationResult.Failed(CalibrationFailure.NotConnected);
        var (session, profile) = (_session, Snapshot.Profile);
        Publish(Snapshot with { Activity = DeviceActivity.Calibrating, CalibrationSuggested = false });
        try
        {
            var live = new InlineProgress<CalibrationProgress>(p =>
            {
                if (p.LastStatus is { } s) Publish(Snapshot with { Status = s, State = PrinterStateResolver.Resolve(s) });
                progress?.Report(p);
            });
            var result = await _calibration.RunAsync(session, profile, ModelCatalog.For(profile), live, ct);
            if (result.Succeeded) PublishSettings(profile, result.Detected);
            return result;
        }
        finally
        {
            Publish(Snapshot with { Activity = DeviceActivity.None });
        }
    }, ct);

    public Task DismissCalibrationSuggestionAsync(CancellationToken ct) =>
        RunExclusiveAsync(() => { Publish(Snapshot with { CalibrationSuggested = false }); return Task.CompletedTask; }, ct);

    /// <summary>Reports synchronously on the caller's thread (Progress&lt;T&gt; would post to a sync context).</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private async Task RunExclusiveAsync(Func<Task> action, CancellationToken ct)
    {
        await _ops.WaitAsync(ct);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DeviceService));
            await action();
        }
        finally { _ops.Release(); }
    }

    private static bool IsDeviceFailure(Exception ex) =>
        ex is TimeoutException or IOException or UnauthorizedAccessException or PrinterUnavailableException or PrinterProtocolException;

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _discovery.DevicesChanged -= OnDevicesChanged;
        _discovery.StopWatching();
        await _lifetime.CancelAsync();
        if (_loop is not null)
        {
            try { await _loop; } catch (OperationCanceledException) { }
        }
        // Wait for whatever's currently in flight (e.g. a ConnectAsync/ReprobeAsync) to finish and release the
        // gate before closing the session, so we can never race an in-flight ConnectCoreAsync's own assignment.
        await _ops.WaitAsync(CancellationToken.None);
        try { await CloseSessionAsync(); }
        finally { _ops.Release(); }
        _lifetime.Dispose();
    }
}

/// <param name="Rejected">Keys whose read-back did not match what was written (printer refused or clamped it).</param>
public sealed record ApplyResult(IReadOnlyDictionary<string, string?> ReadBack, IReadOnlyList<string> Rejected);
