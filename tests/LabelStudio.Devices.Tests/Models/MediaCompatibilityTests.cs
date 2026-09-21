using LabelStudio.Devices.Models;

namespace LabelStudio.Devices.Tests.Models;

public class MediaCompatibilityTests
{
    private static CompatibilityVerdict E(MediaSensing? s, bool? gap, bool? mark, bool? size) =>
        MediaCompatibility.Evaluate(ModelCatalog.Zd220, new MediaAnswers(s, gap, mark, size))!;

    [Fact]
    public void Centre_gap_and_big_enough_is_compatible() => Assert.True(E(MediaSensing.GapOrNotch, true, null, true).IsCompatible);

    [Fact]
    public void Off_centre_gap_is_incompatible() =>
        Assert.Equal([CompatibilityReason.GapOffCentre], E(MediaSensing.GapOrNotch, false, null, true).Reasons);

    [Fact]
    public void Right_side_mark_is_incompatible() =>
        Assert.Equal([CompatibilityReason.MarkOnRight], E(MediaSensing.BlackMark, null, false, true).Reasons);

    [Fact]
    public void Every_failing_answer_is_listed() =>
        Assert.Equal([CompatibilityReason.GapOffCentre, CompatibilityReason.BelowMinimumSize], E(MediaSensing.GapOrNotch, false, null, false).Reasons);

    [Fact]
    public void Continuous_media_only_needs_the_size_answer() => Assert.True(E(MediaSensing.Continuous, null, null, true).IsCompatible);

    [Theory]
    [InlineData(null, true, true, true)]
    [InlineData(MediaSensing.GapOrNotch, null, null, true)]
    [InlineData(MediaSensing.BlackMark, null, null, true)]
    [InlineData(MediaSensing.GapOrNotch, true, null, null)]
    public void Unanswered_relevant_questions_leave_it_incomplete(MediaSensing? s, bool? gap, bool? mark, bool? size)
    {
        var v = E(s, gap, mark, size);
        Assert.False(v.IsComplete);
        Assert.False(v.IsCompatible);
    }

    [Fact]
    public void Models_without_sensor_data_get_no_verdict() =>
        Assert.Null(MediaCompatibility.Evaluate(ModelTraits.Unknown, new MediaAnswers(MediaSensing.GapOrNotch, true, null, true)));
}
