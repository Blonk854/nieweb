namespace Nieweb.Reports.Common.Skips;

/// <summary>
/// Pure, deterministic classifier that labels a sub-panel with its
/// <see cref="SkipClass"/> from <see cref="CardSkipInputs"/> and an
/// admin <see cref="SkipClassificationConfig"/>. All three skip
/// mechanisms and every threshold were verified against the frozen
/// HLYAOI archive; see the <c>skip-classification</c> repo memory.
/// </summary>
public static class SkipClassifier
{
    /// <summary>
    /// <c>CARDS.Anomaly_AR</c> bit 9 (1-indexed) = 256 — "Skipped
    /// sub-panel" (vit-aoi-database skill, CARDS anomaly table).
    /// </summary>
    public const long MachineSkipBit = 1L << 8;   // 256

    /// <summary>
    /// <c>CARDS.Anomaly_AR</c> bit 11 (1-indexed) = 1024 — "Too many
    /// defects on panel — not saved". Such overflow cards have
    /// truncated <c>TESTED_OBJECT</c> rows, so their missing count is
    /// unreliable and they are excluded from the heuristic.
    /// </summary>
    public const long OverflowBit = 1L << 10;     // 1024

    /// <summary>
    /// Classifies a card. Precedence is deliberate and mutually
    /// exclusive in practice: an explicit operator skip beats a machine
    /// auto-skip, which beats the missing-ratio heuristic.
    /// </summary>
    /// <param name="card">Per-card aggregated inputs.</param>
    /// <param name="config">Admin thresholds and button-label map.</param>
    public static SkipClass Classify(in CardSkipInputs card, SkipClassificationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // 1. Manual X-OUT — only trusted on reviewed panels, because the
        //    button is written at repair (after inspection). An
        //    unreviewed panel simply has no sanctions yet.
        if (card.HasBeenReviewed && card.HasManualSkipButton)
        {
            return SkipClass.ManualSkip;
        }

        // 2. Machine auto-skip from a skip mark read by the AOI.
        if ((card.AnomalyAr & MachineSkipBit) != 0)
        {
            return SkipClass.MachineFlagged;
        }

        // 3. Disabled-skip pollution: an (almost) all-missing card that
        //    was fully inspected. Overflow cards are excluded (truncated
        //    TESTED_OBJECT), and two floors guard against tiny cards
        //    tripping the ratio on a handful of genuine defects.
        if ((card.AnomalyAr & OverflowBit) == 0
            && card.NumberOfComponent >= config.MinComponentFloor
            && card.MissingCount >= config.AbsoluteMissingFloor
            && (double)card.MissingCount / card.NumberOfComponent >= config.MissingRatioThreshold)
        {
            return SkipClass.HeuristicMissing;
        }

        return SkipClass.None;
    }
}
