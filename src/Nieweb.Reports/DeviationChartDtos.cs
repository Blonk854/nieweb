using Nieweb.DataSources;

namespace Nieweb.Reports;

/// <summary>
/// Per-tested-object deviation axis a
/// <see cref="DeviationChartReport"/> can histogram over. Each axis
/// maps onto exactly one field of <see cref="TestedObjectRow"/> and is
/// available on every source that materialises those fields (both
/// post-reflow <c>HLYAOI2024</c> and pre-reflow <c>MEAOI</c>).
/// </summary>
/// <remarks>
/// Component-only vs paste-only interpretation is decided by the
/// caller via <see cref="DeviationFilter.Opportunity"/> — on a
/// component the deviations are placement offsets; on a paste pad
/// they describe stencil-print alignment. The axes are the same;
/// the tolerance envelope isn't. See
/// <see cref="DeviationFilter.LowerTolerance"/> /
/// <see cref="DeviationFilter.UpperTolerance"/>.
/// </remarks>
public enum DeviationAxis
{
    /// <summary><c>Delta_X</c> in µm (X offset).</summary>
    DeltaX = 0,

    /// <summary><c>Delta_Y</c> in µm (Y offset).</summary>
    DeltaY = 1,

    /// <summary><c>Delta_Theta</c> in degrees (rotation offset).</summary>
    DeltaTheta = 2,

    /// <summary>
    /// <c>Delta_Thickness</c> in µm (Z / height deviation). Vieweb
    /// calls this "Z" in the deviation-chart axis selector.
    /// </summary>
    DeltaThickness = 3,

    /// <summary><c>Delta_Surface</c> — unitless surface ratio.</summary>
    DeltaSurface = 4,
}

/// <summary>
/// Filter accepted by <see cref="DeviationChartReport"/>. Every
/// narrowing collection is optional; multiple collections combine as
/// a logical AND.
/// </summary>
/// <param name="Window">Half-open UTC time window over <c>Panel_Numeric_Date</c>.</param>
/// <param name="Axis">Which deviation dimension to histogram.</param>
/// <param name="Opportunity">
/// Restricts the input population. Defaults to
/// <see cref="DpmoOpportunity.Components"/> — pastes and components
/// have different tolerance envelopes so mixing them in a single
/// histogram is almost always a mistake.
/// </param>
/// <param name="BinCount">
/// Number of histogram bins. Defaults to <c>40</c>. Must be
/// <c>&gt;= 1</c> and <c>&lt;= 500</c>.
/// </param>
/// <param name="LowerTolerance">
/// Optional lower tolerance overlay. When supplied, the report
/// echoes it back and counts rows that fall below it as out-of-tolerance.
/// Nieweb resolves this from
/// <c>AppParameter</c> keys (e.g. <c>tolerance.component.itx</c>) at
/// the endpoint layer so the report itself stays pure.
/// </param>
/// <param name="UpperTolerance">Symmetric partner of <see cref="LowerTolerance"/>.</param>
/// <param name="MachineIds">DB-level filter on parent panel's <c>Machine_Id</c>.</param>
/// <param name="ProductIds">DB-level filter on parent panel's <c>Product_Id</c>.</param>
/// <param name="Topologies">In-memory narrowing on <c>TESTED_OBJECT.Topology</c>.</param>
/// <param name="PartNumbers">In-memory narrowing on <c>PART_NUMBER.Part_Number</c>.</param>
/// <param name="JedecNames">In-memory narrowing on <c>JEDEC.Jedec_Name</c>.</param>
public sealed record DeviationFilter(
    DateRange Window,
    DeviationAxis Axis,
    DpmoOpportunity Opportunity = DpmoOpportunity.Components,
    int BinCount = 40,
    double? LowerTolerance = null,
    double? UpperTolerance = null,
    IReadOnlyCollection<int>? MachineIds = null,
    IReadOnlyCollection<int>? ProductIds = null,
    IReadOnlyCollection<string>? Topologies = null,
    IReadOnlyCollection<string>? PartNumbers = null,
    IReadOnlyCollection<string>? JedecNames = null);

/// <summary>
/// One bin of a deviation histogram.
/// </summary>
/// <param name="Index">0-based bin index (ascending along the axis).</param>
/// <param name="LowerBound">Inclusive lower bound (axis units).</param>
/// <param name="UpperBound">
/// Exclusive upper bound, except for the last bin which is inclusive
/// so the maximum sample lands somewhere.
/// </param>
/// <param name="Count">Row count in this bin.</param>
public sealed record DeviationBin(
    int Index,
    double LowerBound,
    double UpperBound,
    long Count);

/// <summary>
/// Echoed narrowing filters. Empty collections instead of <c>null</c>
/// so a client can drive breadcrumb UI without null-checks.
/// </summary>
public sealed record DeviationAppliedFilters(
    IReadOnlyList<int> MachineIds,
    IReadOnlyList<int> ProductIds,
    IReadOnlyList<string> Topologies,
    IReadOnlyList<string> PartNumbers,
    IReadOnlyList<string> JedecNames);

/// <summary>
/// Result of running <see cref="DeviationChartReport"/>.
/// </summary>
/// <param name="Source">Descriptor of the AOI source.</param>
/// <param name="Window">Echoed query window.</param>
/// <param name="Axis">Echoed axis.</param>
/// <param name="Opportunity">Echoed opportunity kind.</param>
/// <param name="AppliedFilters">Echoed narrowing filters.</param>
/// <param name="SampleCount">Rows contributing to the histogram (after every filter).</param>
/// <param name="Mean">Arithmetic mean of the axis over <see cref="SampleCount"/> rows.</param>
/// <param name="StdDev">
/// Sample standard deviation (n-1). Zero when
/// <see cref="SampleCount"/> &lt; 2.
/// </param>
/// <param name="Min">Smallest observed sample; <c>NaN</c> when <see cref="SampleCount"/> = 0.</param>
/// <param name="Max">Largest observed sample; <c>NaN</c> when <see cref="SampleCount"/> = 0.</param>
/// <param name="PlusThreeSigma">
/// <c>Mean + 3 * StdDev</c>. Rendered as an overlay line. <c>NaN</c>
/// when <see cref="SampleCount"/> &lt; 2.
/// </param>
/// <param name="MinusThreeSigma">Symmetric partner of <see cref="PlusThreeSigma"/>.</param>
/// <param name="LowerTolerance">Echoed lower tolerance (if any).</param>
/// <param name="UpperTolerance">Echoed upper tolerance (if any).</param>
/// <param name="OutOfToleranceCount">
/// Rows with a sample strictly below <see cref="LowerTolerance"/> or
/// strictly above <see cref="UpperTolerance"/>. Ignores missing bounds
/// (a one-sided tolerance still returns a count against that side only).
/// Zero when neither bound is set.
/// </param>
/// <param name="Bins">
/// Contiguous histogram bins ordered by <see cref="DeviationBin.Index"/>.
/// Always contains <c>BinCount</c> entries; empty bins carry
/// <c>Count = 0</c> so the client can render an even x-axis.
/// </param>
public sealed record DeviationResult(
    SourceDescriptor Source,
    DateRange Window,
    DeviationAxis Axis,
    DpmoOpportunity Opportunity,
    DeviationAppliedFilters AppliedFilters,
    long SampleCount,
    double Mean,
    double StdDev,
    double Min,
    double Max,
    double PlusThreeSigma,
    double MinusThreeSigma,
    double? LowerTolerance,
    double? UpperTolerance,
    long OutOfToleranceCount,
    IReadOnlyList<DeviationBin> Bins);
