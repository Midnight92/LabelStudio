using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Commands;
using LabelStudio.Devices.Models;
using LabelStudio.Devices.Protocol;
using LabelStudio.Devices.Status;

namespace LabelStudio.Devices.Calibration;

/// <summary>
/// SmartCal from the host (spec §6): preflight, ~JC, then watch for the printer to rewrite zpl.label_length,
/// which is the only signal it gives that the measurement finished (docs/hardware/zd220t-m2a-calibration.md:
/// ~HS reports Ready throughout, so status alone can never say "done").
/// </summary>
public sealed class CalibrationRunner(TimeProvider time)
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Keys that carry the calibration result, read back when the printer answers them.</summary>
    public static readonly IReadOnlyList<string> ResultKeys = [SgdKeys.MediaType, SgdKeys.LabelLength, SgdKeys.PrintWidth];

    public async Task<CalibrationResult> RunAsync(PrinterSession session, CapabilityProfile profile, ModelTraits traits,
        IProgress<CalibrationProgress>? progress, CancellationToken ct)
    {
        var before = await session.GetHostStatusAsync(ct);
        switch (PrinterStateResolver.Resolve(before))
        {
            case PrinterState.HeadOpen: return CalibrationResult.Failed(CalibrationFailure.HeadOpen);
            case PrinterState.MediaOut: return CalibrationResult.Failed(CalibrationFailure.MediaOut);
            case PrinterState.RibbonOut: return CalibrationResult.Failed(CalibrationFailure.RibbonOut);
        }
        if (before.Paused) await session.SendRawAsync(ZplCommands.Resume, ct);

        // The length the printer is about to overwrite: the completion signal is this value changing.
        var lengthBefore = await session.GetSgdAsync(SgdKeys.LabelLength, SgdKeys.ProbeTimeout, ct);

        await session.SendRawAsync(ZplCommands.Calibrate, ct);
        var started = time.GetUtcNow();
        await Task.Delay(traits.CalibrationSettle, time, ct);
        string? candidate = null;
        var measuredNoChange = false;
        while (true)
        {
            HostStatus? status = null;
            try { status = await session.GetHostStatusAsync(ct); }
            catch (Exception ex) when (ex is TimeoutException or PrinterProtocolException) { /* still busy measuring */ }
            var elapsed = time.GetUtcNow() - started;
            progress?.Report(new CalibrationProgress(elapsed, status));

            if (status is { HeadUp: true }) return CalibrationResult.Failed(CalibrationFailure.HeadOpen);
            if (status is { PaperOut: true }) return CalibrationResult.Failed(CalibrationFailure.NoGapFound);

            var length = await session.GetSgdAsync(SgdKeys.LabelLength, SgdKeys.ProbeTimeout, ct);
            if (length is not null && length != lengthBefore)
            {
                // Confirm with one more poll so a value read mid-write is never taken as the result.
                if (candidate == length) break;
                candidate = length;
            }

            // An absolute deadline, not one that only applies while the length is unchanged: a length that
            // keeps reporting a different value never confirms, and without this the loop would spin inside
            // the exclusive session gate for the rest of the session, blocking every other device command.
            if (elapsed >= traits.CalibrationTimeout)
            {
                if (candidate is not null) return CalibrationResult.Failed(CalibrationFailure.Timeout);
                measuredNoChange = true; // no fault, no change: the printer re-measured the same media
                break;
            }
            await Task.Delay(PollInterval, time, ct);
        }

        var detected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in ResultKeys.Where(profile.Supports))
            if (await session.GetSgdAsync(key, SgdKeys.ProbeTimeout, ct) is { } value) detected[key] = value;
        return CalibrationResult.Success(detected, measuredNoChange);
    }
}
