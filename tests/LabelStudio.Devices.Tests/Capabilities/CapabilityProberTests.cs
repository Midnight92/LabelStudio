using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Simulation;

namespace LabelStudio.Devices.Tests.Capabilities;

public class CapabilityProberTests
{
    private static readonly CapabilityProber Prober = new(TimeSpan.FromMilliseconds(50));

    private static async Task<(PrinterSession, SimulatedPrinter)> OpenAsync()
    {
        var printer = new SimulatedPrinter();
        printer.SilentKeys.Add(SgdKeys.PowerUpAction);
        var session = new PrinterSession(new SimulatedPrinterTransport(printer));
        await session.OpenAsync(CancellationToken.None);
        return (session, printer);
    }

    [Fact]
    public async Task Splits_keys_into_responding_and_unresponsive()
    {
        var (session, _) = await OpenAsync();
        var profile = await Prober.ProbeAsync(session, "USBSERIAL", new HashSet<string>(), CancellationToken.None);
        Assert.Equal("gap/notch", profile.Get(SgdKeys.MediaType));
        Assert.Contains(SgdKeys.TearOff, profile.UnresponsiveKeys);        // answered "?"
        Assert.Contains(SgdKeys.PowerUpAction, profile.UnresponsiveKeys);  // silent
        Assert.False(profile.Supports(SgdKeys.TearOff));
        Assert.Equal(SgdKeys.ProbeList.Count, profile.Settings.Count + profile.UnresponsiveKeys.Count);
    }

    [Fact]
    public async Task Builds_identity_and_variant()
    {
        var (session, _) = await OpenAsync();
        var profile = await Prober.ProbeAsync(session, "USBSERIAL", new HashSet<string>(), CancellationToken.None);
        Assert.Equal("ZD220-203dpi", profile.Model);
        Assert.Equal(8, profile.DotsPerMm);
        Assert.Equal(PrintMethod.ThermalTransfer, profile.PrintMethod);
        Assert.Equal("ZD220t", profile.VariantName);
        Assert.Equal("USBSERIAL", profile.Serial); // simulator has no device.unique_id
    }

    [Fact]
    public async Task Skipped_keys_are_not_queried()
    {
        var (session, printer) = await OpenAsync();
        var profile = await Prober.ProbeAsync(session, "S", new HashSet<string> { SgdKeys.PowerUpAction }, CancellationToken.None);
        Assert.DoesNotContain(SgdKeys.PowerUpAction, printer.GetVarRequests);
        Assert.Contains(SgdKeys.PowerUpAction, profile.UnresponsiveKeys);
    }

    [Theory]
    [InlineData("thermal trans", PrintMethod.ThermalTransfer)]
    [InlineData("direct thermal", PrintMethod.DirectThermal)]
    [InlineData(null, PrintMethod.Unknown)]
    public void Parses_print_method(string? raw, PrintMethod expected) => Assert.Equal(expected, CapabilityProber.ParsePrintMethod(raw));
}
