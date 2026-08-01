using Nieweb.DataSources;
using Nieweb.Reports.Common;
using Nieweb.Reports.Common.Skips;

namespace Nieweb.Reports;

/// <summary>
/// Which of the three FPY flavours a presentation layer (chart / PDF) shows.
/// The report always computes all three (see <see cref="FpyKpi"/>); this only
/// selects which one is plotted / highlighted.
/// </summary>
public enum FpyFlavor
{
    /// <summary>Raw AOI first-pass good (status = 1).</summary>
    Aoi = 0,

    /// <summary>After-review good, false calls removed (status ∈ {1, 2}).</summary>
    Diagnostic = 1,

    /// <summary>After-repair good (status ∈ {1, 2, 3}).</summary>
    AfterRepair = 2,
}

/// <summary>
/// Filter accepted by <see cref="FpyTrendByLineReport"/>: an FPY-over-time
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
/// <param name="Granularity">
/// Panel-level or sub-panel (board) level FPY. Defaults to
/// <see cref="FpyGranularity.Board"/> (sub-panel).
/// </param>
/// <param name="MachineIds">Optional restriction to a subset of AOI machines.</param>
/// <param name="ProductIds">Optional restriction to a subset of products.</param>
/// <param name="OnlyLastInspection">Restrict to the last inspection of each panel when supported.</param>
/// <param name="SkipExclusion">
/// <see cref="SkipExclusion.Clean"/> (default) drops skipped / empty boards;
/// <see cref="SkipExclusion.Raw"/> counts every panel / board.
/// </param>
/// <param name="SkipConfig">Skip-classification thresholds (null = default).</param>
/// <param name="SkipStatuses">Optional positive narrowing on the per-board skip class.</param>
/// <param name="ExcludeNogo">Drop products whose name contains "NOGO".</param>
public sealed record FpyTrendFilter(
    DateRange Window,
    TimeBucket Bucket,
    TimeZoneInfo? SiteTimeZone = null,
    FpyGranularity Granularity = FpyGranularity.Board,
    IReadOnlyCollection<int>? MachineIds = null,
    IReadOnlyCollection<int>? ProductIds = null,
    bool OnlyLastInspection = true,
    SkipExclusion SkipExclusion = SkipExclusion.Clean,
    SkipClassificationConfig? SkipConfig = null,
    IReadOnlyCollection<SkipClass>? SkipStatuses = null,
    bool ExcludeNogo = false);

/// <summary>
/// One time bucket on the trend X-axis. <see cref="Label"/> is the
/// <see cref="TimeBucketRange.Label"/> (e.g. <c>"2026-07-21"</c> for a day,
/// <c>"2026-W30"</c> for an ISO week).
/// </summary>
public sealed record FpyTrendBucket(
    int Index,
    string Label,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtcExclusive);

/// <summary>
/// One line's FPY for one bucket. <see cref="FpyKpi"/> carries all three
/// flavours (AOI / Diagnostic / After Repair) so the client can toggle
/// between them without a refetch. Buckets in which the line produced no
/// panels are omitted (a gap in the trend).
/// </summary>
public sealed record FpyTrendPoint(
    int BucketIndex,
    FpyKpi Kpi);

/// <summary>One AOI line's trend series across the buckets.</summary>
/// <param name="MachineId">Machine primary key within the owning source.</param>
/// <param name="MachineName">Display name, or <c>null</c> when absent from the catalogue.</param>
/// <param name="Points">Per-bucket KPI, ascending by bucket index; gaps omitted.</param>
/// <param name="Overall">KPI across the whole window for this line.</param>
public sealed record FpyTrendLine(
    int MachineId,
    string? MachineName,
    IReadOnlyList<FpyTrendPoint> Points,
    FpyKpi Overall);

/// <summary>
/// Result of running <see cref="FpyTrendByLineReport"/> against ONE source.
/// The API aggregates one of these per source so the SPA can render both
/// pre- and post-reflow lines side by side (machine ids collide across
/// sources, so every line stays namespaced by its <see cref="Source"/>).
/// </summary>
public sealed record FpyTrendResult(
    SourceDescriptor Source,
    DateRange Window,
    TimeBucket Bucket,
    FpyGranularity Granularity,
    SkipExclusion SkipExclusion,
    IReadOnlyList<FpyTrendBucket> Buckets,
    IReadOnlyList<FpyTrendLine> Lines,
    long SkipExcludedRows = 0);
