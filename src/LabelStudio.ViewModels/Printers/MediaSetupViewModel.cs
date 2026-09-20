using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Models;
using LabelStudio.Devices.Settings;
using LabelStudio.ViewModels.Formatting;
using Microsoft.Extensions.Logging;

namespace LabelStudio.ViewModels.Printers;

/// <summary>
/// Spec §6 media setup panel. Every row is gated on its key having answered the probe (hidden, not broken).
/// Edits live-apply after a short debounce and stay "unsaved" until Save to printer (^JU S).
/// </summary>
public sealed partial class MediaSetupViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(600);

    private static readonly string[] MediaTypes = [SgdValues.MediaType.Continuous, SgdValues.MediaType.GapNotch, SgdValues.MediaType.Mark];
    private static readonly string[] PrintModes = [SgdValues.PrintMode.TearOff, SgdValues.PrintMode.Peel];
    private static readonly string[] PrintMethods = [SgdValues.PrintMethod.ThermalTransfer, SgdValues.PrintMethod.DirectThermal];

    private readonly DeviceService _devices;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<MediaSetupViewModel> _log;
    private readonly ITimer _timer;
    private readonly Dictionary<string, string> _queued = new(StringComparer.Ordinal);
    private bool _loading;
    private int _dotsPerMm = 8;

    public MediaSetupViewModel(DeviceService devices, IUiDispatcher ui, TimeProvider time, ILogger<MediaSetupViewModel> log)
    {
        (_devices, _ui, _log) = (devices, ui, log);
        _timer = time.CreateTimer(_ => _ui.Post(() => _ = ApplyPendingAsync()), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _devices.SnapshotChanged += OnSnapshotChanged;
        _loading = true;
        MediaTypeIndex = PrintMethodIndex = PrintModeIndex = -1; // -1 = no selection (no partial-property initializers)
        LengthOnlyDots = double.NaN;
        _loading = false;
        Load(devices.Snapshot);
    }

    [ObservableProperty] public partial bool IsConnected { get; set; }
    [ObservableProperty] public partial bool CanEdit { get; set; }
    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial string? CommandError { get; set; }

    [ObservableProperty] public partial bool IsMediaTypeVisible { get; set; }
    [ObservableProperty] public partial bool IsPrintMethodVisible { get; set; }
    [ObservableProperty] public partial bool IsPrintModeVisible { get; set; }
    [ObservableProperty] public partial bool IsLabelLengthVisible { get; set; }
    [ObservableProperty] public partial bool IsPrintWidthVisible { get; set; }
    [ObservableProperty] public partial bool IsDarknessVisible { get; set; }
    [ObservableProperty] public partial bool IsSpeedVisible { get; set; }
    [ObservableProperty] public partial bool IsTearOffVisible { get; set; }
    [ObservableProperty] public partial bool IsLengthOnlyVisible { get; set; }

    [ObservableProperty] public partial int MediaTypeIndex { get; set; }
    [ObservableProperty] public partial int PrintMethodIndex { get; set; }
    [ObservableProperty] public partial int PrintModeIndex { get; set; }
    [ObservableProperty] public partial double LabelLengthDots { get; set; }
    [ObservableProperty] public partial double PrintWidthDots { get; set; }
    [ObservableProperty] public partial double Darkness { get; set; }
    [ObservableProperty] public partial double TearOff { get; set; }
    [ObservableProperty] public partial double LengthOnlyDots { get; set; }
    [ObservableProperty] public partial string? LabelLengthCaption { get; set; }
    [ObservableProperty] public partial string? PrintWidthCaption { get; set; }
    [ObservableProperty] public partial string? SpeedText { get; set; }

    private static readonly string[] RangeProperties =
        [nameof(LabelLengthMin), nameof(LabelLengthMax), nameof(PrintWidthMax), nameof(DarknessMin), nameof(DarknessMax), nameof(TearOffMin), nameof(TearOffMax)];

    public double LabelLengthMin => _devices.Traits.LabelLengthDots.Min;
    public double LabelLengthMax => _devices.Traits.LabelLengthDots.Max;
    public double PrintWidthMax => _devices.Traits.PrintWidthDots.Max;
    public double DarknessMin => _devices.Traits.DarknessRange.Min;
    public double DarknessMax => _devices.Traits.DarknessRange.Max;
    public double TearOffMin => _devices.Traits.TearOffRange.Min;
    public double TearOffMax => _devices.Traits.TearOffRange.Max;

    partial void OnMediaTypeIndexChanged(int value) { if (value >= 0) Queue(SgdKeys.MediaType, MediaTypes[value]); }
    partial void OnPrintMethodIndexChanged(int value) { if (value >= 0) Queue(SgdKeys.PrintMethod, PrintMethods[value]); }
    partial void OnPrintModeIndexChanged(int value) { if (value >= 0) Queue(SgdKeys.PrintMode, PrintModes[value]); }
    partial void OnDarknessChanged(double value) => QueueNumber(SgdKeys.Darkness, value, "0.0");
    partial void OnTearOffChanged(double value) => QueueNumber(SgdKeys.TearOff, value, "0");

    partial void OnLabelLengthDotsChanged(double value)
    {
        LabelLengthCaption = Caption(value);
        QueueNumber(SgdKeys.LabelLength, value, "0");
    }

    partial void OnPrintWidthDotsChanged(double value)
    {
        var clamped = double.IsNaN(value) ? value : Math.Clamp(value, 1, PrintWidthMax);
        if (clamped != value) { PrintWidthDots = clamped; return; } // re-enters with the clamped value
        PrintWidthCaption = Caption(value);
        QueueNumber(SgdKeys.PrintWidth, value, "0");
    }

    /// <summary>Sends everything queued since the last apply. Called by the debounce timer; public for tests.</summary>
    public async Task ApplyPendingAsync()
    {
        if (_queued.Count == 0) return;
        var batch = new Dictionary<string, string>(_queued, StringComparer.Ordinal);
        _queued.Clear();
        await CommandGuard.RunAsync(async ct =>
        {
            try
            {
                var result = await _devices.ApplySettingsAsync(batch, ct);
                if (result.Rejected.Count > 0)
                    CommandError = Strings.Format("MediaSetup.Rejected", string.Join(", ", result.Rejected.Select(Label)));
            }
            catch (ArgumentException ex)
            {
                _log.LogWarning(ex, "Media setting refused before sending");
                CommandError = Strings.Get("MediaSetup.OutOfRange");
            }
        }, e => CommandError = e, _log); // CommandGuard clears the error first, then the action may set the rejection text
        _ui.Post(() => Load(_devices.Snapshot)); // show what the printer actually holds
    }

    [RelayCommand]
    private async Task SaveToPrinterAsync()
    {
        await ApplyPendingAsync();
        await CommandGuard.RunAsync(_devices.CommitAsync, e => CommandError = e, _log);
    }

    [RelayCommand]
    private Task PrintTestLabelAsync() => CommandGuard.RunAsync(_devices.PrintTestLabelAsync, e => CommandError = e, _log);

    /// <summary>Spec §6 step 3: continuous media with a programmed length — a legitimate production setup.</summary>
    [RelayCommand]
    private Task ApplyLengthOnlyAsync()
    {
        if (double.IsNaN(LengthOnlyDots) || !_devices.Traits.LabelLengthDots.Contains((int)LengthOnlyDots))
        {
            CommandError = Strings.Format("MediaSetup.LengthOutOfRange", Caption(_devices.Traits.LabelLengthDots.Min), Caption(_devices.Traits.LabelLengthDots.Max));
            return Task.CompletedTask;
        }
        return CommandGuard.RunAsync(async ct =>
        {
            await _devices.ApplySettingsAsync(new Dictionary<string, string>
            {
                [SgdKeys.MediaType] = SgdValues.MediaType.Continuous,
                [SgdKeys.LabelLength] = ((int)LengthOnlyDots).ToString(CultureInfo.InvariantCulture),
            }, ct);
            _ui.Post(() => Load(_devices.Snapshot));
        }, e => CommandError = e, _log);
    }

    private void QueueNumber(string key, double value, string format)
    {
        if (!double.IsNaN(value)) Queue(key, value.ToString(format, CultureInfo.InvariantCulture));
    }

    private void Queue(string key, string value)
    {
        if (_loading || !CanEdit) return;
        _queued[key] = value;
        _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    private void OnSnapshotChanged(object? sender, DeviceSnapshot s) => _ui.Post(() => Load(s));

    private void Load(DeviceSnapshot s)
    {
        _loading = true;
        try
        {
            var p = s.Connection == ConnectionState.Connected ? s.Profile : null;
            var traits = _devices.Traits;
            foreach (var name in RangeProperties) OnPropertyChanged(name); // ranges come from the connected model's traits
            IsConnected = p is not null;
            CanEdit = p is not null && traits.WritableKeys.Count > 0;
            IsDirty = s.PendingCommitKeys.Count > 0;
            _dotsPerMm = p?.DotsPerMm ?? 8;

            IsMediaTypeVisible = Has(p, SgdKeys.MediaType);
            IsPrintMethodVisible = Has(p, SgdKeys.PrintMethod) && p!.PrintMethod == PrintMethod.ThermalTransfer;
            IsPrintModeVisible = Has(p, SgdKeys.PrintMode);
            IsLabelLengthVisible = Has(p, SgdKeys.LabelLength);
            IsPrintWidthVisible = Has(p, SgdKeys.PrintWidth);
            IsDarknessVisible = Has(p, SgdKeys.Darkness);
            IsSpeedVisible = Has(p, SgdKeys.PrintSpeed);
            IsTearOffVisible = Has(p, SgdKeys.TearOff);
            IsLengthOnlyVisible = CanEdit && IsMediaTypeVisible && IsLabelLengthVisible;
            if (p is null) return;

            // Keys the user is still editing (queued, not yet sent) keep their on-screen value.
            if (!_queued.ContainsKey(SgdKeys.MediaType)) MediaTypeIndex = IndexOf(MediaTypes, p.Get(SgdKeys.MediaType));
            if (!_queued.ContainsKey(SgdKeys.PrintMethod)) PrintMethodIndex = IndexOf(PrintMethods, p.Get(SgdKeys.PrintMethod));
            if (!_queued.ContainsKey(SgdKeys.PrintMode)) PrintModeIndex = IndexOf(PrintModes, p.Get(SgdKeys.PrintMode));
            if (!_queued.ContainsKey(SgdKeys.LabelLength)) LabelLengthDots = Number(p.Get(SgdKeys.LabelLength));
            if (!_queued.ContainsKey(SgdKeys.PrintWidth)) PrintWidthDots = Number(p.Get(SgdKeys.PrintWidth));
            if (!_queued.ContainsKey(SgdKeys.Darkness)) Darkness = Number(p.Get(SgdKeys.Darkness));
            if (!_queued.ContainsKey(SgdKeys.TearOff)) TearOff = Number(p.Get(SgdKeys.TearOff));
            LabelLengthCaption = Caption(LabelLengthDots);
            PrintWidthCaption = Caption(PrintWidthDots);
            SpeedText = traits.FixedSpeedIps is { } ips
                ? Strings.Format("MediaSetup.SpeedFixed", ips)
                : Strings.Format("Format.Speed", p.Get(SgdKeys.PrintSpeed));
        }
        finally
        {
            _loading = false;
        }
    }

    private string Caption(double dots) =>
        double.IsNaN(dots) ? "" : Measurement.FormatDots(((int)dots).ToString(CultureInfo.InvariantCulture), _dotsPerMm);

    private static string Label(string key) => Strings.Get(key switch
    {
        SgdKeys.MediaType => "Row.MediaType",
        SgdKeys.PrintMethod => "Row.PrintMethod",
        SgdKeys.PrintMode => "Row.PrintMode",
        SgdKeys.LabelLength => "Row.LabelLength",
        SgdKeys.PrintWidth => "Row.PrintWidth",
        SgdKeys.Darkness => "Row.Darkness",
        _ => "Row.TearOff",
    });

    private static bool Has(Devices.Capabilities.CapabilityProfile? p, string key) => p?.Supports(key) == true;

    private static int IndexOf(string[] values, string? value) =>
        Array.FindIndex(values, v => string.Equals(v, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static double Number(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;

    public void Dispose()
    {
        _devices.SnapshotChanged -= OnSnapshotChanged;
        _timer.Dispose();
    }
}
