using System.Globalization;
using LabelStudio.ViewModels.Formatting;

namespace LabelStudio.ViewModels.Tests;

public class MeasurementTests
{
    [Fact]
    public void Formats_dots_in_three_units()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Assert.Equal("812 dots · 4.00 in · 101.5 mm", Measurement.FormatDots("812", 8));
    }

    [Fact] public void Passes_through_non_numeric() => Assert.Equal("auto", Measurement.FormatDots("auto", 8));
    [Fact] public void Eight_dots_per_mm_is_203_dpi() => Assert.Equal(203, Measurement.DotsPerInch(8));
}
