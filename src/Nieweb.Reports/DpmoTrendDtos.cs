using Nieweb.DataSources;
using Nieweb.Reports.Common;
using Nieweb.Reports.Common.Skips;

namespace Nieweb.Reports;

/// <summary>
/// Filter accepted by <see cref="DpmoTrendByLineReport"/>: a DPMO-over-time
/// breakdown, one series per AOI line, bucketed by day or week.
/// </summary>
/// <param name="Window">Half-open UTC window over <c>Panel_Numeric_Date</c>.</param>
/// <param name="Bucket">
/// Time-bucket size. Only <see cref="TimeBucket.Day"/> and
/// <see cref="TimeBucket.Week"/> are accepted; other values throw.
/// </param>
/// <param name="SiteTimeZone">
/// Wall-clock zone used to align day / week boundaries. <c>null</c> = UTC.
/// </param>
/// <param name="Opportunity">
/// Which tested-object kinds count as opportunities. Changing this changes
/// both the denominator and which objects contribute defects, so the client
/// must refetch — unlike the numerator flavour, which every cell already
/// carries (see <see cref="DpmoTrendKpi"/>).
/// <para>
/// <see cref="DpmoOpportunity.Paste"/> is accepted but produces an empty
/// trend on post-reflow sources: paste is a pre-reflow stage, so
/// <c>Nb_Of_Tests_On_Pads</c> only exists where the source advertises
/// <see cref="Capabilities.PastePrintMetrics"/>.
/// </para>
/// </param>
/// <param name="MachineIds">Optional restriction to a subset of AOI machines.</param>
/// <param name="ProductIds">Optional restriction to a subset of products.</param>
/// <param name="SkipExclusion">
/// <see cref="SkipExclusion.Clean"/> (default) drops skipped / empty boards
/// from BOTH the denominator and the numerator;
/// <see cref="SkipExclusion.Raw"/> counts every board.
/// </param>
/// <param name="SkipConfig">Skip-classification thresholds (null = default).</param>
/// <param name="SkipStatuses">Optional positive narrowing on the per-board skip class.</param>
/// <param name="ExcludeNogo">Drop products whose name contains "NOGO".</param>
public sealed record DpmoTrendFilter(
    DateRange Window,
    TimeBucket Bucket,
    TimeZoneInfo? SiteTimeZone = null,
    DpmoOpportunity Opportunity = DpmoOpportunity.Components,
    IReadOnlyCollection<int>? MachineIds = null,
    IReadOnlyCollection<int>? ProductIds = null,
    SkipExclusion SkipExclusion = SkipExclusion.Clean,
    SkipClassificationConfig? SkipConfig = null,
    IReadOnlyCollection<SkipClass>? SkipStatuses = null,
    bool ExcludeNogo = false);

/// <summary>
/// One time bucket on the trend X-axis. <see cref="Label"/> is the
/// <see cref="TimeBucketRange.Label"/> (e.g. <c>"2026-07-21"</c> for a day,
/// <c>"2026-W30"</c> for an ISO week).
/// </summary>
public sealed record DpmoTrendBucket(
    int Index,
    string Label,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtcExclusive);

/// <summary>
/// DPMO for one (line, bucket) cell. Carries the shared opportunity
/// denominator plus <b>all three</b> defect numerators, so a client can
/// toggle AOI / Real / Dummy without a refetch — the same trick
/// <see cref="FpyKpi"/> plays for the three FPY flavours.
/// </summary>
/// <remarks>
/// Counts are accumulated as <see cref="long"/> and the ratios are computed
/// only here, at emit time. That count-first / divide-last discipline is what
/// makes a week bucket equal the sum of its days, and is the direct fix for
/// Vieweb bug <b>#12421</b>.
/// </remarks>
/// <param name="OpportunityCount">
/// Sum of the AOI's own per-board inspection test counts
/// (<c>CARDS.Nb_Of_Tests_On_Comp</c>, plus <c>_On_Pads</c> when the
/// opportunity flavour is <see cref="DpmoOpportunity.All"/> and the source
/// records paste metrics). Never a <c>TESTED_OBJECT</c> row count.
/// </param>
/// <param name="DefectsAoi">Bits set in <c>Error_Table</c> (raw AOI verdict).</param>
/// <param name="DefectsReal">Bits set in <c>Error_Table_AR</c> (post-review).</param>
/// <param name="DefectsDummy">Bits in <c>Error_Table</c> cleared in <c>Error_Table_AR</c> (false calls).</param>
public sealed record DpmoTrendKpi(
    long OpportunityCount,
    long DefectsAoi,
    long DefectsReal,
    long DefectsDummy)
{
    /// <summary>DPMO for the raw AOI verdict.</summary>
    public double DpmoAoi => Rate(DefectsAoi);

    /// <summary>DPMO for post-review real defects.</summary>
    public double DpmoReal => Rate(DefectsReal);

    /// <summary>DPMO for dummy / false calls.</summary>
    public double DpmoDummy => Rate(DefectsDummy);

    private double Rate(long defects) =>
        OpportunityCount == 0 ? 0d : 1_000_000d * defects / OpportunityCount;
}

/// <summary>
/// One line's DPMO for one bucket. Buckets in which the line inspected
/// nothing are omitted (a gap in the trend) rather than emitted as zero,
/// so an idle shift does not read as a perfect one.
/// </summary>
public sealed record DpmoTrendPoint(
    int BucketIndex,
    DpmoTrendKpi Kpi);

/// <summary>One AOI line's trend series across the buckets.</summary>
/// <param name="MachineId">Machine primary key within the owning source.</param>
/// <param name="MachineName">Display name, or <c>null</c> when absent from the catalogue.</param>
/// <param name="Points">Per-bucket KPI, ascending by bucket index; gaps omitted.</param>
/// <param name="Overall">KPI across the whole window for this line.</param>
public sealed record DpmoTrendLine(
    int MachineId,
    string? MachineName,
    IReadOnlyList<DpmoTrendPoint> Points,
    DpmoTrendKpi Overall);

/// <summary>
/// Result of running <see cref="DpmoTrendByLineReport"/> against ONE source.
/// The API aggregates one of these per source so the SPA can render both
/// pre- and post-reflow lines side by side. Machine ids collide across
/// sources (the same numeric id is a different physical line in each DB), so
/// every line stays namespaced by its <see cref="Source"/> and must never be
/// merged by id.
/// </summary>
public sealed record DpmoTrendResult(
    SourceDescriptor Source,
    DateRange Window,
    TimeBucket Bucket,
    DpmoOpportunity Opportunity,
    SkipExclusion SkipExclusion,
    IReadOnlyList<DpmoTrendBucket> Buckets,
    IReadOnlyList<DpmoTrendLine> Lines,
    long SkipExcludedCards = 0);
