using Nieweb.DataSources;
using Nieweb.Reports.Common;

namespace Nieweb.Reports;

/// <summary>
/// Metric that <see cref="TrendChartReport"/> can plot along the time
/// axis (CR3 in docs/phase-2.md §7.3). A single report run may
/// request any subset of these metrics; each becomes one series in
/// <see cref="TrendResult.Series"/> with one value per bucket in
/// <see cref="TrendResult.Buckets"/>. Numeric parity with the
/// stand-alone FPY and DPMO reports is guaranteed because the
/// underlying accumulators are identical.
/// </summary>
public enum TrendMetric
{
    /// <summary>
    /// FPY (AOI) = <c>100 · GoodAoi / Inspected</c> per bucket.
    /// Panel-level; requires panel streaming. Unit: percent.
    /// </summary>
    FpyAoi = 0,

    /// <summary>
    /// FPY (Diagnostic) = <c>100 · (GoodAoi + GoodDummyOnly) / Inspected</c>
    /// per bucket. Panel-level. Unit: percent.
    /// </summary>
    FpyDiagnostic = 1,

    /// <summary>
    /// FPY (After Repair) = <c>100 · (GoodAoi + GoodDummyOnly + GoodRepaired) / Inspected</c>
    /// per bucket. Panel-level. Unit: percent.
    /// </summary>
    FpyAfterRepair = 2,

    /// <summary>
    /// DPMO computed on AOI-flagged defects
    /// (<see cref="DpmoNumerator.Aoi"/>). Unit: ppm.
    /// </summary>
    DpmoAoi = 3,

    /// <summary>
    /// DPMO computed on post-review real defects
    /// (<see cref="DpmoNumerator.Real"/>). Unit: ppm.
    /// </summary>
    DpmoReal = 4,

    /// <summary>
    /// DPMO computed on false-call / dummy defects
    /// (<see cref="DpmoNumerator.Dummy"/>). Unit: ppm.
    /// </summary>
    DpmoDummy = 5,

    /// <summary>
    /// Absolute panel count in the bucket. Unit: count.
    /// </summary>
    PanelCount = 6,

    /// <summary>
    /// Absolute board / sub-panel count in the bucket. Unit: count.
    /// </summary>
    BoardCount = 7,

    /// <summary>
    /// Absolute defect count (using the requested
    /// <see cref="TrendFilter.Numerator"/>). Unit: count.
    /// </summary>
    DefectCount = 8,

    /// <summary>
    /// Cp = <c>(USL - LSL) / (6 · sample stddev)</c> on the requested
    /// <see cref="TrendFilter.DeviationAxis"/>. Requires BOTH
    /// <see cref="TrendFilter.LowerTolerance"/> and
    /// <see cref="TrendFilter.UpperTolerance"/> to be set. Unit: unitless.
    /// </summary>
    Cp = 9,

    /// <summary>
    /// Cpk = <c>min((USL - mean) / (3σ), (mean - LSL) / (3σ))</c> on
    /// the requested <see cref="TrendFilter.DeviationAxis"/>. Accepts
    /// one-sided tolerance (only one bound supplied); Cpk then falls
    /// back to the single-sided ratio. Unit: unitless.
    /// </summary>
    Cpk = 10,
}

/// <summary>
/// Filter accepted by <see cref="TrendChartReport"/>.
/// </summary>
/// <param name="Window">Half-open UTC time window over <c>Panel_Numeric_Date</c>.</param>
/// <param name="Bucket">
/// Time-bucket size for the X axis. Any <see cref="TimeBucket"/>
/// value; <see cref="TimeBucket.Shift"/> also requires
/// <see cref="Shifts"/>.
/// </param>
/// <param name="Metrics">
/// Set of metrics to compute. Order in this collection determines the
/// order of <see cref="TrendResult.Series"/>. Duplicates are
/// deduplicated on the fly.
/// </param>
/// <param name="Numerator">
/// Numerator used by <see cref="TrendMetric.DefectCount"/> (Aoi /
/// Real / Dummy). The three DPMO metrics carry their own numerator
/// implicitly.
/// </param>
/// <param name="Opportunity">
/// Which tested-object kinds count as opportunities for the DPMO /
/// DefectCount / Cp / Cpk metrics. Panel and Board metrics ignore
/// this.
/// </param>
/// <param name="DeviationAxis">
/// Deviation dimension used by <see cref="TrendMetric.Cp"/> /
/// <see cref="TrendMetric.Cpk"/>. Required when either metric is
/// requested; ignored otherwise.
/// </param>
/// <param name="LowerTolerance">
/// Lower spec limit for Cp / Cpk. Same units as the chosen
/// <see cref="DeviationAxis"/> (µm for X / Y / Thickness, degrees for
/// Theta, unitless ratio for Surface). Both bounds are required for
/// Cp; Cpk accepts one-sided.
/// </param>
/// <param name="UpperTolerance">Upper spec limit for Cp / Cpk (see <paramref name="LowerTolerance"/>).</param>
/// <param name="MachineIds">DB-level filter on parent panel's <c>Machine_Id</c>.</param>
/// <param name="ProductIds">DB-level filter on parent panel's <c>Product_Id</c>.</param>
/// <param name="Topologies">In-memory narrowing filter on <c>TESTED_OBJECT.Topology</c>.</param>
/// <param name="PartNumbers">In-memory narrowing filter on <c>PART_NUMBER.Part_Number</c>.</param>
/// <param name="JedecNames">In-memory narrowing filter on <c>JEDEC.Jedec_Name</c>.</param>
/// <param name="SiteTimeZone">Time zone used for bucket alignment. Defaults to UTC.</param>
/// <param name="Shifts">
/// Required when <paramref name="Bucket"/> is
/// <see cref="TimeBucket.Shift"/>; ignored otherwise.
/// </param>
/// <param name="OnlyLastInspection">
/// When <c>true</c> (default) and the source supports it, restricts
/// panel-level metrics to each panel's latest inspection. Ignored on
/// sources without <see cref="Capabilities.IsLastInspectionFilter"/>.
/// </param>
public sealed record TrendFilter(
    DateRange Window,
    TimeBucket Bucket,
    IReadOnlyCollection<TrendMetric> Metrics,
    DpmoNumerator Numerator = DpmoNumerator.Real,
    DpmoOpportunity Opportunity = DpmoOpportunity.All,
    DeviationAxis? DeviationAxis = null,
    double? LowerTolerance = null,
    double? UpperTolerance = null,
    IReadOnlyCollection<int>? MachineIds = null,
    IReadOnlyCollection<int>? ProductIds = null,
    IReadOnlyCollection<string>? Topologies = null,
    IReadOnlyCollection<string>? PartNumbers = null,
    IReadOnlyCollection<string>? JedecNames = null,
    TimeZoneInfo? SiteTimeZone = null,
    ShiftDefinition? Shifts = null,
    bool OnlyLastInspection = true);

/// <summary>
/// Series-level metadata for one trend metric. One
/// <see cref="TrendSeries"/> per requested
/// <see cref="TrendFilter.Metrics"/> entry, in the same order.
/// </summary>
/// <param name="Metric">Which metric this series represents.</param>
/// <param name="DisplayName">Human-facing name (e.g. <c>"FPY (AOI)"</c>).</param>
/// <param name="Unit">
/// Axis unit hint for the chart: <c>"%"</c>, <c>"ppm"</c>,
/// <c>"count"</c>, or empty string for the unitless Cp / Cpk.
/// </param>
public sealed record TrendSeries(
    TrendMetric Metric,
    string DisplayName,
    string Unit);

/// <summary>
/// One decomposed time-window slice plus every requested metric's
/// value in that slice. Metrics with insufficient data in a bucket
/// (zero panels for FPY, zero opportunities for DPMO, fewer than two
/// deviation samples for Cp / Cpk) emit <c>null</c> — the chart
/// draws a gap rather than a misleading 0.
/// </summary>
/// <param name="Label">Bucket label as produced by <see cref="TimeBucketRange.Label"/>.</param>
/// <param name="StartUtc">Inclusive lower bound in UTC.</param>
/// <param name="EndUtcExclusive">Exclusive upper bound in UTC.</param>
/// <param name="ShiftIndex">
/// Zero-based shift index when the bucket kind is
/// <see cref="TimeBucket.Shift"/>; <c>null</c> otherwise.
/// </param>
/// <param name="Values">
/// Per-metric value in the bucket. The key set matches
/// <see cref="TrendResult.Series"/>; missing values are <c>null</c>.
/// </param>
public sealed record TrendBucketPoint(
    string Label,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtcExclusive,
    int? ShiftIndex,
    IReadOnlyDictionary<TrendMetric, double?> Values);

/// <summary>
/// Snapshot of the narrowing filters actually applied by
/// <see cref="TrendChartReport"/>. Echoed on the result so a UI can
/// render a breadcrumb after a drill.
/// </summary>
public sealed record TrendAppliedFilters(
    IReadOnlyCollection<int>? MachineIds,
    IReadOnlyCollection<int>? ProductIds,
    IReadOnlyCollection<string>? Topologies,
    IReadOnlyCollection<string>? PartNumbers,
    IReadOnlyCollection<string>? JedecNames);

/// <summary>
/// Result of running <see cref="TrendChartReport"/>. Buckets are
/// chronological (matching <see cref="TimeBucketer.Decompose"/>
/// output). Series ordering matches
/// <see cref="TrendFilter.Metrics"/>.
/// </summary>
/// <param name="Source">Descriptor of the AOI source the report ran against.</param>
/// <param name="Window">Echoed query window.</param>
/// <param name="Bucket">Echoed bucket size.</param>
/// <param name="Numerator">Echoed numerator (used by DefectCount).</param>
/// <param name="Opportunity">Echoed opportunity kind.</param>
/// <param name="DeviationAxis">Echoed deviation axis (only meaningful when Cp / Cpk are requested).</param>
/// <param name="LowerTolerance">Echoed lower tolerance.</param>
/// <param name="UpperTolerance">Echoed upper tolerance.</param>
/// <param name="AppliedFilters">Echoed narrowing filters.</param>
/// <param name="Series">Series metadata (one per requested metric).</param>
/// <param name="Buckets">Per-bucket values.</param>
public sealed record TrendResult(
    SourceDescriptor Source,
    DateRange Window,
    TimeBucket Bucket,
    DpmoNumerator Numerator,
    DpmoOpportunity Opportunity,
    DeviationAxis? DeviationAxis,
    double? LowerTolerance,
    double? UpperTolerance,
    TrendAppliedFilters AppliedFilters,
    IReadOnlyList<TrendSeries> Series,
    IReadOnlyList<TrendBucketPoint> Buckets);
