using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Models;

namespace LabelStudio.Devices.Tests.Models;

public class ModelCatalogTests
{
    private static CapabilityProfile Profile(string model, params (string Key, string Value)[] settings) =>
        new(model, "V1", 8, "8192KB", "ABC123456789", PrintMethod.ThermalTransfer,
            settings.ToDictionary(s => s.Key, s => s.Value), []);

    [Fact]
    public void Product_name_selects_the_model() =>
        Assert.Same(ModelCatalog.Zd220, ModelCatalog.For(Profile("XYZ-1", (SgdKeys.ProductName, "ZD220"))));

    [Fact]
    public void Falls_back_to_the_host_identification_model_prefix() =>
        Assert.Same(ModelCatalog.Zd220, ModelCatalog.For(Profile("ZD220-200dpi")));

    [Fact]
    public void Unknown_models_get_conservative_traits()
    {
        var traits = ModelCatalog.For(Profile("ZT999-300dpi"));
        Assert.Same(ModelTraits.Unknown, traits);
        Assert.Empty(traits.WritableKeys);
        Assert.Null(traits.Sensors);
        Assert.Same(ModelTraits.Unknown, ModelCatalog.For(null));
    }

    [Fact]
    public void Zd220_limits_match_the_spec()
    {
        var t = ModelCatalog.Zd220;
        Assert.Equal(832, t.PrintWidthDots.Max);        // 4.09 in / 104 mm at 8 dots/mm
        Assert.Equal(203, t.LabelLengthDots.Min);       // 1.0 in minimum label
        Assert.Equal(7928, t.LabelLengthDots.Max);      // 39 in / 991 mm
        Assert.Equal(4.0, t.FixedSpeedIps);
        Assert.False(t.HasCutter);
        Assert.Equal(new SensorGeometry(true, MarkSensorReach.CentreToLeft), t.Sensors);
        Assert.False(t.CanWrite(SgdKeys.PrintSpeed));
        Assert.True(t.CanWrite(SgdKeys.Darkness));
    }
}
