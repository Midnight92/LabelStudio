using LabelStudio.Core;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Commands;
using LabelStudio.Devices.Discovery;
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
    private readonly IPrinterDiscovery _discovery;
    private readonly ITransportFactory _transports;
    private readonly CapabilityProber _prober;
    private readonly IProfileCache _profiles;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<DeviceService> _log;
    private readonly SemaphoreSlim _ops = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private PrinterSession? _session;
    private UsbPrinterInfo? _target;
    private int _failures;
    private Task? _loop;
    private volatile bool _disposed;

    public DeviceService(IPrinterDiscovery discovery, ITransportFactory transports, CapabilityProber prober, IProfileCache profiles,
        ISettingsService settings, TimeProvider time, ILogger<DeviceService> log)
    {
        (_discovery, _transports, _prober, _profiles, _settings, _time, _log) = (discovery, transports, prober, profiles, settings, time, log);
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
    public Task PauseAsync(CancellationToken ct) => SendThenPollAsync(ZplCommands.Pause, ct);
    public Task ResumeAsync(CancellationToken ct) => SendThenPollAsync(ZplCommands.Resume, ct);

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
        Snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
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
