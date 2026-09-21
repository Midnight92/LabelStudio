using LabelStudio.Devices.Protocol;

namespace LabelStudio.Devices.Calibration;

public enum CalibrationFailure
{
    NotConnected,
    /// <summary>Preflight or mid-run: the print head is open.</summary>
    HeadOpen,
    /// <summary>Preflight: no media loaded, so calibration was not started.</summary>
    MediaOut,
    RibbonOut,
    /// <summary>The printer fed through looking for a gap or mark and never found one — the incompatible-media signature.</summary>
    NoGapFound,
    Timeout,
}

/// <param name="MeasuredNoChange">
/// The printer never rewrote the label length before the timeout: it measured the same media again.
/// Not a failure, but the UI says so rather than implying something changed.
/// </param>
public sealed record CalibrationResult(CalibrationFailure? Failure, IReadOnlyDictionary<string, string> Detected, bool MeasuredNoChange = false)
{
    public bool Succeeded => Failure is null;
    public static CalibrationResult Success(IReadOnlyDictionary<string, string> detected, bool measuredNoChange = false) =>
        new(null, detected, measuredNoChange);
    public static CalibrationResult Failed(CalibrationFailure failure) => new(failure, new Dictionary<string, string>());
}

public sealed record CalibrationProgress(TimeSpan Elapsed, HostStatus? LastStatus);
