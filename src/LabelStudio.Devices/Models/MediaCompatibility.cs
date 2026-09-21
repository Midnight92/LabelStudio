namespace LabelStudio.Devices.Models;

public enum MediaSensing { GapOrNotch, BlackMark, Continuous }

/// <summary>The operator's answers; null means not answered yet.</summary>
public sealed record MediaAnswers(MediaSensing? Sensing, bool? GapAtCentre, bool? MarkLeftOfCentre, bool? MeetsMinimumSize);

public enum CompatibilityReason { GapOffCentre, MarkOnRight, BelowMinimumSize }

public sealed record CompatibilityVerdict(bool IsComplete, IReadOnlyList<CompatibilityReason> Reasons)
{
    public bool IsCompatible => IsComplete && Reasons.Count == 0;
}

/// <summary>
/// Spec §6 media compatibility check: can this printer's fixed sensors ever calibrate on this media?
/// Only the questions the model's sensor geometry makes relevant are required.
/// </summary>
public static class MediaCompatibility
{
    /// <returns>null when the model has no sensor data, so no verdict can honestly be given.</returns>
    public static CompatibilityVerdict? Evaluate(ModelTraits traits, MediaAnswers a)
    {
        if (traits.Sensors is not { } sensors) return null;
        var reasons = new List<CompatibilityReason>();
        var complete = a.Sensing is not null && a.MeetsMinimumSize is not null;

        if (a.Sensing == MediaSensing.GapOrNotch && sensors.GapSensorFixedAtCentre)
        {
            if (a.GapAtCentre is null) complete = false;
            else if (a.GapAtCentre == false) reasons.Add(CompatibilityReason.GapOffCentre);
        }
        if (a.Sensing == MediaSensing.BlackMark && sensors.MarkReach == MarkSensorReach.CentreToLeft)
        {
            if (a.MarkLeftOfCentre is null) complete = false;
            else if (a.MarkLeftOfCentre == false) reasons.Add(CompatibilityReason.MarkOnRight);
        }
        if (a.MeetsMinimumSize == false) reasons.Add(CompatibilityReason.BelowMinimumSize);
        return new CompatibilityVerdict(complete, reasons);
    }
}
