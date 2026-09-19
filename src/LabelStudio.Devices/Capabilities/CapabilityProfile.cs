namespace LabelStudio.Devices.Capabilities;

public enum PrintMethod { Unknown, DirectThermal, ThermalTransfer }

public sealed record CapabilityProfile(
    string Model, string Firmware, int DotsPerMm, string Memory, string Serial, PrintMethod PrintMethod,
    IReadOnlyDictionary<string, string> Settings, IReadOnlyList<string> UnresponsiveKeys)
{
    public bool Supports(string key) => Settings.ContainsKey(key);
    public string? Get(string key) => Settings.GetValueOrDefault(key);

    /// <summary>"ZD220t"/"ZD220d" from the ~HI model plus the probed print method.</summary>
    public string VariantName
    {
        get
        {
            var baseModel = Model.Split('-')[0];
            return PrintMethod switch
            {
                PrintMethod.ThermalTransfer => baseModel + "t",
                PrintMethod.DirectThermal => baseModel + "d",
                _ => baseModel,
            };
        }
    }
}
