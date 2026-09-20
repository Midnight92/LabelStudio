using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using LabelStudio.Devices;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Discovery;
using LabelStudio.Devices.Protocol;
using LabelStudio.Devices.Status;
using LabelStudio.Devices.Transport;

namespace LabelStudio.Probe;

/// <summary>
/// M2a hardware questions (plan Task 1) plus the Task 1b follow-up: a verified setvar helper (the first run
/// found that a setvar immediately followed by a getvar can read back stale on this firmware — see
/// docs/hardware/zd220t-m2a-investigation.md Q3), a `--set` phase to put the printer back to a known state,
/// and a `--calibrate-only` phase that records what changes during ~JC without touching any setting.
/// Interactive: asks the operator to power-cycle the printer and to count fed labels. Every setting the
/// investigation phase changes is restored (with the verified helper) before it exits.
/// </summary>
internal static class Investigation
{
    private static readonly TimeSpan KeyTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly Regex ValidSgdKey = new("^[a-z0-9._]+$", RegexOptions.Compiled);

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
                var afterSet = await SetVarVerifiedAsync(session, SgdKeys.Darkness, testTone, ct);
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
                var restoredTone = await SetVarVerifiedAsync(session, SgdKeys.Darkness, originalTone, ct);
                if (RestoreConfirmed(restoredTone, originalTone))
                {
                    // Only commit to non-volatile memory once the restore is confirmed — saving an
                    // unconfirmed value would make the wrong darkness permanent (the bug this task exists
                    // to prevent).
                    await session.SendRawAsync("^XA^JUS^XZ", ct);
                }
                else
                {
                    md.AppendLine($"**WARNING: could not confirm restore of `{SgdKeys.Darkness}` to `{originalTone}` (printer now reads `{restoredTone}`). " +
                        $"The non-volatile value was NOT touched (`^XA^JUS^XZ` skipped), but the LIVE value may still be wrong. " +
                        $"Operator: run `tools/LabelStudio.Probe --set {SgdKeys.Darkness}={originalTone}` to fix it.**").AppendLine();
                }
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
                var readback = await SetVarVerifiedAsync(session, SgdKeys.MediaType, candidate, ct);
                md.AppendLine($"| `{candidate}` | `{readback}` |");
            }
            if (mediaType is not null)
            {
                var restoredMediaType = await SetVarVerifiedAsync(session, SgdKeys.MediaType, mediaType, ct);
                md.AppendLine().AppendLine($"Restored to `{restoredMediaType}`.").AppendLine();
                if (!RestoreConfirmed(restoredMediaType, mediaType))
                    md.AppendLine($"**WARNING: could not restore `{SgdKeys.MediaType}`; printer left at `{restoredMediaType}`.**").AppendLine();
            }

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

    /// <summary>
    /// Task 1b: puts the printer back to a known state (e.g. `ezpl.media_type=gap/notch`) using the verified
    /// setvar helper, then exits — no questions, no `~JC`, no `^JUS`. Keys are validated before connecting so
    /// a typo fails fast instead of talking to the printer first.
    /// </summary>
    public static async Task<int> RunSetAsync(IReadOnlyList<(string Key, string Value)> pairs, CancellationToken ct)
    {
        // Validate every pair up front, before opening a session, so a typo in the Nth pair never leaves the
        // first N-1 applied with no chance to report the rest.
        foreach (var (key, value) in pairs)
        {
            if (!ValidSgdKey.IsMatch(key))
            {
                Console.Error.WriteLine($"Invalid --set key '{key}': SGD keys must be lowercase letters, digits, dots and underscores only.");
                return 1;
            }
            try
            {
                ValidateSetvarValue(value);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Invalid --set value for '{key}={value}': {ex.Message}");
                return 1;
            }
        }

        var (_, session) = await OpenFirstAsync(ct);
        try
        {
            foreach (var (key, value) in pairs)
            {
                var before = await session.GetSgdAsync(key, KeyTimeout, ct);
                var after = await SetVarVerifiedAsync(session, key, value, ct);
                Console.WriteLine($"{key}: {before} -> {after}");
            }
            return 0;
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    /// <summary>
    /// Task 1b: runs only the calibration measurement (`~JC`) and records which observable values change,
    /// to find a substitute for the missing "calibration finished" signal (Q4 found `~HS` answers Ready the
    /// whole time). Changes nothing except what `~JC` itself changes — no restore needed.
    /// </summary>
    public static async Task<int> RunCalibrateOnlyAsync(string? outOption, CancellationToken ct)
    {
        var (printer, session) = await OpenFirstAsync(ct);
        try
        {
            PrinterState state;
            try
            {
                state = PrinterStateResolver.Resolve(await session.GetHostStatusAsync(ct));
            }
            catch (Exception ex) when (ex is TimeoutException or PrinterProtocolException)
            {
                Console.Error.WriteLine($"Printer is not Ready (no answer to ~HS: {ex.GetType().Name}); calibration not started (media not wasted).");
                return 1;
            }
            if (state != PrinterState.Ready)
            {
                Console.Error.WriteLine($"Printer is not Ready (state: {state}); calibration not started (media not wasted).");
                return 1;
            }

            var profile = await new CapabilityProber().ProbeAsync(session, printer.Serial, new HashSet<string>(), ct);
            var outPath = outOption ?? Path.Combine("docs", "hardware", $"{profile.VariantName.ToLowerInvariant()}-m2a-calibration.md");

            var pollKeys = new[] { SgdKeys.LabelLength, SgdKeys.MediaType, SgdKeys.SenseMode, SgdKeys.OdometerUserLabels };
            var preValues = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var key in pollKeys) preValues[key] = profile.Get(key);
            var printWidth = profile.Get(SgdKeys.PrintWidth);

            Console.WriteLine($"Preflight: ~HS = {state}");
            Console.WriteLine($"  {SgdKeys.MediaType} = {preValues[SgdKeys.MediaType]}");
            Console.WriteLine($"  {SgdKeys.SenseMode} = {preValues[SgdKeys.SenseMode]}");
            Console.WriteLine($"  {SgdKeys.LabelLength} = {preValues[SgdKeys.LabelLength]}");
            Console.WriteLine($"  {SgdKeys.PrintWidth} = {printWidth}");
            Console.WriteLine("Confirm the sensing mode above matches the loaded media before labels are fed.");

            var md = new StringBuilder();
            md.AppendLine($"# {profile.VariantName} — M2a calibration measurement").AppendLine()
              .AppendLine($"Generated {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz} by `tools/LabelStudio.Probe --calibrate-only`, firmware {profile.Firmware}.").AppendLine()
              .AppendLine("## Preflight").AppendLine()
              .AppendLine("| Field | Value |").AppendLine("| --- | --- |")
              .AppendLine($"| ~HS state | {state} |")
              .AppendLine($"| `{SgdKeys.MediaType}` | `{preValues[SgdKeys.MediaType]}` |")
              .AppendLine($"| `{SgdKeys.SenseMode}` | `{preValues[SgdKeys.SenseMode]}` |")
              .AppendLine($"| `{SgdKeys.LabelLength}` | `{preValues[SgdKeys.LabelLength]}` |")
              .AppendLine($"| `{SgdKeys.PrintWidth}` | `{printWidth}` |")
              .AppendLine();

            md.AppendLine("## Calibration poll (~JC)").AppendLine()
              .AppendLine($"| t (ms) | ~HS | `{SgdKeys.LabelLength}` | `{SgdKeys.MediaType}` | `{SgdKeys.SenseMode}` | `{SgdKeys.OdometerUserLabels}` |")
              .AppendLine("| --- | --- | --- | --- | --- | --- |");

            var firstChangeMs = new Dictionary<string, long?>(StringComparer.Ordinal);
            foreach (var key in pollKeys) firstChangeMs[key] = null;

            await session.SendRawAsync("~JC", ct);
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(40))
            {
                var elapsed = clock.ElapsedMilliseconds;
                string hsCell;
                try
                {
                    var s = await session.GetHostStatusAsync(ct);
                    hsCell = $"paperOut={s.PaperOut} paused={s.Paused} headUp={s.HeadUp} lengthDots={s.LabelLengthDots} formats={s.FormatsInBuffer} → {PrinterStateResolver.Resolve(s)}";
                }
                catch (Exception ex) when (ex is TimeoutException or PrinterProtocolException)
                {
                    hsCell = $"no answer ({ex.GetType().Name})";
                }

                var cells = new string?[pollKeys.Length];
                for (var i = 0; i < pollKeys.Length; i++)
                {
                    var key = pollKeys[i];
                    var value = await session.GetSgdAsync(key, KeyTimeout, ct);
                    cells[i] = value;
                    if (firstChangeMs[key] is null && value is not null && preValues[key] is not null &&
                        !string.Equals(value, preValues[key], StringComparison.Ordinal))
                        firstChangeMs[key] = elapsed;
                }

                md.AppendLine($"| {elapsed} | {hsCell} | `{cells[0]}` | `{cells[1]}` | `{cells[2]}` | `{cells[3]}` |");
                await Task.Delay(250, ct);
            }
            md.AppendLine();

            Console.Write("How many labels did the printer feed during calibration? ");
            var fed = Console.ReadLine();
            md.AppendLine($"**Labels fed by ~JC (operator count): {fed}**").AppendLine();

            md.AppendLine("## Observations").AppendLine()
              .AppendLine("| Key | First changed at (ms) |").AppendLine("| --- | --- |");
            foreach (var key in pollKeys)
                md.AppendLine($"| `{key}` | {(firstChangeMs[key] is { } ms ? ms.ToString() : "unchanged")} |");
            md.AppendLine();

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            await File.WriteAllTextAsync(outPath, md.ToString(), ct);
            Console.WriteLine($"Wrote {outPath}.");
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

    /// <summary>
    /// Sends a setvar, waits ~300 ms, then reads the key back; if the read-back does not match (case-insensitive,
    /// trimmed) the value being set, retries the whole set+read-back cycle up to 3 times total. Hardware finding
    /// (M2a Task 1): a setvar immediately followed by a getvar can read back stale on this firmware — see
    /// docs/hardware/zd220t-m2a-investigation.md Q3, where the Q3 restore silently did not stick.
    /// </summary>
    /// <returns>The final read-back value, or null if the key never answered at all.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is not printable ASCII or contains a double quote, which would break out of
    /// the quoted setvar argument (`! U1 setvar "key" "value"`) and could send unintended commands to the
    /// printer. Validated here so every call site — including the fixed literals inside this file — is
    /// covered, not just <see cref="RunSetAsync"/>'s operator-supplied values.
    /// </exception>
    private static async Task<string?> SetVarVerifiedAsync(PrinterSession session, string key, string value, CancellationToken ct)
    {
        ValidateSetvarValue(value);
        string? readBack = null;
        // 3 total set+read-back attempts (not an initial attempt plus 3 retries).
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await session.SendRawAsync($"! U1 setvar \"{key}\" \"{value}\"\r\n", ct);
            await Task.Delay(300, ct);
            readBack = await session.GetSgdAsync(key, KeyTimeout, ct);
            if (readBack is not null && string.Equals(readBack.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase))
                return readBack;
        }
        return readBack;
    }

    private static bool RestoreConfirmed(string? readBack, string expected) =>
        readBack is not null && string.Equals(readBack.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Rejects values that would break out of the quoted setvar argument or contain unsendable bytes.</summary>
    private static void ValidateSetvarValue(string value)
    {
        if (value.Contains('"'))
            throw new ArgumentException($"value '{value}' contains a double quote, which would break out of the quoted setvar argument.");
        if (value.Any(c => c < 0x20 || c > 0x7E))
            throw new ArgumentException($"value '{value}' contains a non-printable-ASCII character.");
    }

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
