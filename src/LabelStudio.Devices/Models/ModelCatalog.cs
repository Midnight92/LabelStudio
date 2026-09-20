using LabelStudio.Devices.Capabilities;

namespace LabelStudio.Devices.Models;

public static class ModelCatalog
{
    /// <summary>ZD220 (spec §2; write and calibration behaviour measured in M2a Task 1).</summary>
    public static ModelTraits Zd220 { get; } = new(
        ProductName: "ZD220",
        PrintWidthDots: new(1, 832),        // 4.09 in / 104 mm
        LabelLengthDots: new(203, 7928),    // 1.0 in minimum label, 39 in maximum
        MinLabelWidthDots: 203,             // 1.0 in minimum media width
        FixedSpeedIps: 4.0,
        HasCutter: false,
        Sensors: new SensorGeometry(GapSensorFixedAtCentre: true, MarkReach: MarkSensorReach.CentreToLeft),
        CalibrationFeedLabels: 2,
        CalibrationSettle: TimeSpan.FromSeconds(1),
        CalibrationTimeout: TimeSpan.FromSeconds(30),
        DarknessRange: new(0, 30),
        TearOffRange: new(-120, 120),
        WritableKeys: new Dictionary<string, WriteStrategy>(StringComparer.Ordinal)
        {
            [SgdKeys.MediaType] = WriteStrategy.Sgd,
            [SgdKeys.PrintMethod] = WriteStrategy.Sgd,
            [SgdKeys.PrintMode] = WriteStrategy.Sgd,
            [SgdKeys.LabelLength] = WriteStrategy.Sgd,
            [SgdKeys.PrintWidth] = WriteStrategy.Sgd,
            [SgdKeys.Darkness] = WriteStrategy.Sgd,
            [SgdKeys.TearOff] = WriteStrategy.Sgd,
        });

    private static readonly Dictionary<string, ModelTraits> ByProductName = new(StringComparer.OrdinalIgnoreCase)
    {
        [Zd220.ProductName] = Zd220,
    };

    public static ModelTraits For(CapabilityProfile? profile)
    {
        if (profile is null) return ModelTraits.Unknown;
        var name = profile.Get(SgdKeys.ProductName) ?? profile.Model.Split('-')[0];
        return ByProductName.GetValueOrDefault(name.Trim()) ?? ModelTraits.Unknown;
    }
}
