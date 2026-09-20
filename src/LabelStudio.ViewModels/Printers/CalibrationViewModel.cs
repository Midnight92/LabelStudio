using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelStudio.Devices;
using LabelStudio.Devices.Calibration;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Status;
using LabelStudio.ViewModels.Formatting;
using Microsoft.Extensions.Logging;

namespace LabelStudio.ViewModels.Printers;

public enum CalibrationUiState { Idle, Running, Succeeded, Failed }

/// <summary>SmartCal from the host (spec §6 Calibration Center, step 1). Shared by the Printers page and first run.</summary>
public sealed partial class CalibrationViewModel : ObservableObject, IDisposable
{
    // Segoe Fluent Icons: CheckMark, Warning.
    private const string OkGlyph = "", ProblemGlyph = "";

    private readonly DeviceService _devices;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<CalibrationViewModel> _log;

    public CalibrationViewModel(DeviceService devices, IUiDispatcher ui, ILogger<CalibrationViewModel> log)
    {
        (_devices, _ui, _log) = (devices, ui, log);
        FeedNotice = "";
        _devices.SnapshotChanged += OnSnapshotChanged;
        Apply(devices.Snapshot);
    }

    public ObservableCollection<ChecklistRow> Checklist { get; } = [];
    public ObservableCollection<KeyValueRow> DetectedRows { get; } = [];

    [ObservableProperty] public partial string FeedNotice { get; set; }
    [ObservableProperty] public partial string? FailureTitle { get; set; }
    [ObservableProperty] public partial string? FailureBody { get; set; }
    [ObservableProperty] public partial bool ShowCompatibilityAction { get; set; }
    [ObservableProperty] public partial bool SuggestCalibration { get; set; }
    [ObservableProperty] public partial string? CommandError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsSucceeded), nameof(IsFailed))]
    [NotifyCanExecuteChangedFor(nameof(RunSmartCalCommand))]
    public partial CalibrationUiState State { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSmartCalCommand))]
    public partial bool CanCalibrate { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintTestLabelCommand))]
    public partial bool IsReady { get; set; }

    // Bool views of State for XAML visibility (x:Bind functions can't take enum literals).
    public bool IsRunning => State == CalibrationUiState.Running;
    public bool IsSucceeded => State == CalibrationUiState.Succeeded;
    public bool IsFailed => State == CalibrationUiState.Failed;

    public event EventHandler? CompatibilityCheckRequested;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunSmartCalAsync() => CommandGuard.RunAsync(async ct =>
    {
        State = CalibrationUiState.Running;
        FailureTitle = FailureBody = null;
        ShowCompatibilityAction = false;
        CalibrationResult? result = null;
        try
        {
            result = await _devices.CalibrateAsync(null, ct);
        }
        finally
        {
            // A thrown device error leaves the flow Idle; CommandGuard shows the message.
            _ui.Post(() => ShowResult(result));
        }
    }, e => CommandError = e, _log);

    private bool CanRun() => CanCalibrate && State != CalibrationUiState.Running;

    [RelayCommand]
    private Task DismissSuggestionAsync() => CommandGuard.RunAsync(_devices.DismissCalibrationSuggestionAsync, e => CommandError = e, _log);

    [RelayCommand]
    private void CheckCompatibility() => CompatibilityCheckRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(IsReady))]
    private Task PrintTestLabelAsync() => CommandGuard.RunAsync(_devices.PrintTestLabelAsync, e => CommandError = e, _log);

    private void ShowResult(CalibrationResult? result)
    {
        if (result is null) { State = CalibrationUiState.Idle; return; }
        DetectedRows.Clear();
        if (result.Succeeded)
        {
            var dotsPerMm = _devices.Snapshot.Profile?.DotsPerMm ?? 8;
            foreach (var (key, label) in new[] { (SgdKeys.MediaType, "Row.MediaType"), (SgdKeys.LabelLength, "Row.LabelLength"), (SgdKeys.PrintWidth, "Row.PrintWidth") })
                if (result.Detected.TryGetValue(key, out var v))
                    DetectedRows.Add(new KeyValueRow(Strings.Get(label), key == SgdKeys.MediaType ? v : Measurement.FormatDots(v, dotsPerMm)));
            State = CalibrationUiState.Succeeded;
            return;
        }
        var failure = result.Failure!.Value;
        FailureTitle = Strings.Get($"Calibration.Failed.{failure}.Title");
        FailureBody = Strings.Get($"Calibration.Failed.{failure}.Body");
        ShowCompatibilityAction = failure is CalibrationFailure.NoGapFound or CalibrationFailure.Timeout;
        State = CalibrationUiState.Failed;
    }

    private void OnSnapshotChanged(object? sender, DeviceSnapshot s) => _ui.Post(() => Apply(s));

    private void Apply(DeviceSnapshot s)
    {
        var connected = s.Connection == ConnectionState.Connected && s.Status is not null;
        FeedNotice = Strings.Format("Calibration.FeedNotice", _devices.Traits.CalibrationFeedLabels); // traits follow the connected model
        SuggestCalibration = connected && s.CalibrationSuggested;
        IsReady = connected && s.State == PrinterState.Ready && s.Activity == DeviceActivity.None;
        if (s.Activity == DeviceActivity.Calibrating) return; // keep the pre-run checklist while the printer is busy

        Checklist.Clear();
        if (connected)
        {
            Checklist.Add(Row("Calibration.Check.Media", !s.Status!.PaperOut));
            Checklist.Add(Row("Calibration.Check.Cover", !s.Status.HeadUp));
            if (s.Profile?.PrintMethod == PrintMethod.ThermalTransfer) Checklist.Add(Row("Calibration.Check.Ribbon", !s.Status.RibbonOut));
        }
        CanCalibrate = connected && Checklist.All(r => r.Ok);
    }

    private static ChecklistRow Row(string key, bool ok) =>
        new(Strings.Get(ok ? key + ".Ok" : key + ".Problem"), ok, ok ? OkGlyph : ProblemGlyph);

    public void Dispose() => _devices.SnapshotChanged -= OnSnapshotChanged;
}
