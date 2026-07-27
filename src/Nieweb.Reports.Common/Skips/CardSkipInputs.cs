namespace Nieweb.Reports.Common.Skips;

/// <summary>
/// The per-card facts <see cref="SkipClassifier"/> needs, already
/// aggregated from a <c>CARDS</c> row, its <c>TESTED_OBJECT</c>
/// children, and the parent <c>PANELS</c> row. Kept transport-agnostic
/// so it can be produced either by a SQL <c>GROUP BY</c> (efficient for
/// panel-level reports) or by an in-memory join over the streaming
/// DTOs.
/// </summary>
/// <param name="NumberOfComponent">
/// <c>CARDS.Number_Of_Component</c> — the denominator for the
/// missing-ratio heuristic.
/// </param>
/// <param name="CardStatus"><c>CARDS.Card_Status</c>.</param>
/// <param name="AnomalyAr">
/// <c>CARDS.Anomaly_AR</c> — bit 9 (256) = machine skip, bit 11 (1024)
/// = overflow (TESTED_OBJECT truncated).
/// </param>
/// <param name="MissingCount">
/// Count of the card's <c>TESTED_OBJECT</c> rows carrying
/// <c>Error_Table</c> bit 1 ("Object missing"). The AOI-original bit is
/// used (not the after-review bit) so the heuristic measures what the
/// machine actually saw.
/// </param>
/// <param name="HasManualSkipButton">
/// <c>true</c> if any of the card's <c>TESTED_OBJECT</c> rows carries a
/// repair button mapped to <see cref="RepairButtonMeaning.ManualSkip"/>.
/// </param>
/// <param name="HasBeenReviewed">
/// Parent <c>PANELS.Has_Been_Reviewed</c> — gates
/// <see cref="SkipClass.ManualSkip"/> because the X-OUT button is
/// written at repair, after inspection.
/// </param>
public readonly record struct CardSkipInputs(
    int NumberOfComponent,
    int CardStatus,
    long AnomalyAr,
    int MissingCount,
    bool HasManualSkipButton,
    bool HasBeenReviewed);
