using Nieweb.DataSources;
using Nieweb.Filters;
using Nieweb.Reports.Common;
using Nieweb.Reports.Common.Skips;

namespace Nieweb.Reports;

/// <summary>
/// Category axis a Pareto chart groups on. All six axes share the same
/// data source (<c>TESTED_OBJECT</c>) and are stateless: drilling from
/// one axis to another is expressed by combining
/// <see cref="ParetoFilter.Axis"/> with any of the narrowing filter
/// collections on <see cref="ParetoFilter"/> — no server-side session
/// state is required.
/// </summary>
public enum ParetoAxis
{
    /// <summary>One bar per <see cref="Nieweb.Reports.Common.Defects.DefectBit"/>.</summary>
    Defect = 0,

    /// <summary>One bar per <c>Product_Id</c>.</summary>
    Product = 1,

    /// <summary>One bar per <c>Machine_Id</c>.</summary>
    AoiMachine = 2,

    /// <summary>One bar per <c>TESTED_OBJECT.Topology</c> (reference designator).</summary>
    ReferenceDesignator = 3,

    /// <summary>One bar per <c>PART_NUMBER</c> name.</summary>
    PartNumber = 4,

    /// <summary>One bar per <c>JEDEC</c> / package name.</summary>
    Jedec = 5,

    /// <summary>
    /// One bar per local-calendar day of the requested window (see
    /// <see cref="ParetoFilter.SiteTimeZone"/>). Bucket keys are the
    /// ISO date <c>yyyy-MM-dd</c>. Days with no matching tested-object
    /// rows are omitted from the output — the Pareto chart itself
    /// does not draw gaps.
    /// </summary>
    Day = 6,

    /// <summary>
    /// One bar per production-shift instance in the requested window.
    /// Requires <see cref="ParetoFilter.Shifts"/>. Because shifts wrap
    /// around midnight (see <see cref="ShiftDefinition"/>), a single
    /// 24-hour window produces up to <c>Shifts.Starts.Length</c>
    /// rows. Bucket keys embed the shift-start date and label so
    /// same-name shifts on different days remain distinct.
    /// </summary>
    Shift = 7,
}

/// <summary>
/// Weight applied to each defect when computing bar heights and the
/// cumulative-percent line. <see cref="Count"/> is the boss-approved
/// default (volume-weighted); <see cref="Dpmo"/> / <see cref="Ppm"/>
/// switch to a rate view — the "scale toggle" from docs/phase-2.md
/// §7.3 CR1.
/// </summary>
/// <remarks>
/// <para>
/// Under <see cref="Count"/>, absolute defect count scales with
/// production volume, so a low-rate defect on a high-volume product
/// correctly outranks a high-rate defect on a low-volume product.
/// Under <see cref="Dpmo"/> / <see cref="Ppm"/> that ordering flips —
/// the ranking becomes rate-driven and low-volume outliers can lead
/// the chart. Line engineers toggle deliberately.
/// </para>
/// <para>
/// <see cref="Ppm"/> is a display alias for <see cref="Dpmo"/> — the
/// math is identical (both compute <c>1e6 · defects / opportunities</c>).
/// The distinction is preserved so localised UI can say "PPM"
/// (parts-per-million, familiar to component-quality engineers)
/// rather than "DPMO" (defects-per-million-opportunities, familiar
/// to process-quality engineers). Row values are byte-identical.
/// </para>
/// <para>
/// A future severity/cost weight will land as an additional enum
/// value backed by an internal <c>DefectWeight</c> table. Adding a
/// new value must be paired with wiring the metric into
/// <see cref="ParetoRow.WeightedScore"/> inside
/// <see cref="ParetoReport"/>.
/// </para>
/// </remarks>
public enum ParetoWeight
{
    /// <summary>
    /// Each defect contributes 1.0. Bar height = absolute defect count
    /// = the boss-approved volume-weighted ranking.
    /// </summary>
    Count = 0,

    /// <summary>
    /// Bar height = <c>1e6 · defect count / opportunity count</c>
    /// (defects per million opportunities). Rate-view ranking; the
    /// sort order flips relative to <see cref="Count"/>. On axes with
    /// no per-group denominator the report applies <see cref="Count"/>
    /// instead and echoes that applied weight. A true zero denominator
    /// on a supported axis still emits <c>0</c>.
    /// </summary>
    Dpmo = 1,

    /// <summary>
    /// Bar height = <c>1e6 · defect count / opportunity count</c>,
    /// numerically identical to <see cref="Dpmo"/>. Preserved as a
    /// distinct enum value so component-quality reports can render
    /// "PPM" in the UI without owning a separate report definition.
    /// </summary>
    Ppm = 2,
}

/// <summary>
/// Filter accepted by <see cref="ParetoReport"/>. Every narrowing
/// collection is optional; multiple collections combine as a logical
/// AND, so the client drills any depth by adding one more filter
/// value per call (e.g. call 1: <c>Axis=Defect</c>; call 2:
/// <c>Axis=PartNumber, DefectBits=[1]</c>; call 3:
/// <c>Axis=ReferenceDesignator, DefectBits=[1], PartNumbers=["PN-A"]</c>).
/// </summary>
/// <param name="Window">Half-open UTC time window over <c>Panel_Numeric_Date</c>.</param>
/// <param name="Axis">Primary category axis.</param>
/// <param name="Numerator">
/// Which defect bits count. Default is
/// <see cref="DpmoNumerator.Real"/> — post-review defects — because
/// that's what improvement decisions should target. Toggle to
/// <see cref="DpmoNumerator.Aoi"/> when gauging AOI inspection burden
/// or to <see cref="DpmoNumerator.Dummy"/> when hunting false-call
/// programme quality.
/// </param>
/// <param name="Opportunity">Which tested-object kinds count as opportunities.</param>
/// <param name="Weight">
/// Bar-height metric. <see cref="ParetoWeight.Count"/> is the
/// volume-weighted default; <see cref="ParetoWeight.Dpmo"/> and
/// <see cref="ParetoWeight.Ppm"/> switch to the rate view.
/// </param>
/// <param name="TopN">
/// Cap on the number of visible rows. When set and there are more
/// buckets than <see cref="TopN"/>, the surplus is rolled into
/// <see cref="ParetoResult.OthersBucket"/> if
/// <see cref="IncludeOthersBucket"/> is true.
/// </param>
/// <param name="IncludeOthersBucket">
/// When true (default), rows past <see cref="TopN"/> collapse into a
/// single "Others" row so cumulative-% still sums to 100.
/// </param>
/// <param name="VitalFewThresholdPercent">
/// Cumulative-% threshold that separates the "vital few" from the
/// "trivial many". Defaults to the classic Pareto 80.0.
/// </param>
/// <param name="IncludeObsoleteBits">
/// When <see cref="Axis"/> is <see cref="ParetoAxis.Defect"/>,
/// whether to emit rows for bits flagged obsolete by the defect
/// catalogue. Default <c>false</c> — matches the DPMO table.
/// </param>
/// <param name="MachineIds">DB-level filter on parent panel's <c>Machine_Id</c>.</param>
/// <param name="ProductIds">DB-level filter on parent panel's <c>Product_Id</c>.</param>
/// <param name="DefectBits">
/// In-memory narrowing filter: only tested-object rows that have at
/// least one of these bits set in the chosen numerator field
/// contribute. Values are 1-based bit numbers matching
/// <see cref="Nieweb.Reports.Common.Defects.DefectBit"/>.
/// </param>
/// <param name="Topologies">
/// In-memory narrowing filter on <c>TESTED_OBJECT.Topology</c> (reference designator).
/// Match is ordinal-case-sensitive to preserve historical row identity.
/// </param>
/// <param name="PartNumbers">In-memory narrowing filter on <c>PART_NUMBER.Part_Number</c>.</param>
/// <param name="JedecNames">In-memory narrowing filter on <c>JEDEC.Jedec_Name</c>.</param>
/// <param name="SiteTimeZone">
/// Time zone used to bucket <c>Panel_Numeric_Date</c> when
/// <see cref="Axis"/> is <see cref="ParetoAxis.Day"/> or
/// <see cref="ParetoAxis.Shift"/>. When <c>null</c> the report falls
/// back to UTC — matching how <c>Panel_Numeric_Date</c> is stored in
/// the Superviseur DB — so shipping without a site time zone still
/// produces reproducible buckets.
/// </param>
/// <param name="Shifts">
/// Production-shift schedule used when <see cref="Axis"/> is
/// <see cref="ParetoAxis.Shift"/>. Required for that axis and
/// ignored otherwise; the report throws
/// <see cref="System.ArgumentException"/> when Shift is requested
/// without a definition.
/// </param>
/// <param name="Filters">
/// Optional Vieweb-style generic operator filter (reference designator,
/// part number, package, product, AOI machine, defect). Applied in memory
/// via <see cref="FilterEvaluator"/> after the DB-level window / machine /
/// product filters, so every operator (Like / Not like / In / Not in /
/// Between / &lt;= / &gt;=) narrows the streamed rows. <c>null</c> or empty
/// matches every row.
/// </param>
/// <param name="SkipExclusion">
/// Whether to exclude "skipped" boards (manual X-OUT, machine-flagged,
/// heuristic-missing) from the Pareto. <see cref="Nieweb.Reports.SkipExclusion.Raw"/>
/// counts every board; <see cref="Nieweb.Reports.SkipExclusion.Clean"/> drops
/// defects on skipped boards. Matches the DPMO / FPY toggle.
/// </param>
/// <param name="SkipConfig">
/// Skip-classification thresholds (defaults to
/// <see cref="SkipClassificationConfig.Default"/> when <c>null</c>).
/// </param>
/// <param name="SkipStatuses">
/// Optional narrowing to specific <see cref="SkipClass"/> values (e.g.
/// "only ManualSkip + HeuristicMissing"). Combines with
/// <paramref name="SkipExclusion"/> as a logical AND.
/// </param>
/// <param name="ExcludeNogo">
/// When <c>true</c>, drops every product whose name contains "NOGO"
/// (case-insensitive) from both the opportunity denominator and the
/// defect numerator. NOGO boards are known-defect calibration coupons
/// run at changeover and normally must not skew production KPIs.
/// </param>
public sealed record ParetoFilter(
    DateRange Window,
    ParetoAxis Axis,
    DpmoNumerator Numerator = DpmoNumerator.Real,
    DpmoOpportunity Opportunity = DpmoOpportunity.All,
    ParetoWeight Weight = ParetoWeight.Count,
    int? TopN = null,
    bool IncludeOthersBucket = true,
    double VitalFewThresholdPercent = 80.0,
    bool IncludeObsoleteBits = false,
    IReadOnlyCollection<int>? MachineIds = null,
    IReadOnlyCollection<int>? ProductIds = null,
    IReadOnlyCollection<int>? DefectBits = null,
    IReadOnlyCollection<string>? Topologies = null,
    IReadOnlyCollection<string>? PartNumbers = null,
    IReadOnlyCollection<string>? JedecNames = null,
    TimeZoneInfo? SiteTimeZone = null,
    ShiftDefinition? Shifts = null,
    FilterRequest? Filters = null,
    SkipExclusion SkipExclusion = SkipExclusion.Raw,
    SkipClassificationConfig? SkipConfig = null,
    IReadOnlyCollection<SkipClass>? SkipStatuses = null,
    bool ExcludeNogo = false);

/// <summary>
/// One row of a Pareto chart. <see cref="DefectCount"/> is the bar
/// height under <see cref="ParetoWeight.Count"/>; the remaining
/// fields tell the volume-context story that keeps a rate-only view
/// (DPMO) from misleading the reader.
/// </summary>
/// <param name="GroupKey">Stable machine-readable identifier for the bucket. <c>null</c> only on the synthetic Others row.</param>
/// <param name="GroupName">Human-readable label. <c>null</c> when the underlying reference row was missing.</param>
/// <param name="DefectCount">
/// Absolute count of set defect bits contributed by this bucket
/// (sum across every tested object in the bucket). This is what the
/// bars plot when <see cref="ParetoFilter.Weight"/> is
/// <see cref="ParetoWeight.Count"/>.
/// </param>
/// <param name="WeightedScore">
/// Bar height under the currently active <see cref="ParetoWeight"/>.
/// Equal to <see cref="DefectCount"/> for <see cref="ParetoWeight.Count"/>;
/// future severity/cost weights populate this differently. Rows are
/// sorted descending by this value.
/// </param>
/// <param name="OpportunityCount">
/// Card-derived opportunity count for this bucket
/// (<see cref="Nieweb.DataSources.CardRow.NbOfTestsOnComp"/> /
/// <see cref="Nieweb.DataSources.CardRow.NbOfTestsOnPads"/>) when
/// <see cref="OpportunitiesApplicable"/> is true. Zero means a real
/// empty denominator on a supported axis. When applicability is
/// false the number is compatibility padding (typically 0) and must
/// not be presented as a measured value.
/// </param>
/// <param name="OpportunitySharePercent">
/// <c>100 · OpportunityCount / TotalOpportunities</c>. Answers
/// "what fraction of production ran through this bucket?".
/// </param>
/// <param name="DpmoPpm">
/// <c>1e6 · DefectCount / OpportunityCount</c>. Rate-view metric.
/// Ranks the bar when <see cref="ParetoFilter.Weight"/> is
/// <see cref="ParetoWeight.Dpmo"/> or <see cref="ParetoWeight.Ppm"/>;
/// under <see cref="ParetoWeight.Count"/> it is a diagnostic only
/// and the ranking stays volume-weighted so a low-rate / high-volume
/// bucket correctly outranks a high-rate / low-volume bucket.
/// </param>
/// <param name="DefectSharePercent">
/// <c>100 · DefectCount / TotalDefectCount</c>. Height of this bar
/// as a fraction of total defect volume.
/// </param>
/// <param name="CumulativePercent">
/// Running sum of <see cref="DefectSharePercent"/> at this row's
/// position (sort order). The classic Pareto cumulative line.
/// </param>
/// <param name="IsVitalFew">
/// <c>true</c> when this row is part of the "vital few" — its
/// <see cref="CumulativePercent"/> at emit time is at or below
/// <see cref="ParetoFilter.VitalFewThresholdPercent"/> (or is the
/// first row that crosses it, so the boundary bar is always
/// included).
/// </param>
/// <param name="OpportunitiesApplicable">
/// Whether <see cref="OpportunityCount"/> and <see cref="DpmoPpm"/>
/// are a measured per-group denominator. False for reference
/// designator, part number, and JEDEC (no card-derived per-group
/// count). True for machine, product, day, shift, and defect
/// (defect uses the overall card denominator).
/// </param>
public sealed record ParetoRow(
    string? GroupKey,
    string? GroupName,
    long DefectCount,
    double WeightedScore,
    long OpportunityCount,
    double OpportunitySharePercent,
    double DpmoPpm,
    double DefectSharePercent,
    double CumulativePercent,
    bool IsVitalFew,
    bool OpportunitiesApplicable);

/// <summary>
/// Result of running <see cref="ParetoReport"/>. Rows are sorted
/// descending by <see cref="ParetoRow.WeightedScore"/> with ties broken
/// by <see cref="ParetoRow.GroupKey"/> for stable snapshots.
/// <see cref="OthersBucket"/> is non-null only when
/// <see cref="ParetoFilter.TopN"/> is set and there was overflow.
/// </summary>
/// <param name="Source">Descriptor of the AOI source the report ran against.</param>
/// <param name="Window">Echoed query window.</param>
/// <param name="Axis">Echoed axis.</param>
/// <param name="Numerator">Echoed numerator.</param>
/// <param name="Opportunity">Echoed opportunity kind.</param>
/// <param name="Weight">Echoed weight metric.</param>
/// <param name="AppliedFilters">
/// Snapshot of the narrowing filters that were actually applied
/// (echoing <see cref="ParetoFilter"/>'s in-memory narrowing
/// collections). Useful for breadcrumb rendering after a drill.
/// </param>
/// <param name="Overall">
/// DPMO-flavoured KPI over every row (before <see cref="ParetoFilter.TopN"/>
/// trimming). Provides denominator/numerator counts that any single
/// row's percentages divide into.
/// </param>
/// <param name="Rows">Visible bars, sorted descending by <see cref="ParetoRow.WeightedScore"/>.</param>
/// <param name="OthersBucket">
/// Non-null when <see cref="ParetoFilter.TopN"/> caused rows to be
/// collapsed. Its <see cref="ParetoRow.CumulativePercent"/> is always
/// 100.0. <see cref="ParetoRow.IsVitalFew"/> is always <c>false</c>
/// on the Others row.
/// </param>
/// <param name="SkipExclusion">Echoed skip-exclusion mode (Raw / Clean).</param>
/// <param name="SkipExcludedCards">
/// Count of sub-panels dropped by skip filtering (Clean mode and/or a
/// <see cref="ParetoFilter.SkipStatuses"/> narrowing). Zero when no skip
/// filtering was requested.
/// </param>
/// <param name="VitalFewThresholdPercent">
/// Echo of <see cref="ParetoFilter.VitalFewThresholdPercent"/> actually
/// used to flag <see cref="ParetoRow.IsVitalFew"/>.
/// </param>
public sealed record ParetoResult(
    SourceDescriptor Source,
    DateRange Window,
    ParetoAxis Axis,
    DpmoNumerator Numerator,
    DpmoOpportunity Opportunity,
    ParetoWeight Weight,
    ParetoAppliedFilters AppliedFilters,
    DpmoKpi Overall,
    IReadOnlyList<ParetoRow> Rows,
    ParetoRow? OthersBucket,
    SkipExclusion SkipExclusion = SkipExclusion.Raw,
    long SkipExcludedCards = 0,
    double VitalFewThresholdPercent = 80.0);

/// <summary>
/// Echo of every narrowing filter <see cref="ParetoReport"/> honoured
/// for a specific run. All collections are guaranteed non-<c>null</c>
/// (empty when the caller left the corresponding <see cref="ParetoFilter"/>
/// field <c>null</c>) so a client can drive breadcrumb UI without
/// null-checks.
/// </summary>
public sealed record ParetoAppliedFilters(
    IReadOnlyList<int> MachineIds,
    IReadOnlyList<int> ProductIds,
    IReadOnlyList<int> DefectBits,
    IReadOnlyList<string> Topologies,
    IReadOnlyList<string> PartNumbers,
    IReadOnlyList<string> JedecNames);
