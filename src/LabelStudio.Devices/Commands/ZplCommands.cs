namespace LabelStudio.Devices.Commands;

public static class ZplCommands
{
    /// <summary>~PH: slew to home — feeds one blank label.</summary>
    public const string Feed = "~PH";
    public const string Pause = "~PP";
    public const string Resume = "~PS";
    /// <summary>~JA: cancel all formats in the printer's buffer.</summary>
    public const string CancelAll = "~JA";
}
