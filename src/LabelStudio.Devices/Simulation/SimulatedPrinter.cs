using System.Text;
using System.Text.RegularExpressions;
using LabelStudio.Devices.Discovery;

namespace LabelStudio.Devices.Simulation;

/// <summary>In-memory ZD220t that answers ~HS, ~HI and SGD getvar like the real firmware. Used by tests and LABELSTUDIO_SIMULATOR=1.</summary>
public sealed partial class SimulatedPrinter
{
    public string Serial { get; init; } = "SIM0000000001";
    public string Model { get; init; } = "ZD220-200dpi";
    public string Firmware { get; init; } = "V84.20.21Z";
    public bool HeadUp { get; set; }
    public bool PaperOut { get; set; }
    public bool RibbonOut { get; set; }
    public bool Paused { get; set; }
    public bool Unplugged { get; set; }
    public bool Claimed { get; set; }

    public Dictionary<string, string> Sgd { get; } = new(StringComparer.Ordinal)
    {
        ["appl.name"] = "V84.20.21Z",
        ["device.languages"] = "zpl",
        ["device.friendly_name"] = "ZD220 SIM",
        ["head.resolution.in_dpi"] = "203",
        ["ezpl.media_type"] = "gap/notch",
        ["ezpl.print_method"] = "thermal trans",
        ["media.printmode"] = "tear off",
        ["zpl.label_length"] = "1218",
        ["ezpl.print_width"] = "812",
        ["print.tone"] = "20.0",
        ["media.speed"] = "4.0",
    };

    /// <summary>Keys the firmware ignores entirely (no "?" reply) — exercises the timeout path.</summary>
    public HashSet<string> SilentKeys { get; } = new(StringComparer.Ordinal);
    public List<string> ReceivedJobs { get; } = [];
    public List<string> GetVarRequests { get; } = [];

    public UsbPrinterInfo Info => new(
        $@"\\?\usb#vid_0a5f&pid_0164#{Serial.ToLowerInvariant()}#{{28d78fad-5a12-11d1-ae5b-0000f803a8c2}}",
        0x0A5F, 0x0164, Serial, "ZD220 (simulated)");

    internal string Respond(string input)
    {
        var output = new StringBuilder();
        if (input.Contains("~HS", StringComparison.Ordinal)) output.Append(HostStatusResponse());
        if (input.Contains("~HI", StringComparison.Ordinal)) output.Append($"\u0002{Model},{Firmware},8,8192KB\u0003\r\n");
        foreach (Match m in GetVarPattern().Matches(input))
        {
            var key = m.Groups[1].Value;
            GetVarRequests.Add(key);
            if (!SilentKeys.Contains(key)) output.Append('"').Append(Sgd.GetValueOrDefault(key, "?")).Append('"');
        }
        if (input.Contains("~PP", StringComparison.Ordinal)) Paused = true;
        if (input.Contains("~PS", StringComparison.Ordinal)) Paused = false;
        if (input.Contains("^XA", StringComparison.Ordinal)) ReceivedJobs.Add(input);
        return output.ToString();
    }

    private string HostStatusResponse()
    {
        static char F(bool b) => b ? '1' : '0';
        var thermalTransfer = Sgd.GetValueOrDefault("ezpl.print_method") == "thermal trans";
        return $"\u0002030,{F(PaperOut)},{F(Paused || PaperOut || HeadUp)},1218,000,0,0,0,000,0,0,0\u0003\r\n"
             + $"\u0002001,0,{F(HeadUp)},{F(RibbonOut)},{F(thermalTransfer)},2,6,0,00000000,1,000\u0003\r\n"
             + "\u00021234,0\u0003\r\n";
    }

    [GeneratedRegex("! U1 getvar \"([^\"]+)\"")]
    private static partial Regex GetVarPattern();
}
