using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelStudio.Core;
using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.ViewModels.Formatting;
using LabelStudio.ViewModels.Printers;
using LabelStudio.ViewModels.Status;
using Microsoft.Extensions.Logging;

namespace LabelStudio.ViewModels.Home;

public enum FirstRunStep { FindPrinter, ConfirmMedia, Calibrate, TestPrint }

/// <summary>Spec §13: find your printer, confirm your media, run a calibration, print a test label.</summary>
public sealed partial class FirstRunViewModel : ObservableObject, IDisposable
{
    private readonly DeviceService _devices;
    private readonly IUiDispatcher _ui;
    private readonly INavigationService _navigation;
    private readonly ISettingsService _settings;
    private readonly ILogger<FirstRunViewModel> _log;

    public FirstRunViewModel(DeviceService devices, IUiDispatcher ui, INavigationService navigation, ISettingsService settings,
        CalibrationViewModel calibration, CompatibilityCheckerViewModel checker, ILogger<FirstRunViewModel> log)
    {
        (_devices, _ui, _navigation, _settings, _log) = (devices, ui, navigation, settings, log);
        (Calibration, Checker) = (calibration, checker);
        Calibration.PropertyChanged += OnCalibrationChanged;
        Calibration.CompatibilityCheckRequested += OnCheckRequested;
        Checker.LengthOnlyRequested += OnLengthOnlyRequested;
        _devices.SnapshotChanged += OnSnapshotChanged;
        Status = StatusPresenter.Present(devices.Snapshot, devices.Printers.Count);
        StepText = "";
        UpdateStep();
        Apply(devices.Snapshot);
    }

    public CalibrationViewModel Calibration { get; }
    public CompatibilityCheckerViewModel Checker { get; }
    public ObservableCollection<KeyValueRow> MediaRows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    public partial FirstRunStep Step { get; set; }

    [ObservableProperty] public partial string StepText { get; set; }
    [ObservableProperty] public partial bool IsFindStep { get; set; }
    [ObservableProperty] public partial bool IsMediaStep { get; set; }
    [ObservableProperty] public partial bool IsCalibrateStep { get; set; }
    [ObservableProperty] public partial bool IsTestPrintStep { get; set; }
    [ObservableProperty] public partial StatusPresentation Status { get; set; }
    [ObservableProperty] public partial bool ShowChecker { get; set; }
    [ObservableProperty] public partial bool HasTestPrinted { get; set; }
    [ObservableProperty] public partial string? CommandError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial bool IsConnected { get; set; }

    public event EventHandler? Completed;

    partial void OnStepChanged(FirstRunStep value) => UpdateStep();

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        ShowChecker = false;
        Step++;
    }

    private bool CanGoNext() => Step switch
    {
        FirstRunStep.FindPrinter => IsConnected,
        FirstRunStep.ConfirmMedia => IsConnected,
        FirstRunStep.Calibrate => IsConnected && Calibration.IsSucceeded,
        _ => false, // the last step finishes through PrintedCorrectly
    };

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        ShowChecker = false;
        Step--;
    }

    private bool CanGoBack() => Step > FirstRunStep.FindPrinter;

    [RelayCommand]
    private Task FindAgainAsync() => CommandGuard.RunAsync(_devices.ReconnectAsync, e => CommandError = e, _log);

    [RelayCommand]
    private void CheckMedia() => ShowChecker = true;

    [RelayCommand]
    private Task PrintTestLabelAsync() => CommandGuard.RunAsync(async ct =>
    {
        await _devices.PrintTestLabelAsync(ct);
        HasTestPrinted = true;
    }, e => CommandError = e, _log);

    [RelayCommand]
    private void PrintedCorrectly() => Finish();

    [RelayCommand]
    private void PrintedWrong() => ShowChecker = true;

    [RelayCommand]
    private void SkipSetup() => Finish();

    private void Finish()
    {
        try { _settings.Update(s => s with { FirstRunCompleted = true }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Could not record that first run was completed");
        }
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateStep()
    {
        StepText = Strings.Format("FirstRun.StepOf", (int)Step + 1, Enum.GetValues<FirstRunStep>().Length);
        IsFindStep = Step == FirstRunStep.FindPrinter;
        IsMediaStep = Step == FirstRunStep.ConfirmMedia;
        IsCalibrateStep = Step == FirstRunStep.Calibrate;
        IsTestPrintStep = Step == FirstRunStep.TestPrint;
    }

    private void OnSnapshotChanged(object? sender, DeviceSnapshot s) => _ui.Post(() => Apply(s));

    private void Apply(DeviceSnapshot s)
    {
        Status = StatusPresenter.Present(s, _devices.Printers.Count);
        IsConnected = s.Connection == ConnectionState.Connected;
        MediaRows.Clear();
        if (s.Profile is { } p)
            foreach (var (key, label) in new[] { (SgdKeys.MediaType, "Row.MediaType"), (SgdKeys.LabelLength, "Row.LabelLength"), (SgdKeys.PrintWidth, "Row.PrintWidth") })
                if (p.Get(key) is { } v)
                    MediaRows.Add(new KeyValueRow(Strings.Get(label), key == SgdKeys.MediaType ? v : Measurement.FormatDots(v, p.DotsPerMm)));
    }

    private void OnCalibrationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalibrationViewModel.State)) NextCommand.NotifyCanExecuteChanged();
    }

    private void OnCheckRequested(object? sender, EventArgs e) => ShowChecker = true;

    /// <summary>Length-only setup lives in the Printers page's media setup; first run hands over to it.</summary>
    private void OnLengthOnlyRequested(object? sender, EventArgs e) =>
        _navigation.NavigateTo(PageKeys.Printers, new PrinterDeepLink(PrinterTab.Calibration, PrinterSection.LengthOnly));

    public void Dispose()
    {
        _devices.SnapshotChanged -= OnSnapshotChanged;
        Calibration.PropertyChanged -= OnCalibrationChanged;
        Calibration.CompatibilityCheckRequested -= OnCheckRequested;
        Checker.LengthOnlyRequested -= OnLengthOnlyRequested;
        Calibration.Dispose();
        Checker.Dispose();
    }
}
