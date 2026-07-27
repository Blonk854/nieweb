using Nieweb.Reports.Common.Skips;
using Xunit;

namespace Nieweb.Reports.Tests.Skips;

/// <summary>
/// Golden scenarios for <see cref="SkipClassifier"/>, mirroring the
/// cases verified against the HLYAOI archive (see the
/// <c>skip-classification</c> repo memory): a manual X-OUT card, a
/// machine skip-mark card, a real disabled-skip card (379/439 missing),
/// a tiny false-positive card, an overflow card, and precedence between
/// the three mechanisms.
/// </summary>
public sealed class SkipClassifierTests
{
    private static readonly SkipClassificationConfig _cfg = SkipClassificationConfig.Default;

    private static CardSkipInputs Card(
        int components = 100,
        int cardStatus = 1,
        long anomalyAr = 0,
        int missing = 0,
        bool manualSkip = false,
        bool reviewed = true)
        => new(components, cardStatus, anomalyAr, missing, manualSkip, reviewed);

    [Fact]
    public void NormalCard_IsNone()
    {
        Assert.Equal(SkipClass.None, SkipClassifier.Classify(Card(), _cfg));
    }

    [Fact]
    public void ManualSkipButton_OnReviewedPanel_IsManualSkip()
    {
        var card = Card(manualSkip: true, reviewed: true);
        Assert.Equal(SkipClass.ManualSkip, SkipClassifier.Classify(card, _cfg));
    }

    [Fact]
    public void ManualSkipButton_OnUnreviewedPanel_IsNotManualSkip()
    {
        // The X-OUT button is written at repair; an unreviewed panel has
        // no sanction yet, so the flag is not trusted.
        var card = Card(manualSkip: true, reviewed: false);
        Assert.Equal(SkipClass.None, SkipClassifier.Classify(card, _cfg));
    }

    [Fact]
    public void MachineSkipBit_IsMachineFlagged()
    {
        var card = Card(cardStatus: 0, anomalyAr: SkipClassifier.MachineSkipBit); // 256
        Assert.Equal(SkipClass.MachineFlagged, SkipClassifier.Classify(card, _cfg));
    }

    [Fact]
    public void MostlyMissingCard_IsHeuristicMissing()
    {
        // Real archive card 107564636: 379 missing / 439 components = 86%.
        var card = Card(components: 439, missing: 379);
        Assert.Equal(SkipClass.HeuristicMissing, SkipClassifier.Classify(card, _cfg));
    }

    [Fact]
    public void TinyCardAtThreshold_IsNotFlagged_MinComponentFloor()
    {
        // 4-component card with 2 missing = 50% would trip the ratio but
        // is below the min-component floor (8) — a known false positive.
        var card = Card(components: 4, missing: 2);
        Assert.Equal(SkipClass.None, SkipClassifier.Classify(card, _cfg));
    }

    [Fact]
    public void OverflowCard_IsExcludedFromHeuristic()
    {
        // Overflow (bit 11 = 1024) truncates TESTED_OBJECT, so the
        // missing count is unreliable — do not flag as HeuristicMissing.
        var card = Card(components: 100, missing: 80, anomalyAr: SkipClassifier.OverflowBit);
        Assert.Equal(SkipClass.None, SkipClassifier.Classify(card, _cfg));
    }

    [Fact]
    public void AbsoluteMissingFloor_BlocksLowRatioConfig()
    {
        // With a lowered ratio threshold, a card can hit the ratio with
        // very few missing; the absolute floor still blocks it.
        var cfg = _cfg with { MissingRatioThreshold = 0.30 };
        var card = Card(components: 10, missing: 3); // ratio 0.30 met, but 3 < floor 4
        Assert.Equal(SkipClass.None, SkipClassifier.Classify(card, cfg));

        var flagged = Card(components: 10, missing: 4); // ratio 0.40, 4 == floor
        Assert.Equal(SkipClass.HeuristicMissing, SkipClassifier.Classify(flagged, cfg));
    }

    [Fact]
    public void ManualSkip_BeatsMachineFlag_AndHeuristic()
    {
        // Precedence: an explicit operator X-OUT wins over everything.
        var card = Card(components: 439, missing: 379,
            anomalyAr: SkipClassifier.MachineSkipBit, manualSkip: true, reviewed: true);
        Assert.Equal(SkipClass.ManualSkip, SkipClassifier.Classify(card, _cfg));
    }

    [Fact]
    public void MachineFlag_BeatsHeuristic()
    {
        var card = Card(components: 439, missing: 379, anomalyAr: SkipClassifier.MachineSkipBit);
        Assert.Equal(SkipClass.MachineFlagged, SkipClassifier.Classify(card, _cfg));
    }

    [Theory]
    [InlineData("X-OUT", RepairButtonMeaning.ManualSkip)]
    [InlineData("x-out", RepairButtonMeaning.ManualSkip)] // case-insensitive
    [InlineData("FC", RepairButtonMeaning.Normal)]         // not mapped by default
    [InlineData("", RepairButtonMeaning.Normal)]
    [InlineData(null, RepairButtonMeaning.Normal)]
    public void MeaningOf_ResolvesButtonLabels(string? label, RepairButtonMeaning expected)
    {
        Assert.Equal(expected, SkipClassificationConfig.Default.MeaningOf(label));
    }
}
