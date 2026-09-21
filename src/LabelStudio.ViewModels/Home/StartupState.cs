using LabelStudio.Core;

namespace LabelStudio.ViewModels.Home;

/// <summary>
/// Captured once before the device service connects: connecting records LastPrinterSerial, which must not
/// hide the first-run flow halfway through it (spec §13 Home).
/// </summary>
public sealed record StartupState(bool IsFirstRun)
{
    public static StartupState From(AppSettings settings) => new(settings.LastPrinterSerial is null && !settings.FirstRunCompleted);
}
