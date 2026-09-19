using System.Diagnostics;
using System.Text;
using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Discovery;
using LabelStudio.Devices.Protocol;
using LabelStudio.Devices.Status;
using LabelStudio.Devices.Transport;

namespace LabelStudio.Probe;

/// <summary>
/// M2a hardware questions (plan Task 1). Interactive: asks the operator to power-cycle the printer and to
/// count fed labels. Every setting it changes is restored before it exits; nothing is committed with ^JUS
/// except the explicit restore in question 1.
/// </summary>
internal static class Investigation
{
    private static readonly TimeSpan KeyTimeout = TimeSpan.FromMilliseconds(800);

    public static async Task<int> RunAsync(string? outOption, CancellationToken ct)
    {
        var md = new StringBuilder();
        var (printer, session) = await OpenFirstAsync(ct);
        try
        {
            var profile = await new CapabilityProber().ProbeAsync(session, printer.Serial, new HashSet<string>(), ct);
            var outPath = outOption ?? Path.Combine("docs", "hardware", $"{profile.VariantName.ToLowerInvariant()}-m2a-investigation.md");
            md.AppendLine($"# {profile.VariantName} — M2a hardware investigation").AppendLine()
              .AppendLine($"Generated {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz} by `tools/LabelStudio.Probe --investigate-m2a`, firmware {profile.Firmware}.").AppendLine();

            // Q1 — does setvar persist across a power cycle without ^JU S?
            var originalTone = await session.GetSgdAsync(SgdKeys.Darkness, KeyTimeout, ct);
            string persists;
            if (originalTone is null)
            {
                md.AppendLine("## Q1 — setvar persistence").AppendLine()
                  .AppendLine("print.tone did not answer; Q1 skipped.").AppendLine();
                persists = "unknown";
            }
            else
            {
                var testTone = originalTone.StartsWith("16", StringComparison.Ordinal) ? "17.0" : "16.0";
                await SetVarAsync(session, SgdKeys.Darkness, testTone, ct);
                var afterSet = await session.GetSgdAsync(SgdKeys.Darkness, KeyTimeout, ct);
                Console.WriteLine($"print.tone was {originalTone}, setvar {testTone}, reads back {afterSet}.");
                Console.WriteLine("Power-cycle the printer now (switch off, wait 5 s, switch on), wait for the light to go solid, then press Enter.");
                Console.ReadLine();
                await session.DisposeAsync();
                (printer, session) = await OpenFirstAsync(ct);
                var afterCycle = await session.GetSgdAsync(SgdKeys.Darkness, KeyTimeout, ct);
                persists = (afterCycle == afterSet).ToString().ToLowerInvariant();
                md.AppendLine("## Q1 — setvar persistence").AppendLine()
                  .AppendLine("| Step | print.tone |").AppendLine("| --- | --- |")
                  .AppendLine($"| original | `{originalTone}` |").AppendLine($"| after setvar {testTone} | `{afterSet}` |")
                  .AppendLine($"| after power cycle (no ^JUS) | `{afterCycle}` |").AppendLine()
                  .AppendLine($"**SetvarPersistsWithoutSave = {persists}**").AppendLine();
                await SetVarAsync(session, SgdKeys.Darkness, originalTone, ct);
                await session.SendRawAsync("^XA^JUS^XZ", ct);
            }

            // Q2 — are ZPL setting commands reflected in SGD? (each change is restored with the same mechanism)
            md.AppendLine("## Q2 — ZPL commands vs SGD keys").AppendLine()
              .AppendLine("| ZPL sent | Key | Before | After | Reflected |").AppendLine("| --- | --- | --- | --- | --- |");
            var length = await session.GetSgdAsync(SgdKeys.LabelLength, KeyTimeout, ct);
            var width = await session.GetSgdAsync(SgdKeys.PrintWidth, KeyTimeout, ct);
            if (int.TryParse(length, out var l)) await ZplRowAsync(md, session, $"^XA^LL{l + 8}^XZ", SgdKeys.LabelLength, $"^XA^LL{l}^XZ", ct);
            if (int.TryParse(width, out var w)) await ZplRowAsync(md, session, $"^XA^PW{w - 8}^XZ", SgdKeys.PrintWidth, $"^XA^PW{w}^XZ", ct);
            var tone = await session.GetSgdAsync(SgdKeys.Darkness, KeyTimeout, ct);
            if (double.TryParse(tone, System.Globalization.CultureInfo.InvariantCulture, out var t))
                await ZplRowAsync(md, session, $"~SD{(int)t + 1:00}", SgdKeys.Darkness, $"~SD{(int)t:00}", ct);
            var mediaType = await session.GetSgdAsync(SgdKeys.MediaType, KeyTimeout, ct);
            await ZplRowAsync(md, session, "^XA^MNN^XZ", SgdKeys.MediaType, mediaType == "mark" ? "^XA^MNM^XZ" : "^XA^MNY^XZ", ct);
            md.AppendLine();

            // Q3 — which ezpl.media_type values does setvar accept?
            md.AppendLine("## Q3 — ezpl.media_type values accepted by setvar").AppendLine()
              .AppendLine("| setvar value | reads back |").AppendLine("| --- | --- |");
            foreach (var candidate in new[] { "continuous", "gap/notch", "mark", "web", "blackmark" })
            {
                await SetVarAsync(session, SgdKeys.MediaType, candidate, ct);
                md.AppendLine($"| `{candidate}` | `{await session.GetSgdAsync(SgdKeys.MediaType, KeyTimeout, ct)}` |");
            }
            if (mediaType is not null) await SetVarAsync(session, SgdKeys.MediaType, mediaType, ct);
            md.AppendLine().AppendLine($"Restored to `{await session.GetSgdAsync(SgdKeys.MediaType, KeyTimeout, ct)}`.").AppendLine();

            // Q4 + Q5 — ~JC behaviour and which keys change
            var status = await session.GetHostStatusAsync(ct);
            if (PrinterStateResolver.Resolve(status) == PrinterState.Paused) await session.SendRawAsync("~PS", ct);
            var before = await ReadKeysAsync(session, ct);
            Console.WriteLine("About to run SmartCal (~JC). Media must be loaded and the cover closed. Press Enter to start.");
            Console.ReadLine();
            md.AppendLine("## Q4 — ~HS during ~JC").AppendLine().AppendLine("| t (ms) | ~HS |").AppendLine("| --- | --- |");
            await session.SendRawAsync("~JC", ct);
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(30))
            {
                string row;
                try
                {
                    var s = await session.GetHostStatusAsync(ct);
                    row = $"paperOut={s.PaperOut} paused={s.Paused} headUp={s.HeadUp} lengthDots={s.LabelLengthDots} formats={s.FormatsInBuffer} → {PrinterStateResolver.Resolve(s)}";
                }
                catch (Exception ex) when (ex is TimeoutException or PrinterProtocolException)
                {
                    row = $"no answer ({ex.GetType().Name})";
                }
                md.AppendLine($"| {clock.ElapsedMilliseconds} | {row} |");
                await Task.Delay(250, ct);
            }
            Console.Write("How many labels did the printer feed during calibration? ");
            var fed = Console.ReadLine();
            md.AppendLine().AppendLine($"**Labels fed by ~JC (operator count): {fed}**").AppendLine();

            var after = await ReadKeysAsync(session, ct);
            md.AppendLine("## Q5 — keys before/after ~JC").AppendLine().AppendLine("| Key | Before | After |").AppendLine("| --- | --- | --- |");
            foreach (var key in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
                md.AppendLine($"| `{key}` | `{before.GetValueOrDefault(key)}` | `{after.GetValueOrDefault(key)}` |");
            md.AppendLine();

            md.AppendLine("## Decision table (fill in from the tables above; Tasks 2 and 5 read this)").AppendLine()
              .AppendLine("| Decision | Value |").AppendLine("| --- | --- |")
              .AppendLine($"| SetvarPersistsWithoutSave | {persists} |")
              .AppendLine("| WriteStrategy per key (Sgd unless setvar failed to change the value) | |")
              .AppendLine("| Accepted ezpl.media_type values | |")
              .AppendLine("| ~HS during ~JC (answers+paused / silent / answers+ready) | |")
              .AppendLine("| Calibration settle time (first ms where ~HS is stable) | |")
              .AppendLine($"| Labels fed by ~JC | {fed} |")
              .AppendLine("| Keys that change after ~JC | |");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            await File.WriteAllTextAsync(outPath, md.ToString(), ct);
            Console.WriteLine($"Wrote {outPath}. Fill in the decision table from the measured rows.");
            return 0;
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    // Candidate keys include names that may not exist on this tier; "?"/silence is itself a finding for M2b.
    private static readonly string[] CandidateKeys =
    [
        .. SgdKeys.ProbeList,
        "ezpl.label_length_max", "media.type", "sensor.gap_threshold", "sensor.web_threshold", "sensor.media_threshold",
        "sensor.select", "ezpl.label_top", "zpl.left_position", "media.tof",
    ];

    private static async Task<Dictionary<string, string>> ReadKeysAsync(PrinterSession session, CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in CandidateKeys.Distinct())
            if (await session.GetSgdAsync(key, KeyTimeout, ct) is { } v) values[key] = v;
        return values;
    }

    private static async Task ZplRowAsync(StringBuilder md, PrinterSession session, string zpl, string key, string restore, CancellationToken ct)
    {
        var before = await session.GetSgdAsync(key, KeyTimeout, ct);
        await session.SendRawAsync(zpl, ct);
        await Task.Delay(300, ct);
        var after = await session.GetSgdAsync(key, KeyTimeout, ct);
        md.AppendLine($"| `{zpl}` | `{key}` | `{before}` | `{after}` | {(before != after ? "yes" : "no")} |");
        await session.SendRawAsync(restore, ct);
        await Task.Delay(300, ct);
    }

    private static Task SetVarAsync(PrinterSession session, string key, string value, CancellationToken ct) =>
        session.SendRawAsync($"! U1 setvar \"{key}\" \"{value}\"\r\n", ct);

    private static async Task<(UsbPrinterInfo, PrinterSession)> OpenFirstAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var printers = await new UsbPrinterDiscovery().FindAllAsync(ct);
            if (printers.Count > 0)
            {
                var session = new PrinterSession(new UsbPrintTransport(printers[0].DevicePath));
                await session.OpenAsync(ct);
                return (printers[0], session);
            }
            await Task.Delay(1000, ct);
        }
        throw new InvalidOperationException("No Zebra printer found on USB after 30 s.");
    }
}
