using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelStudio.Devices;
using LabelStudio.Devices.Models;

namespace LabelStudio.ViewModels.Printers;

/// <summary>
/// Spec §6 media compatibility check. Index properties bind to RadioButtons.SelectedIndex (-1 = unanswered).
/// The questions come from the connected model's sensor geometry, so they refresh when the printer changes.
/// </summary>
public sealed partial class CompatibilityCheckerViewModel : ObservableObject, IDisposable
{
    private readonly DeviceService _devices;
    private readonly IUiDispatcher _ui;

    public CompatibilityCheckerViewModel(DeviceService devices, IUiDispatcher ui)
    {
        (_devices, _ui) = (devices, ui);
        SensingIndex = GapAnswerIndex = MarkAnswerIndex = SizeAnswerIndex = -1; // -1 = unanswered (no partial-property initializers)
        _devices.SnapshotChanged += OnSnapshotChanged;
        ApplyTraits();
    }

    public ObservableCollection<string> Reasons { get; } = [];

    [ObservableProperty] public partial bool IsAvailable { get; set; }
    [ObservableProperty] public partial string? SizeQuestion { get; set; }

    [ObservableProperty] public partial int SensingIndex { get; set; }
    [ObservableProperty] public partial int GapAnswerIndex { get; set; }
    [ObservableProperty] public partial int MarkAnswerIndex { get; set; }
    [ObservableProperty] public partial int SizeAnswerIndex { get; set; }
    [ObservableProperty] public partial bool ShowGapQuestion { get; set; }
    [ObservableProperty] public partial bool ShowMarkQuestion { get; set; }
    [ObservableProperty] public partial bool IsVerdictVisible { get; set; }
    [ObservableProperty] public partial bool IsCompatible { get; set; }
    /// <summary>True when the "use a set length instead" action applies: an incompatible media, or continuous media.</summary>
    [ObservableProperty] public partial bool IsLengthOnlyOffered { get; set; }
    [ObservableProperty] public partial string? VerdictTitle { get; set; }
    [ObservableProperty] public partial string? VerdictBody { get; set; }

    public event EventHandler? LengthOnlyRequested;

    partial void OnSensingIndexChanged(int value) => Evaluate();
    partial void OnGapAnswerIndexChanged(int value) => Evaluate();
    partial void OnMarkAnswerIndexChanged(int value) => Evaluate();
    partial void OnSizeAnswerIndexChanged(int value) => Evaluate();

    [RelayCommand]
    private void UseLengthOnly() => LengthOnlyRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Reset()
    {
        SensingIndex = GapAnswerIndex = MarkAnswerIndex = SizeAnswerIndex = -1;
    }

    private void Evaluate()
    {
        MediaSensing? sensing = SensingIndex switch { 0 => MediaSensing.GapOrNotch, 1 => MediaSensing.BlackMark, 2 => MediaSensing.Continuous, _ => null };
        ShowGapQuestion = sensing == MediaSensing.GapOrNotch;
        ShowMarkQuestion = sensing == MediaSensing.BlackMark;
        var verdict = MediaCompatibility.Evaluate(_devices.Traits, new MediaAnswers(sensing, Answer(GapAnswerIndex), Answer(MarkAnswerIndex), Answer(SizeAnswerIndex)));
        IsVerdictVisible = verdict is { IsComplete: true };
        IsCompatible = verdict?.IsCompatible == true;
        Reasons.Clear();
        foreach (var reason in verdict?.Reasons ?? []) Reasons.Add(Strings.Get($"Checker.Reason.{reason}"));
        // Continuous media is compatible, but it has nothing for the sensors to measure: sending the operator to
        // SmartCal would feed a long run of labels and fail. Point them at length-only setup instead.
        var continuous = sensing == MediaSensing.Continuous;
        IsLengthOnlyOffered = IsVerdictVisible && (!IsCompatible || continuous);
        VerdictTitle = Strings.Get(IsCompatible ? "Checker.Compatible.Title" : "Checker.Incompatible.Title");
        VerdictBody = Strings.Get(IsCompatible
            ? continuous ? "Checker.Compatible.Continuous.Body" : "Checker.Compatible.Body"
            : "Checker.Incompatible.Body");
    }

    private static bool? Answer(int index) => index switch { 0 => true, 1 => false, _ => null };

    private void OnSnapshotChanged(object? sender, DeviceSnapshot s) => _ui.Post(ApplyTraits);

    private void ApplyTraits()
    {
        var traits = _devices.Traits;
        IsAvailable = traits.Sensors is not null;
        // Resolution comes from the probed printer, not a constant: this layer is shared across models.
        var dotsPerMm = _devices.Snapshot.Profile?.DotsPerMm ?? 8;
        var minInches = traits.LabelLengthDots.Min / (dotsPerMm * 25.4);
        SizeQuestion = Strings.Format("Checker.SizeQuestion", minInches.ToString("0.0", CultureInfo.CurrentCulture), Math.Round(minInches * 25.4));
    }

    public void Dispose() => _devices.SnapshotChanged -= OnSnapshotChanged;
}
