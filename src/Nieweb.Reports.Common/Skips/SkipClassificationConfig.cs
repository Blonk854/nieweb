namespace Nieweb.Reports.Common.Skips;

/// <summary>
/// Admin-tunable parameters that drive <see cref="SkipClassifier"/>.
/// Every threshold has a data-validated default (see the
/// <c>skip-classification</c> repo memory and the HLYAOI archive
/// probes); a site can override any of them without a code change.
/// </summary>
/// <param name="RepairButtonMeanings">
/// Case-insensitive map from a repair-button label
/// (<c>TESTED_OBJECT.Repair_Button_Comment</c>) to its
/// <see cref="RepairButtonMeaning"/>. Labels absent from the map are
/// treated as <see cref="RepairButtonMeaning.Normal"/>.
/// </param>
/// <param name="MissingRatioThreshold">
/// Fraction of a card's components that must be flagged "Object
/// missing" for the card to be a <see cref="SkipClass.HeuristicMissing"/>.
/// Default <c>0.50</c>.
/// </param>
/// <param name="MinComponentFloor">
/// Minimum <c>CARDS.Number_Of_Component</c> before the missing-ratio
/// heuristic may fire — guards against a tiny card tripping the ratio
/// on a couple of genuine defects (the HLYAOI probe found a 4-component
/// card at 50% that was a false positive). Default <c>8</c>.
/// </param>
/// <param name="AbsoluteMissingFloor">
/// Minimum absolute missing-component count before the heuristic may
/// fire, a second guard alongside the ratio. Default <c>4</c>.
/// </param>
public sealed record SkipClassificationConfig(
    IReadOnlyDictionary<string, RepairButtonMeaning> RepairButtonMeanings,
    double MissingRatioThreshold = 0.50,
    int MinComponentFloor = 8,
    int AbsoluteMissingFloor = 4)
{
    /// <summary>
    /// Default site configuration, validated against the HLYAOI
    /// archive. Maps only the confirmed label <c>"X-OUT"</c> →
    /// <see cref="RepairButtonMeaning.ManualSkip"/>; sites add their own
    /// labels (e.g. <c>"FC"</c> → <see cref="RepairButtonMeaning.FalseCall"/>,
    /// <c>"MY_MISSING"</c> → <see cref="RepairButtonMeaning.ConfirmedRealMissing"/>).
    /// </summary>
    public static SkipClassificationConfig Default { get; } = new(
        RepairButtonMeanings: new Dictionary<string, RepairButtonMeaning>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-OUT"] = RepairButtonMeaning.ManualSkip,
        });

    /// <summary>
    /// Resolves a repair-button label to its configured meaning,
    /// defaulting to <see cref="RepairButtonMeaning.Normal"/> for a
    /// <c>null</c>, empty, or unmapped label.
    /// </summary>
    public RepairButtonMeaning MeaningOf(string? buttonComment)
        => !string.IsNullOrEmpty(buttonComment)
           && RepairButtonMeanings.TryGetValue(buttonComment, out var meaning)
            ? meaning
            : RepairButtonMeaning.Normal;
}
