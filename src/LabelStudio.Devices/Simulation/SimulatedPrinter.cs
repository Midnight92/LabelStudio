using System.Text;
using System.Text.RegularExpressions;
using LabelStudio.Devices.Discovery;

namespace LabelStudio.Devices.Simulation;

/// <summary>
/// What the simulated ~JC finds. FindsGap=false models incompatible media: the printer feeds until it runs
/// out looking for a gap that is not there (measured: ~18 labels over ~20 s, ending in media out).
/// </summary>
public sealed record SimulatedCalibrationOutcome(bool FindsGap, int LengthDots);

/// <summary>
/// In-memory ZD220t that answers ~HS, ~HI and SGD getvar/setvar like the real firmware, including volatile
/// settings (lost on <see cref="PowerCycle"/> unless saved with ^JUS) and a timed ~JC calibration.
/// Used by tests and LABELSTUDIO_SIMULATOR=1. Defaults follow docs/hardware/zd220t-m2a-investigation.md.
/// </summary>
public sealed partial class SimulatedPrinter
{
    private DateTimeOffset? _calibratingUntil;

    public SimulatedPrinter() => SavedSgd = new Dictionary<string, string>(Sgd, StringComparer.Ordinal);

    public string Serial { get; init; } = "SIM0000000001";
    public string Model { get; init; } = "ZD220-200dpi";
    public string Firmware { get; set; } = "V84.20.21Z";
    public bool HeadUp { get; set; }
    public bool PaperOut { get; set; }
    public bool RibbonOut { get; set; }
    public bool Paused { get; set; }
    public bool Unplugged { get; set; }
    public bool Claimed { get; set; }
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    public Dictionary<string, string> Sgd { get; } = new(StringComparer.Ordinal)
    {
        ["appl.name"] = "V84.20.21Z",
        ["device.languages"] = "zpl",
        ["device.friendly_name"] = "ZD220 SIM",
        ["device.product_name"] = "ZD220",
        ["head.resolution.in_dpi"] = "203",
        ["ezpl.media_type"] = "gap/notch",
        ["ezpl.print_method"] = "thermal trans",
        ["media.printmode"] = "tear off",
        ["zpl.label_length"] = "1218",
        ["ezpl.print_width"] = "812",
        ["print.tone"] = "20.0",
        ["media.speed"] = "4.0",
        ["odometer.total_print_length"] = "4800 INCHES, 12192 CENTIMETERS",
        ["odometer.headclean"] = "4800 INCHES, 12192 CENTIMETERS",
        ["odometer.user_label_count"] = "1200",
    };

    /// <summary>Values restored by <see cref="PowerCycle"/>; ^JUS copies <see cref="Sgd"/> here.</summary>
    public Dictionary<string, string> SavedSgd { get; private set; }

    /// <summary>Measured on the ZD220t: a setvar survives a power cycle with no ^JU S. Set false to model firmware that reverts.</summary>
    public bool SetvarPersistsWithoutSave { get; set; } = true;

    /// <summary>Keys the firmware ignores entirely (no "?" reply) — exercises the timeout path.</summary>
    public HashSet<string> SilentKeys { get; } = new(StringComparer.Ordinal);
    /// <summary>Keys that answer getvar but ignore setvar.</summary>
    public HashSet<string> ReadOnlyKeys { get; } = new(StringComparer.Ordinal);
    public List<string> ReceivedJobs { get; } = [];
    public List<string> GetVarRequests { get; } = [];
    public List<string> SetVarRequests { get; } = [];
    /// <summary>Every write in arrival order — lets tests assert ordering (e.g. snapshot before setvar).</summary>
    public List<string> ReceivedCommands { get; } = [];

    /// <summary>Measured: a good calibration rewrites zpl.label_length at ~2.2 s.</summary>
    public TimeSpan CalibrationDuration { get; set; } = TimeSpan.FromSeconds(2.5);
    /// <summary>Measured: a calibration that cannot sense the media runs ~20 s before reporting media out.</summary>
    public TimeSpan FailureDuration { get; set; } = TimeSpan.FromSeconds(20);
    public SimulatedCalibrationOutcome CalibrationOutcome { get; set; } = new(FindsGap: true, LengthDots: 1218);
    public int CalibrationFedLabels { get; set; } = 2;
    public int FailureFedLabels { get; set; } = 18;
    public int CalibrationRuns { get; private set; }
    public int LabelsFed { get; private set; }
    public bool IsCalibrating => _calibratingUntil is { } until && Clock.GetUtcNow() < until;

    public UsbPrinterInfo Info => new(
        $@"\\?\usb#vid_0a5f&pid_0164#{Serial.ToLowerInvariant()}#{{28d78fad-5a12-11d1-ae5b-0000f803a8c2}}",
        0x0A5F, 0x0164, Serial, "ZD220 (simulated)");

    /// <summary>Switch off and on: volatile settings revert to the last ^JUS unless they persist anyway.</summary>
    public void PowerCycle()
    {
        CompleteCalibrationIfDue();
        _calibratingUntil = null;
        Paused = false;
        if (SetvarPersistsWithoutSave) return;
        Sgd.Clear();
        foreach (var (k, v) in SavedSgd) Sgd[k] = v;
    }

    internal string Respond(string input)
    {
        CompleteCalibrationIfDue();
        ReceivedCommands.Add(input);
        var output = new StringBuilder();
        if (input.Contains("~HS", StringComparison.Ordinal)) output.Append(HostStatusResponse()); // measured: ~HS answers throughout ~JC
        if (input.Contains("~HI", StringComparison.Ordinal)) output.Append($"\u0002{Model},{Firmware},8,8192KB\u0003\r\n");
        foreach (Match m in GetVarPattern().Matches(input))
        {
            var key = m.Groups[1].Value;
            GetVarRequests.Add(key);
            if (!SilentKeys.Contains(key)) output.Append('"').Append(Sgd.GetValueOrDefault(key, "?")).Append('"');
        }
        foreach (Match m in SetVarPattern().Matches(input))
        {
            var key = m.Groups[1].Value;
            SetVarRequests.Add(key);
            if (!ReadOnlyKeys.Contains(key) && !SilentKeys.Contains(key) && Sgd.ContainsKey(key)) Sgd[key] = m.Groups[2].Value;
        }
        if (input.Contains("~PP", StringComparison.Ordinal)) Paused = true;
        if (input.Contains("~PS", StringComparison.Ordinal)) Paused = false;
        if (input.Contains("~JC", StringComparison.Ordinal)) StartCalibration();
        if (input.Contains("^JUS", StringComparison.Ordinal)) SavedSgd = new Dictionary<string, string>(Sgd, StringComparer.Ordinal);
        else if (input.Contains("^XA", StringComparison.Ordinal)) ReceivedJobs.Add(input);
        return output.ToString();
    }

    private void StartCalibration()
    {
        CalibrationRuns++;
        // A calibration that cannot sense the media keeps feeding for much longer before it gives up.
        _calibratingUntil = Clock.GetUtcNow() + (CalibrationOutcome.FindsGap ? CalibrationDuration : CalibrationDuration + FailureDuration);
    }

    private void CompleteCalibrationIfDue()
    {
        if (_calibratingUntil is not { } until || Clock.GetUtcNow() < until) return;
        _calibratingUntil = null;
        if (!CalibrationOutcome.FindsGap)
        {
            LabelsFed += FailureFedLabels;
            PaperOut = true;
            return;
        }
        LabelsFed += CalibrationFedLabels;
        // Measured: the label length is the only key the printer rewrites; media type and sense mode are untouched.
        Sgd["zpl.label_length"] = CalibrationOutcome.LengthDots.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private string HostStatusResponse()
    {
        static char F(bool b) => b ? '1' : '0';
        var thermalTransfer = Sgd.GetValueOrDefault("ezpl.print_method") == "thermal trans";
        var paused = Paused || PaperOut || HeadUp; // measured: calibrating does NOT report paused
        var length = Sgd.GetValueOrDefault("zpl.label_length", "1218");
        return $"\u0002030,{F(PaperOut)},{F(paused)},{length},000,0,0,0,000,0,0,0\u0003\r\n"
             + $"\u0002001,0,{F(HeadUp)},{F(RibbonOut)},{F(thermalTransfer)},2,6,0,00000000,1,000\u0003\r\n"
             + "\u00021234,0\u0003\r\n";
    }

    [GeneratedRegex("! U1 getvar \"([^\"]+)\"")]
    private static partial Regex GetVarPattern();

    [GeneratedRegex("! U1 setvar \"([^\"]+)\" \"([^\"]*)\"")]
    private static partial Regex SetVarPattern();
}
