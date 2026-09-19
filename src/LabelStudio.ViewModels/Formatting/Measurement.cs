using System.Globalization;

namespace LabelStudio.ViewModels.Formatting;

public static class Measurement
{
    public static int DotsPerInch(int dotsPerMm) => (int)Math.Round(dotsPerMm * 25.4);

    public static string FormatDots(string rawDots, int dotsPerMm) =>
        int.TryParse(rawDots, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dots)
            ? Strings.Format("Format.Dots", dots, dots / (dotsPerMm * 25.4), dots / (double)dotsPerMm)
            : rawDots;
}
