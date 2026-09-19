using System.Globalization;
using LabelStudio.Devices.Capabilities;

namespace LabelStudio.Devices.Commands;

public static class TestLabel
{
    /// <summary>Test label; the timestamp is resolved host-side because the ZD220 has no real-time clock.</summary>
    public static string Build(CapabilityProfile profile, DateTimeOffset printedAt)
    {
        var when = printedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return "^XA^CI28"
             + "^FO30,30^A0N,36,36^FDZD220 Label Studio^FS"
             + $"^FO30,80^A0N,26,26^FD{Clean(profile.VariantName)} {Clean(profile.Firmware)}^FS"
             + $"^FO30,115^A0N,26,26^FDSerial {Clean(profile.Serial)}^FS"
             + $"^FO30,150^A0N,26,26^FD{when}^FS"
             + $"^FO30,190^BY2^BCN,60,Y,N,N^FD{Clean(profile.Serial)}^FS"
             + "^XZ";
    }

    // ^ and ~ start ZPL commands; they must never appear inside field data.
    private static string Clean(string value) => value.Replace("^", "", StringComparison.Ordinal).Replace("~", "", StringComparison.Ordinal);
}
