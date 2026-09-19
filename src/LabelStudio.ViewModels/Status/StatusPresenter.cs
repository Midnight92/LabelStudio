using LabelStudio.Devices;
using LabelStudio.Devices.Status;

namespace LabelStudio.ViewModels.Status;

public static class StatusPresenter
{
    // Segoe Fluent Icons code points (CheckMark, ErrorBadge, Warning, Help, Sync).
    private const string Check = "", Error = "", Warning = "", Unknown = "", Sync = "";

    public static StatusPresentation Present(DeviceSnapshot s, int knownPrinterCount) => s.Connection switch
    {
        ConnectionState.Connected when s.State is { } state => ForState(state, s.Profile?.VariantName ?? "ZD220"),
        ConnectionState.Connecting => Make(StatusTone.Neutral, Sync, "Connecting", StatusAction.None),
        ConnectionState.Disconnected => s.Problem switch
        {
            DeviceProblem.Claimed => Make(StatusTone.Critical, Error, "Claimed", StatusAction.Reconnect),
            DeviceProblem.Unplugged => Make(StatusTone.Neutral, Unknown, "Unplugged", StatusAction.Reconnect),
            DeviceProblem.NotFound => Make(StatusTone.Neutral, Unknown, "NotFound", StatusAction.Reconnect),
            _ => Make(StatusTone.Neutral, Unknown, "NotResponding", StatusAction.Reconnect),
        },
        _ => knownPrinterCount > 1
            ? Make(StatusTone.Neutral, Unknown, "ChoosePrinter", StatusAction.OpenPrinters)
            : Make(StatusTone.Neutral, Unknown, "NoPrinter", StatusAction.Reconnect),
    };

    private static StatusPresentation ForState(PrinterState state, string variant) => state switch
    {
        PrinterState.Ready => Make(StatusTone.Success, Check, "Ready", StatusAction.None, variant),
        PrinterState.Paused => Make(StatusTone.Caution, Warning, "Paused", StatusAction.Resume, variant),
        PrinterState.HeadOpen => Make(StatusTone.Critical, Error, "HeadOpen", StatusAction.Retry, variant),
        PrinterState.MediaOut => Make(StatusTone.Critical, Error, "MediaOut", StatusAction.Retry, variant),
        PrinterState.RibbonOut => Make(StatusTone.Critical, Error, "RibbonOut", StatusAction.Retry, variant),
        PrinterState.OverTemperature => Make(StatusTone.Critical, Error, "OverTemperature", StatusAction.None, variant),
        PrinterState.UnderTemperature => Make(StatusTone.Caution, Warning, "UnderTemperature", StatusAction.None, variant),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private static StatusPresentation Make(StatusTone tone, string glyph, string key, StatusAction action, string variant = "") =>
        new(tone, glyph,
            Strings.Format($"Status.{key}.Pill", variant),
            Strings.Get($"Status.{key}.Title"),
            Strings.Get($"Status.{key}.Body"),
            action,
            action == StatusAction.None ? null : Strings.Get($"StatusAction.{action}"));
}
