using System.Globalization;

namespace LabelStudio.Devices.Protocol;

public sealed record HostIdentification(string Model, string Firmware, int DotsPerMm, string Memory);

public static class HostIdentificationParser
{
    public static HostIdentification Parse(string frame)
    {
        var parts = frame.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) throw new PrinterProtocolException($"~HI response malformed: '{frame}'.");
        var dotsPerMm = parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 8;
        return new HostIdentification(parts[0], parts[1], dotsPerMm, parts.Length > 3 ? parts[3] : "");
    }
}
