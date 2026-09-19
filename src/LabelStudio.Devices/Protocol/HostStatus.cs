using System.Globalization;

namespace LabelStudio.Devices.Protocol;

public sealed record HostStatus(
    bool PaperOut, bool Paused, int LabelLengthDots, int FormatsInBuffer, bool BufferFull,
    bool UnderTemperature, bool OverTemperature,
    bool HeadUp, bool RibbonOut, bool ThermalTransferMode, int LabelsRemainingInBatch);

/// <summary>Parses the three ~HS strings. Field positions follow the ZPL II programming guide.</summary>
public static class HostStatusParser
{
    public static HostStatus Parse(IReadOnlyList<string> frames)
    {
        if (frames.Count < 2)
            throw new PrinterProtocolException($"~HS expected 3 status strings but received {frames.Count}.");
        var s1 = frames[0].Split(',');
        var s2 = frames[1].Split(',');
        if (s1.Length < 12 || s2.Length < 9)
            throw new PrinterProtocolException($"~HS response malformed: '{frames[0]}' / '{frames[1]}'.");

        return new HostStatus(
            PaperOut: Flag(s1[1]), Paused: Flag(s1[2]), LabelLengthDots: Num(s1[3]), FormatsInBuffer: Num(s1[4]), BufferFull: Flag(s1[5]),
            UnderTemperature: Flag(s1[10]), OverTemperature: Flag(s1[11]),
            HeadUp: Flag(s2[2]), RibbonOut: Flag(s2[3]), ThermalTransferMode: Flag(s2[4]), LabelsRemainingInBatch: Num(s2[8]));
    }

    private static bool Flag(string v) => v.Trim() == "1";

    private static int Num(string v) =>
        int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new PrinterProtocolException($"~HS field '{v}' is not a number.");
}
