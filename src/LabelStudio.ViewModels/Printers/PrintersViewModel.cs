using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Status;
using LabelStudio.ViewModels.Formatting;
using LabelStudio.ViewModels.Status;
using Microsoft.Extensions.Logging;

namespace LabelStudio.ViewModels.Printers;

public sealed partial class PrintersViewModel : ObservableObject, IDisposable
{
    // Segoe Fluent Icons code points (CheckMark, Remove).
    private const string RespondedGlyph = "", NoResponseGlyph = "";

    private static readonly (string Key, string Label, Func<string, int, string> Format)[] MediaFields =
    [
        (SgdKeys.MediaType, "Row.MediaType", (v, _) => v),
        (SgdKeys.PrintMethod, "Row.PrintMethod", (v, _) => v),
        (SgdKeys.PrintMode, "Row.PrintMode", (v, _) => v),
        (SgdKeys.LabelLength, "Row.LabelLength", Measurement.FormatDots),
        (SgdKeys.PrintWidth, "Row.PrintWidth", Measurement.FormatDots),
        (SgdKeys.Darkness, "Row.Darkness", (v, _) => v),
        (SgdKeys.PrintSpeed, "Row.PrintSpeed", (v, _) => Strings.Format("Format.Speed", v)),
        (SgdKeys.TearOff, "Row.TearOff", (v, _) => v),
    ];

    // M2a: spec §5 Counters card.
    public static readonly IReadOnlyList<string> CounterKeys = [SgdKeys.OdometerUserLabels, SgdKeys.OdometerTotal, SgdKeys.OdometerHeadClean];

    private readonly DeviceService _devices;
    private readonly IUiDispatcher _ui;
    private readonly INavigationService _navigation;
    private readonly UserCounterStore _counters;
    private readonly ILogger<PrintersViewModel> _log;
    private CapabilityProfile? _shownProfile;
    private string? _shownCurrentSerial;
    private StatusPresentation? _shownRowStatus;
    private long? _printerLabelCount;

    public PrintersViewModel(DeviceService devices, IUiDispatcher ui, INavigationService navigation, UserCounterStore counters,
        CalibrationViewModel calibration, MediaSetupViewModel mediaSetup, CompatibilityCheckerViewModel checker, ILogger<PrintersViewModel> log)
    {
        (_devices, _ui, _navigation, _counters, _log) = (devices, ui, navigation, counters, log);
        (Calibration, MediaSetup, Checker) = (calibration, mediaSetup, checker);
        Calibration.CompatibilityCheckRequested += OnCompatibilityCheckRequested;
        Checker.LengthOnlyRequested += OnLengthOnlyRequested;
        Status = StatusPresenter.Present(devices.Snapshot, devices.Printers.Count);
        Heading = Strings.Get("Printers.NoSelection");
        PauseLabel = Strings.Get("Printers.Pause");
        _devices.SnapshotChanged += OnSnapshotChanged;
        _devices.PrintersChanged += OnPrintersChanged;
        Apply(devices.Snapshot);
        ApplyPrinters();
    }

    public CalibrationViewModel Calibration { get; }
    public MediaSetupViewModel MediaSetup { get; }
    public CompatibilityCheckerViewModel Checker { get; }

    public ObservableCollection<PrinterListItem> Printers { get; } = [];
    public ObservableCollection<KeyValueRow> Identity { get; } = [];
    public ObservableCollection<KeyValueRow> Media { get; } = [];
    public ObservableCollection<KeyValueRow> Counters { get; } = [];
    public ObservableCollection<SgdKeyRow> ProbedKeys { get; } = [];

    [ObservableProperty] public partial StatusPresentation Status { get; set; }
    [ObservableProperty] public partial string Heading { get; set; }
    [ObservableProperty] public partial string PauseLabel { get; set; }
    [ObservableProperty] public partial bool IsPaused { get; set; }
    [ObservableProperty] public partial bool HasNoPrinters { get; set; }
    [ObservableProperty] public partial string? CommandError { get; set; }
    [ObservableProperty] public partial string? ActionHint { get; set; }
    [ObservableProperty] public partial bool IsConnecting { get; set; }
    [ObservableProperty] public partial bool HasCounters { get; set; }
    [ObservableProperty] public partial string? AppCounterText { get; set; }
    [ObservableProperty] public partial PrinterTab SelectedTab { get; set; }
    [ObservableProperty] public partial PrinterSection SelectedSection { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetCounterCommand))]
    public partial bool CanResetCounter { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand), nameof(ReprobeCommand), nameof(RefreshCountersCommand))]
    public partial bool IsConnected { get; set; }

    /// <summary>Gates <see cref="PrintTestLabelCommand"/>: printing is only safe when the printer is actually ready.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintTestLabelCommand))]
    public partial bool IsReady { get; set; }

    /// <summary>Gates <see cref="FeedCommand"/>: feeding is harmless while paused, unlike printing.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FeedCommand))]
    public partial bool CanFeed { get; set; }

    /// <summary>Toasts, Home's Calibrate shortcut and in-page links land here.</summary>
    public void ApplyDeepLink(PrinterDeepLink link)
    {
        SelectedTab = link.Tab;
        SelectedSection = link.Section;
    }

    [RelayCommand]
    private Task ConnectAsync(PrinterListItem? item) => item is null ? Task.CompletedTask : RunAsync(ct => _devices.ConnectAsync(item.Info, ct));

    [RelayCommand]
    private Task RefreshAsync() => RunAsync(ct => IsConnected ? _devices.RefreshAsync(ct) : _devices.ReconnectAsync(ct));

    [RelayCommand(CanExecute = nameof(IsReady))]
    private Task PrintTestLabelAsync() => RunAsync(_devices.PrintTestLabelAsync);

    [RelayCommand(CanExecute = nameof(CanFeed))]
    private Task FeedAsync() => RunAsync(_devices.FeedAsync);

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task TogglePauseAsync() => RunAsync(ct => IsPaused ? _devices.ResumeAsync(ct) : _devices.PauseAsync(ct));

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task ReprobeAsync() => RunAsync(_devices.ReprobeAsync);

    [RelayCommand]
    private Task StatusActionAsync() => Status.Action switch
    {
        StatusAction.Reconnect => RunAsync(_devices.ReconnectAsync),
        StatusAction.Resume => RunAsync(_devices.ResumeAsync),
        StatusAction.Retry => RunAsync(_devices.RefreshAsync),
        _ => Task.CompletedTask,
    };

    // M2a ------------------------------------------------------------------------------------------------

    [RelayCommand]
    private void OpenBlinkCodes() => _navigation.NavigateTo(PageKeys.BlinkCodes);

    [RelayCommand]
    private void EditMedia() => ApplyDeepLink(new PrinterDeepLink(PrinterTab.Calibration, PrinterSection.MediaSetup));

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task RefreshCountersAsync() => RunAsync(ct => _devices.ReadSettingsAsync(CounterKeys, ct));

    /// <summary>Confirmation is a flyout in the view; this resets the app counter to zero at the printer's current count.</summary>
    [RelayCommand(CanExecute = nameof(CanResetCounter))]
    private void ResetCounter()
    {
        if (_devices.Snapshot.Profile is not { } p || _printerLabelCount is not { } count) return;
        SaveBaselineBestEffort(p.Serial, count);
        AppCounterText = 0.ToString("N0", CultureInfo.CurrentCulture);
    }

    private void OnCompatibilityCheckRequested(object? sender, EventArgs e) => ApplyDeepLink(new PrinterDeepLink(PrinterTab.Calibration, PrinterSection.Checker));
    private void OnLengthOnlyRequested(object? sender, EventArgs e) => ApplyDeepLink(new PrinterDeepLink(PrinterTab.Calibration, PrinterSection.LengthOnly));

    private void ApplyCounters(CapabilityProfile? p)
    {
        Counters.Clear();
        _printerLabelCount = null;
        AppCounterText = null;
        if (p is not null)
        {
            if (long.TryParse(p.Get(SgdKeys.OdometerUserLabels), NumberStyles.Integer, CultureInfo.InvariantCulture, out var labels))
            {
                _printerLabelCount = labels;
                Counters.Add(new KeyValueRow(Strings.Get("Row.LabelsPrinted"), labels.ToString("N0", CultureInfo.CurrentCulture)));
                var baseline = _counters.GetBaseline(p.Serial);
                if (baseline is null || baseline > labels) // first sight, or the printer's own counter was reset below ours
                {
                    SaveBaselineBestEffort(p.Serial, labels);
                    baseline = labels;
                }
                AppCounterText = (labels - baseline.Value).ToString("N0", CultureInfo.CurrentCulture);
            }
            foreach (var (key, label) in new[] { (SgdKeys.OdometerTotal, "Row.HeadDistance"), (SgdKeys.OdometerHeadClean, "Row.SinceHeadClean") })
                if (OdometerReading.TryParse(p.Get(key), out var reading))
                    Counters.Add(new KeyValueRow(Strings.Get(label), Strings.Format("Format.Distance", reading!.Inches, reading.Centimetres)));
        }
        HasCounters = Counters.Count > 0;
        CanResetCounter = _printerLabelCount is not null;
    }

    private void SaveBaselineBestEffort(string serial, long value)
    {
        try { _counters.SetBaseline(serial, value); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Could not save the label-counter baseline for {Serial}", serial);
        }
    }

    // ----------------------------------------------------------------------------------------------------

    private Task RunAsync(Func<CancellationToken, Task> action) => CommandGuard.RunAsync(action, e => CommandError = e, _log);

    private void OnSnapshotChanged(object? sender, DeviceSnapshot s) => _ui.Post(() => Apply(s));
    private void OnPrintersChanged(object? sender, EventArgs e) => _ui.Post(ApplyPrinters);

    private void Apply(DeviceSnapshot s)
    {
        Status = StatusPresenter.Present(s, _devices.Printers.Count);
        IsConnected = s.Connection == ConnectionState.Connected;
        IsConnecting = s.Connection == ConnectionState.Connecting;
        IsPaused = s.Status?.Paused == true; // the printer pauses itself on faults, so this can be true even under a fault State
        IsReady = IsConnected && s.State == PrinterState.Ready && s.Activity == DeviceActivity.None;
        CanFeed = IsConnected && s.State is PrinterState.Ready or PrinterState.Paused && s.Activity == DeviceActivity.None;
        ActionHint = !IsConnected ? Strings.Get("Printers.Hint.NotConnected")
            : s.Activity == DeviceActivity.Calibrating ? Strings.Get("Printers.Hint.Calibrating")
            : s.State == PrinterState.Paused ? Strings.Get("Printers.Hint.Paused") // State-based: faults outrank Paused (see PrinterStateResolver)
            : s.State != PrinterState.Ready ? Strings.Get("Printers.Hint.Fault")
            : null;
        PauseLabel = Strings.Get(IsPaused ? "Printers.Resume" : "Printers.Pause");
        Heading = s.Profile?.VariantName ?? s.Printer?.FriendlyName ?? Strings.Get("Printers.NoSelection");

        if (!ReferenceEquals(s.Profile, _shownProfile)) // polls every 3 s must not rebuild these lists
        {
            _shownProfile = s.Profile;
            Replace(Identity, s.Profile is null ? [] : IdentityRows(s.Profile));
            Replace(Media, s.Profile is null ? [] : MediaFields.Where(f => s.Profile.Supports(f.Key))
                .Select(f => new KeyValueRow(Strings.Get(f.Label), f.Format(s.Profile.Settings[f.Key], s.Profile.DotsPerMm))));
            Replace(ProbedKeys, s.Profile is null ? [] : SgdKeys.ProbeList.Select(k => s.Profile.Settings.TryGetValue(k, out var v)
                ? new SgdKeyRow(k, v, true, Strings.Get("Sgd.Responded"), RespondedGlyph)
                : new SgdKeyRow(k, "", false, Strings.Get("Sgd.NoResponse"), NoResponseGlyph)));
            ApplyCounters(s.Profile);
        }
        if (s.Printer?.Serial != _shownCurrentSerial || Status != _shownRowStatus) ApplyPrinters();
    }

    private void ApplyPrinters()
    {
        _shownCurrentSerial = _devices.Snapshot.Printer?.Serial;
        _shownRowStatus = Status;
        Replace(Printers, _devices.Printers.Select(p =>
        {
            var current = p.Serial == _shownCurrentSerial;
            return new PrinterListItem(p, p.FriendlyName, p.Serial, current, current ? Status : null);
        }));
        HasNoPrinters = _devices.Printers.Count == 0;
    }

    private static IEnumerable<KeyValueRow> IdentityRows(CapabilityProfile p)
    {
        yield return new(Strings.Get("Row.Model"), p.VariantName);
        yield return new(Strings.Get("Row.Serial"), p.Serial);
        yield return new(Strings.Get("Row.Firmware"), p.Firmware);
        yield return new(Strings.Get("Row.Resolution"), Strings.Format("Format.Resolution", Measurement.DotsPerInch(p.DotsPerMm), p.DotsPerMm));
        if (p.Memory.Length > 0) yield return new(Strings.Get("Row.Memory"), p.Memory);
        if (p.Get(SgdKeys.Languages) is { } language) yield return new(Strings.Get("Row.Languages"), language);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    public void Dispose()
    {
        _devices.SnapshotChanged -= OnSnapshotChanged;
        _devices.PrintersChanged -= OnPrintersChanged;
        Calibration.CompatibilityCheckRequested -= OnCompatibilityCheckRequested;
        Checker.LengthOnlyRequested -= OnLengthOnlyRequested;
        Calibration.Dispose();
        MediaSetup.Dispose();
        Checker.Dispose();
    }
}
