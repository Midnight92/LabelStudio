using System.Globalization;
using LabelStudio.Devices.Capabilities;

namespace LabelStudio.Devices.Commands;

public static class TestLabel
{
    /// <summary>Test label; the timestamp is resolved host-side because the ZD220 has no real-time clock.</summary>
    public static string Build(CapabilityProfile profile, DateTimeOffset printedAt)
    {
        var when = printedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        if (IsSmallMedia(profile))
            return "^XA^CI28"
                 + "^FO10,10^A0N,22,22^FDLabel Studio^FS"
                 + $"^FO10,38^A0N,18,18^FD{Clean(profile.VariantName)} {Clean(profile.Serial)}^FS"
                 + $"^FO10,62^A0N,18,18^FD{when}^FS"
                 + $"^FO10,90^BY1^BCN,50,N,N,N^FD{Clean(profile.Serial)}^FS"
                 + "^XZ";

        return "^XA^CI28"
             + "^FO30,30^A0N,36,36^FDZD220 Label Studio^FS"
             + $"^FO30,80^A0N,26,26^FD{Clean(profile.VariantName)} {Clean(profile.Firmware)}^FS"
             + $"^FO30,115^A0N,26,26^FDSerial {Clean(profile.Serial)}^FS"
             + $"^FO30,150^A0N,26,26^FD{when}^FS"
             + $"^FO30,190^BY2^BCN,60,Y,N,N^FD{Clean(profile.Serial)}^FS"
             + "^XZ";
    }

    /// <summary>Controller ruling: loaded media probed at 320 x 209 dots clips the full layout.</summary>
    private static bool IsSmallMedia(CapabilityProfile profile) =>
        (TryParseDots(profile.Get(SgdKeys.LabelLength)) is { } length && length < 300)
        || (TryParseDots(profile.Get(SgdKeys.PrintWidth)) is { } width && width < 400);

    private static int? TryParseDots(string? value) =>
        value is not null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    // ^ and ~ start ZPL commands; they must never appear inside field data.
    private static string Clean(string value) => value.Replace("^", "", StringComparison.Ordinal).Replace("~", "", StringComparison.Ordinal);
}
