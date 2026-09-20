using System.Globalization;
using LabelStudio.Devices.Capabilities;
using LabelStudio.Devices.Models;

namespace LabelStudio.Devices.Settings;

/// <summary>Validates one media setting against the model's limits and renders it as SGD or ZPL (spec §6 table).</summary>
public static class MediaSettingWriter
{
    private static readonly Dictionary<string, string[]> Choices = new(StringComparer.Ordinal)
    {
        [SgdKeys.MediaType] = [SgdValues.MediaType.Continuous, SgdValues.MediaType.GapNotch, SgdValues.MediaType.Mark],
        [SgdKeys.PrintMode] = [SgdValues.PrintMode.TearOff, SgdValues.PrintMode.Peel],
        [SgdKeys.PrintMethod] = [SgdValues.PrintMethod.ThermalTransfer, SgdValues.PrintMethod.DirectThermal],
    };

    public static string Normalise(string key, string value, ModelTraits traits)
    {
        if (!traits.CanWrite(key)) throw new ArgumentException($"'{key}' is not writable on this printer.", nameof(key));
        if (Choices.TryGetValue(key, out var choices))
            return choices.FirstOrDefault(c => string.Equals(c, value.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"'{value}' is not a valid value for {key}.", nameof(value));
        return key switch
        {
            SgdKeys.LabelLength => InRange(Int(value), traits.LabelLengthDots, key).ToString(CultureInfo.InvariantCulture),
            SgdKeys.PrintWidth => InRange(Int(value), traits.PrintWidthDots, key).ToString(CultureInfo.InvariantCulture),
            SgdKeys.TearOff => InRange(Int(value), traits.TearOffRange, key).ToString(CultureInfo.InvariantCulture),
            SgdKeys.Darkness => Darkness(value, traits.DarknessRange),
            _ => throw new ArgumentException($"'{key}' has no writer.", nameof(key)),
        };
    }

    public static string ToZpl(string key, string value) => key switch
    {
        SgdKeys.MediaType => value switch
        {
            SgdValues.MediaType.Continuous => "^XA^MNN^XZ",
            SgdValues.MediaType.Mark => "^XA^MNM^XZ",
            _ => "^XA^MNY^XZ",
        },
        SgdKeys.PrintMode => value == SgdValues.PrintMode.Peel ? "^XA^MMP^XZ" : "^XA^MMT^XZ",
        SgdKeys.PrintMethod => value == SgdValues.PrintMethod.DirectThermal ? "^XA^MTD^XZ" : "^XA^MTT^XZ",
        SgdKeys.LabelLength => $"^XA^LL{value}^XZ",
        SgdKeys.PrintWidth => $"^XA^PW{value}^XZ",
        SgdKeys.Darkness => $"~SD{(int)Math.Round(double.Parse(value, CultureInfo.InvariantCulture)):00}",
        SgdKeys.TearOff => $"~TA{int.Parse(value, CultureInfo.InvariantCulture).ToString("00;-00;00", CultureInfo.InvariantCulture)}",
        _ => throw new ArgumentException($"'{key}' has no ZPL equivalent.", nameof(key)),
    };

    /// <summary>Read-back comparison: numbers compare numerically ("16" = "16.0"), text case-insensitively.</summary>
    public static bool Matches(string expected, string? actual)
    {
        if (actual is null) return false;
        if (double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            return Math.Abs(a - b) < 0.001;
        return string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int Int(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new ArgumentException($"'{value}' is not a whole number.", nameof(value));

    private static int InRange(int value, IntRange range, string key) =>
        range.Contains(value) ? value : throw new ArgumentOutOfRangeException(key, value, $"Allowed range is {range.Min}–{range.Max}.");

    private static string Darkness(string value, IntRange range)
    {
        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            throw new ArgumentException($"'{value}' is not a number.", nameof(value));
        if (d < range.Min || d > range.Max) throw new ArgumentOutOfRangeException(SgdKeys.Darkness, d, $"Allowed range is {range.Min}–{range.Max}.");
        return d.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
