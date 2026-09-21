namespace LabelStudio.ViewModels.Reference;

public enum BlinkCodeKind { LedPattern, FeedSequence }

/// <param name="SourcePage">Page in the Zebra user guide the entry was taken from (see docs/hardware/*-blink-codes-source.md).</param>
public sealed record BlinkCode(string Id, BlinkCodeKind Kind, string SourcePage);

/// <summary>
/// Status-light patterns and FEED-button sequences per printer model (spec §5). Text lives in Strings.resx
/// under BlinkCode.{Model}.{Id}.{Pattern|Meaning|Action}; entries are transcribed from Zebra's user guide, never invented.
/// </summary>
public static class BlinkCodeCatalog
{
    private static readonly Dictionary<string, BlinkCode[]> ByModel = new(StringComparer.OrdinalIgnoreCase)
    {
        // ZD200 Series Thermal Transfer Desktop Printer User Guide, P1129638-03EN Rev A (Zebra Technologies
        // Corporation), "Controls and Indicators" chapter. See docs/hardware/zd220-blink-codes-source.md.
        ["ZD220"] =
        [
            new("SolidGreen", BlinkCodeKind.LedPattern, "p. 23"),
            new("FlashingGreen", BlinkCodeKind.LedPattern, "p. 23"),
            new("DoubleFlashingGreen", BlinkCodeKind.LedPattern, "p. 23"),
            new("FlashingRed", BlinkCodeKind.LedPattern, "p. 23"),
            new("FlashingAmber", BlinkCodeKind.LedPattern, "p. 23"),
            new("FlashingRedRedGreen", BlinkCodeKind.LedPattern, "p. 24"),
            new("HoldFeedOneFlash", BlinkCodeKind.FeedSequence, "p. 25"),
            new("HoldFeedTwoFlashes", BlinkCodeKind.FeedSequence, "p. 25"),
            new("HoldFeedThreeFlashes", BlinkCodeKind.FeedSequence, "p. 25"),
            new("ReleaseFeedAfterThirdFlash", BlinkCodeKind.FeedSequence, "p. 25"),
        ],
    };

    public static IReadOnlyList<string> Models { get; } = ByModel.Keys.Order(StringComparer.Ordinal).ToList();

    public static IReadOnlyList<BlinkCode> For(string productName) => ByModel.GetValueOrDefault(productName) ?? [];
}
