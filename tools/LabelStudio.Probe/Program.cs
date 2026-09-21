using System.Text;
using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Commands;
using LabelStudio.Devices.Discovery;
using LabelStudio.Devices.Status;
using LabelStudio.Devices.Transport;

// Usage: LabelStudio.Probe [--out <file.md>] [--test-print] [--investigate-m2a] [--calibrate-only] [--set key=value]...
// Default --out is per-unit (variant + serial) so re-running against a different printer, or a different
// unit of the same variant, never silently overwrites another unit's report (or the hand-written M1
// end-to-end report that predates this convention).
var outOption = Option(args, "--out");
var testPrint = args.Contains("--test-print");

var setPairs = new List<(string Key, string Value)>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] != "--set") continue;
    if (i + 1 >= args.Length)
    {
        Console.Error.WriteLine("--set requires a key=value argument.");
        return 1;
    }
    var raw = args[++i];
    var eq = raw.IndexOf('=');
    if (eq <= 0)
    {
        Console.Error.WriteLine($"--set value '{raw}' is not in key=value form.");
        return 1;
    }
    setPairs.Add((raw[..eq], raw[(eq + 1)..]));
}
if (setPairs.Count > 0)
{
    // Applies each pair with the verified setvar helper and exits; no questions, no ~JC, no ^JUS. This is
    // how the operator puts the printer back to a known state (e.g. ezpl.media_type=gap/notch) after
    // --investigate-m2a Q3 left it on the last swept candidate (see docs/hardware/zd220t-m2a-investigation.md).
    using var setCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    return await LabelStudio.Probe.Investigation.RunSetAsync(setPairs, setCts.Token);
}

if (args.Contains("--calibrate-only"))
{
    // Generous timeout: includes USB discovery retries, the 40 s poll, and an unbounded operator prompt for
    // the fed label count, same shape as --investigate-m2a.
    using var calibrateCts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
    return await LabelStudio.Probe.Investigation.RunCalibrateOnlyAsync(outOption, calibrateCts.Token);
}

if (args.Contains("--investigate-m2a"))
{
    using var investigation = new CancellationTokenSource(TimeSpan.FromMinutes(15));
    return await LabelStudio.Probe.Investigation.RunAsync(outOption, investigation.Token);
}
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

var printers = await new UsbPrinterDiscovery().FindAllAsync(cts.Token);
if (printers.Count == 0)
{
    Console.Error.WriteLine("No Zebra printer found on USB.");
    return 1;
}
var printer = printers[0];
Console.WriteLine($"Using {printer.FriendlyName} ({printer.Serial}) at {printer.DevicePath}");

await using var session = new PrinterSession(new UsbPrintTransport(printer.DevicePath));
await session.OpenAsync(cts.Token);
var profile = await new CapabilityProber().ProbeAsync(session, printer.Serial, new HashSet<string>(), cts.Token);
var status = await session.GetHostStatusAsync(cts.Token);
var outPath = outOption ?? Path.Combine("docs", "hardware", $"{profile.VariantName.ToLowerInvariant()}-{profile.Serial}-sgd-probe.md");

var md = new StringBuilder()
    .AppendLine($"# {profile.VariantName} SGD probe").AppendLine()
    .AppendLine($"Generated {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz} by `tools/LabelStudio.Probe`. Answers spec §19 question 1 for this unit.").AppendLine()
    .AppendLine("| Field | Value |").AppendLine("| --- | --- |")
    .AppendLine($"| Model (~HI) | {profile.Model} |")
    .AppendLine($"| Firmware | {profile.Firmware} |")
    .AppendLine($"| Dots/mm | {profile.DotsPerMm} |")
    .AppendLine($"| Memory | {profile.Memory} |")
    .AppendLine($"| Serial | {profile.Serial} |")
    .AppendLine($"| USB interface | `{printer.DevicePath}` |")
    .AppendLine($"| State (~HS) | {PrinterStateResolver.Resolve(status)} |")
    .AppendLine().AppendLine("## SGD keys").AppendLine()
    .AppendLine("| Key | Responded | Value |").AppendLine("| --- | --- | --- |");
foreach (var key in SgdKeys.ProbeList)
    md.AppendLine(profile.Settings.TryGetValue(key, out var v) ? $"| `{key}` | yes | `{v}` |" : $"| `{key}` | no | |");

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
await File.WriteAllTextAsync(outPath, md.ToString(), cts.Token);
Console.WriteLine($"Wrote {outPath}: {profile.Settings.Count} keys responded, {profile.UnresponsiveKeys.Count} did not.");

if (testPrint)
{
    var state = PrinterStateResolver.Resolve(status);
    if (state == PrinterState.Paused)
    {
        // Hardware finding (see docs/hardware/zd220t-sgd-probe.md / task-5-report.md): this unit's ~HS
        // reported Paused=true with no fault flags set (no paper-out, head-open or ribbon-out). A paused
        // printer holds buffered formats without feeding, so clear it with ZplCommands.Resume ("~PS")
        // before sending the label, same as pressing the PAUSE button.
        Console.WriteLine("Printer reported Paused; sending ~PS (ZplCommands.Resume) first.");
        await session.SendRawAsync(ZplCommands.Resume, cts.Token);
        await Task.Delay(300, cts.Token);
        state = PrinterStateResolver.Resolve(await session.GetHostStatusAsync(cts.Token));
    }

    // Require the printer to actually be Ready (not merely "not Paused"): a fault such as head-open or
    // media-out also reports Paused=false in some states, and a paused printer can resolve to a
    // still-not-Ready fault state after resuming (e.g. it was paused *and* out of media).
    if (state != PrinterState.Ready)
    {
        Console.Error.WriteLine($"Printer is not ready to print (state: {state}); test label not sent.");
        return 1;
    }
    await session.SendRawAsync(TestLabel.Build(profile, DateTimeOffset.Now), cts.Token);
    Console.WriteLine("Sent test label.");
}
return 0;

static string? Option(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
