using LabelStudio.Devices.Protocol;

namespace LabelStudio.Devices.Tests.Protocol;

public class HostIdentificationParserTests
{
    [Fact]
    public void Parses_model_firmware_density_memory()
    {
        var hi = HostIdentificationParser.Parse("ZD220-203dpi,V84.20.21Z,8,8192KB");
        Assert.Equal(new HostIdentification("ZD220-203dpi", "V84.20.21Z", 8, "8192KB"), hi);
    }

    [Fact]
    public void Defaults_density_to_8_when_missing()
    {
        Assert.Equal(8, HostIdentificationParser.Parse("ZD220-203dpi,V84").DotsPerMm);
    }
}
