using System.Globalization;
using System.Text.RegularExpressions;

namespace LabelStudio.ViewModels.Printers;

/// <summary>odometer.* values look like "33953 INCHES, 86242 CENTIMETERS" (docs/hardware/zd220t-sgd-probe.md).</summary>
public sealed partial record OdometerReading(long Inches, long Centimetres)
{
    public static bool TryParse(string? raw, out OdometerReading? reading)
    {
        reading = null;
        var m = raw is null ? null : Pattern().Match(raw);
        if (m is not { Success: true }) return false;
        reading = new OdometerReading(long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        return true;
    }

    [GeneratedRegex(@"^\s*(\d+)\s*INCHES\s*,\s*(\d+)\s*CENTIMETERS\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}
