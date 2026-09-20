namespace LabelStudio.Devices.Models;

public enum WriteStrategy { Sgd, Zpl }

public readonly record struct IntRange(int Min, int Max)
{
    public bool Contains(int value) => value >= Min && value <= Max;
    public int Clamp(int value) => Math.Clamp(value, Min, Max);
}

public enum MarkSensorReach { FullWidth, CentreToLeft }

/// <summary>Physical sensor limits that decide media compatibility (spec §2 "Sensors").</summary>
public sealed record SensorGeometry(bool GapSensorFixedAtCentre, MarkSensorReach MarkReach);

/// <summary>
/// Model-specific facts, looked up by product name. Everything printer-specific that shared code needs
/// lives here, so supporting another model is a new catalog entry rather than new branches (spec §3 portability).
/// </summary>
public sealed record ModelTraits(
    string ProductName,
    IntRange PrintWidthDots,
    IntRange LabelLengthDots,
    int MinLabelWidthDots,
    double? FixedSpeedIps,
    bool HasCutter,
    SensorGeometry? Sensors,
    int CalibrationFeedLabels,
    TimeSpan CalibrationSettle,
    TimeSpan CalibrationTimeout,
    IntRange DarknessRange,
    IntRange TearOffRange,
    IReadOnlyDictionary<string, WriteStrategy> WritableKeys)
{
    public bool CanWrite(string key) => WritableKeys.ContainsKey(key);

    /// <summary>
    /// A printer with no catalog entry: nothing is writable and no compatibility verdict is offered.
    /// Calibration stays available because ~JC is standard ZPL.
    /// </summary>
    public static ModelTraits Unknown { get; } = new(
        ProductName: "",
        PrintWidthDots: new(1, 832), LabelLengthDots: new(1, 7928), MinLabelWidthDots: 1,
        FixedSpeedIps: null, HasCutter: false, Sensors: null,
        CalibrationFeedLabels: 3, CalibrationSettle: TimeSpan.FromSeconds(1), CalibrationTimeout: TimeSpan.FromSeconds(60),
        DarknessRange: new(0, 30), TearOffRange: new(-120, 120),
        WritableKeys: new Dictionary<string, WriteStrategy>());
}
