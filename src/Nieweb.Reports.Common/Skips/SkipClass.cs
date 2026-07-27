namespace Nieweb.Reports.Common.Skips;

/// <summary>
/// Classification of a sub-panel (<c>CARDS</c> row) as a genuine
/// inspection result vs one of three distinct "skip" mechanisms that
/// must be segregated from FPY / DPMO so the KPIs reflect real
/// production quality rather than skipped / empty boards.
/// </summary>
/// <remarks>
/// All three mechanisms were verified against the frozen HLYAOI archive
/// (see the <c>skip-classification</c> repo memory). They are kept
/// separate because they have different root causes and different
/// remediation: an operator X-OUT is a workflow fact, a machine flag is
/// a program setting, and the missing-ratio heuristic points at a
/// <i>disabled</i> skip feature that should be turned back on.
/// </remarks>
public enum SkipClass
{
    /// <summary>Normal card — participates in KPIs.</summary>
    None = 0,

    /// <summary>
    /// The operator marked the board absent at review by pressing a
    /// skip button (default label <c>"X-OUT"</c>); the card is a
    /// placeholder, not a real inspection. Post-reflow only — pre-reflow
    /// SPI has no repair sanctions.
    /// </summary>
    ManualSkip = 1,

    /// <summary>
    /// The AOI machine auto-skipped the sub-panel after reading a skip
    /// mark (<c>CARDS.Anomaly_AR</c> bit 9 = 256). No real inspection
    /// ran on the board.
    /// </summary>
    MachineFlagged = 2,

    /// <summary>
    /// The sub-panel was fully inspected but is (almost) entirely
    /// "Object missing" — the signature of an empty / depaneled board
    /// inspected with the skip feature <i>disabled</i>. Detected
    /// heuristically from the missing-component ratio.
    /// </summary>
    HeuristicMissing = 3,
}
