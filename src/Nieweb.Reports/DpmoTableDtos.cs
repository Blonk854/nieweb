using Nieweb.DataSources;
using Nieweb.Reports.Common.Defects;
using Nieweb.Reports.Common.Skips;

namespace Nieweb.Reports;

/// <summary>
/// Column-grouping axis for a DPMO table
/// (Vieweb §3.1.6.5: "DPMO tables can show data by AOI / by defect /
/// by package (Jedec) / by part number / by product / by Reference
/// designator").
/// </summary>
public enum DpmoGroupBy
{
    /// <summary>One row per <c>Machine_Id</c>.</summary>
    AoiMachine = 0,

    /// <summary>One row per <see cref="DefectBit"/> present in the window.</summary>
    Defect = 1,

    /// <summary>One row per <c>Product_Id</c>.</summary>
    Product = 2,

    /// <summary>One row per <c>TESTED_OBJECT.Topology</c> (reference designator).</summary>
    ReferenceDesignator = 3,

    /// <summary>One row per <c>PART_NUMBER</c> name.</summary>
    PartNumber = 4,

    /// <summary>One row per <c>JEDEC</c> / package name.</summary>
    Jedec = 5,
}

/// <summary>
/// Which defect bits count towards the DPMO numerator. Encodes the
/// three Vieweb DPMO variants from §3.1.6.5 ("DPMO, DPMO dummy false
/// and DPMO real defects") using the VIT rule that a DummyFault
/// sanction clears every bit in <c>Error_Table_AR</c>.
/// </summary>
public enum DpmoNumerator
{
    /// <summary>
    /// Raw AOI defects: bits set in <c>TESTED_OBJECT.Error_Table</c>
    /// (before review). Answers "how did the AOI see the board?".
    /// </summary>
    Aoi = 0,

    /// <summary>
    /// Real defects: bits set in <c>TESTED_OBJECT.Error_Table_AR</c>
    /// (post-review). Excludes anything the review operator
    /// re-classified as a dummy fault. This is the Vieweb "DPMO real
    /// defects" flavour.
    /// </summary>
    Real = 1,

    /// <summary>
    /// Dummy / false calls: bits set in
    /// <c>TESTED_OBJECT.Error_Table</c> but cleared in
    /// <c>TESTED_OBJECT.Error_Table_AR</c> by the review operator's
    /// DummyFault sanction. This is the Vieweb "DPMO dummy false"
    /// flavour.
    /// </summary>
    Dummy = 2,
}

/// <summary>
/// Which tested-object kinds count as opportunities in the DPMO
/// denominator. Matches the Vieweb DPMO sub-flavours from the
/// <c>aoi-quality-metrics</c> skill ("DPMO defects" vs "DPMO defects
/// components" vs "DPMO defects paste").
/// </summary>
public enum DpmoOpportunity
{
    /// <summary>Every tested object contributes one opportunity.</summary>
    All = 0,

    /// <summary>
    /// Only objects tagged as components
    /// (<c>OBJECT_TYPE.Object_Type_Id</c> bit <c>0x00000001</c>).
    /// </summary>
    Components = 1,

    /// <summary>
    /// Only objects tagged as paste pads
    /// (<c>OBJECT_TYPE.Object_Type_Id</c> bit <c>0x00000010</c>).
    /// </summary>
    Paste = 2,
}

/// <summary>
/// Filter accepted by <see cref="DpmoTableReport"/>.
/// </summary>
/// <param name="Window">Half-open UTC time window over <c>Panel_Numeric_Date</c>.</param>
/// <param name="GroupBy">Rows grouped by the chosen axis.</param>
/// <param name="Numerator">Which defect bits count (AOI / Real / Dummy).</param>
/// <param name="Opportunity">Which tested-object kinds count as opportunities (All / Components / Paste).</param>
/// <param name="MachineIds">Optional restriction to a subset of AOI machines.</param>
/// <param name="ProductIds">Optional restriction to a subset of products.</param>
/// <param name="IncludeObsoleteBits">
/// When grouping by <see cref="DpmoGroupBy.Defect"/>, whether to emit
/// rows for bits flagged obsolete by
/// <see cref="DefectBitDecoder.All"/>. Defaults to <c>false</c> — the
/// modern UI hides obsolete columns by default.
/// </param>
/// <param name="SkipExclusion">
/// <see cref="SkipExclusion.Raw"/> (default) counts every board;
/// <see cref="SkipExclusion.Clean"/> excludes skipped / empty boards
/// from both the opportunity denominator and the defect numerator.
/// </param>
/// <param name="SkipConfig">
/// Skip-classification thresholds used when
/// <paramref name="SkipExclusion"/> is <see cref="SkipExclusion.Clean"/>.
/// <c>null</c> uses <see cref="SkipClassificationConfig.Default"/>.
/// </param>
/// <param name="SkipStatuses">
/// Optional positive narrowing filter on the computed per-board
/// <see cref="SkipClass"/>: when non-empty, only boards whose class is
/// in the set are counted (both denominator and numerator). Composes
/// with <paramref name="SkipExclusion"/> — a board must satisfy both.
/// <c>null</c> / empty applies no status narrowing. Requires the same
/// per-board classification as <see cref="SkipExclusion.Clean"/>.
/// </param>
/// <param name="ExcludeNogo">
/// When <c>true</c>, drops every product whose name contains "NOGO"
/// (case-insensitive) from both the opportunity denominator and the
/// defect numerator. NOGO boards are known-defect calibration coupons
/// run at changeover and normally must not skew production KPIs.
/// </param>
public sealed record DpmoTableFilter(
    DateRange Window,
    DpmoGroupBy GroupBy,
    DpmoNumerator Numerator,
    DpmoOpportunity Opportunity,
    IReadOnlyCollection<int>? MachineIds = null,
    IReadOnlyCollection<int>? ProductIds = null,
    bool IncludeObsoleteBits = false,
    SkipExclusion SkipExclusion = SkipExclusion.Raw,
    SkipClassificationConfig? SkipConfig = null,
    IReadOnlyCollection<SkipClass>? SkipStatuses = null,
    bool ExcludeNogo = false);

/// <summary>
/// DPMO counts for a single scope (row-level or grand total).
/// Numerator/denominator are exposed alongside the derived
/// <see cref="DpmoPpm"/> so a UI can display raw counts next to the
/// ratio (per Vieweb §3.1.6.5 layout).
/// </summary>
/// <param name="TestedObjectCount">Every tested-object row seen (before opportunity filter).</param>
/// <param name="OpportunityCount">Denominator: rows retained by the opportunity filter.</param>
/// <param name="DefectBitCount">Numerator: sum of set bits (per <see cref="DpmoNumerator"/>).</param>
/// <param name="DpmoPpm">
/// <c>1e6 · DefectBitCount / OpportunityCount</c>. Returns <c>0</c>
/// when <see cref="OpportunityCount"/> is zero (avoids
/// divide-by-zero divergence when merging buckets).
/// </param>
public sealed record DpmoKpi(
    long TestedObjectCount,
    long OpportunityCount,
    long DefectBitCount,
    double DpmoPpm);

/// <summary>
/// One row of a DPMO table result. <see cref="GroupKey"/> is a string
/// discriminator (numeric ids are stringified) so a single DTO can
/// carry every axis; <see cref="GroupName"/> is the human-readable
/// label. Both are <c>null</c> for rows that fell into the
/// "unassigned" bucket for a nullable axis (e.g. tested objects with
/// no PART_NUMBER join).
/// </summary>
public sealed record DpmoTableRow(
    string? GroupKey,
    string? GroupName,
    DpmoKpi Kpi);

/// <summary>
/// Result of running <see cref="DpmoTableReport"/>. Rows are sorted
/// descending by <see cref="DpmoKpi.DpmoPpm"/> (worst offenders
/// first — matches how a line engineer reads a Vieweb DPMO table)
/// with ties broken by <see cref="DpmoTableRow.GroupKey"/> for
/// stable snapshots.
/// </summary>
public sealed record DpmoTableResult(
    SourceDescriptor Source,
    DateRange Window,
    DpmoGroupBy GroupBy,
    DpmoNumerator Numerator,
    DpmoOpportunity Opportunity,
    DpmoKpi Overall,
    IReadOnlyList<DpmoTableRow> Rows,
    SkipExclusion SkipExclusion = SkipExclusion.Raw,
    long SkipExcludedCards = 0);
