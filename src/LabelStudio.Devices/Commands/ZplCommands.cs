namespace LabelStudio.Devices.Commands;

public static class ZplCommands
{
    /// <summary>~PH: slew to home — feeds one blank label.</summary>
    public const string Feed = "~PH";
    public const string Pause = "~PP";
    public const string Resume = "~PS";
    /// <summary>~JA: cancel all formats in the printer's buffer.</summary>
    public const string CancelAll = "~JA";
    /// <summary>~JC: SmartCal — the printer feeds labels and measures the media (spec §6).</summary>
    public const string Calibrate = "~JC";
    /// <summary>^JUS: save the current settings to non-volatile memory.</summary>
    public const string SaveSettings = "^XA^JUS^XZ";
}
