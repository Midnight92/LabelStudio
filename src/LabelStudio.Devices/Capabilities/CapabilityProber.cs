namespace LabelStudio.Devices.Capabilities;

/// <summary>Builds a capability profile by asking the printer, never by model lookup (spec §4).</summary>
public sealed class CapabilityProber(TimeSpan? keyTimeout = null)
{
    private readonly TimeSpan _keyTimeout = keyTimeout ?? SgdKeys.ProbeTimeout;

    public Task<CapabilityProfile> ProbeAsync(PrinterSession session, string usbSerial, IReadOnlySet<string> skipKeys, CancellationToken ct) =>
        ProbeAsync(session, usbSerial, _ => skipKeys, ct);

    /// <param name="skipKeysForFirmware">Called with the ~HI firmware version, so cached skip lists are firmware-specific.</param>
    public async Task<CapabilityProfile> ProbeAsync(PrinterSession session, string usbSerial, Func<string, IReadOnlySet<string>> skipKeysForFirmware, CancellationToken ct)
    {
        var identification = await session.GetHostIdentificationAsync(ct);
        var skipKeys = skipKeysForFirmware(identification.Firmware);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        var unresponsive = new List<string>();
        foreach (var key in SgdKeys.ProbeList)
        {
            if (skipKeys.Contains(key)) { unresponsive.Add(key); continue; }
            var value = await session.GetSgdAsync(key, _keyTimeout, ct);
            if (value is null) unresponsive.Add(key);
            else settings[key] = value;
        }

        return new CapabilityProfile(
            identification.Model, identification.Firmware, identification.DotsPerMm, identification.Memory,
            settings.GetValueOrDefault(SgdKeys.UniqueId) ?? usbSerial,
            ParsePrintMethod(settings.GetValueOrDefault(SgdKeys.PrintMethod)),
            settings, unresponsive);
    }

    public static PrintMethod ParsePrintMethod(string? raw) => raw switch
    {
        null => PrintMethod.Unknown,
        _ when raw.Contains("trans", StringComparison.OrdinalIgnoreCase) => PrintMethod.ThermalTransfer,
        _ when raw.Contains("direct", StringComparison.OrdinalIgnoreCase) => PrintMethod.DirectThermal,
        _ => PrintMethod.Unknown,
    };
}
