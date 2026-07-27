namespace Nieweb.Reports.Common.Skips;

/// <summary>
/// The meaning an operator's repair-button press
/// (<c>TESTED_OBJECT.Repair_Button_Comment</c>) carries for skip
/// classification and KPI accounting. The label-to-meaning mapping is
/// admin-configurable (<see cref="SkipClassificationConfig"/>) because
/// the button labels vary per site, product, and review policy.
/// </summary>
public enum RepairButtonMeaning
{
    /// <summary>
    /// Ordinary sanction (repaired / good / confirmed faulty) — no
    /// special skip handling. The default for any unmapped label.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// The operator marked the whole board absent, so the card is a
    /// <see cref="SkipClass.ManualSkip"/> (default label <c>"X-OUT"</c>).
    /// </summary>
    ManualSkip = 1,

    /// <summary>
    /// The operator cleared the flag as a false call — the AOI defect
    /// is not a real defect.
    /// </summary>
    FalseCall = 2,

    /// <summary>
    /// The operator confirmed a genuine "Missing" defect. Counts as a
    /// real defect AND is a positive corroborating signal for
    /// <see cref="SkipClass.HeuristicMissing"/>.
    /// </summary>
    ConfirmedRealMissing = 3,
}
