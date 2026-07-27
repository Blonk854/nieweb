using Nieweb.DataSources;
using Nieweb.Reports.Common.Skips;

namespace Nieweb.Reports;

/// <summary>
/// Filter accepted by <see cref="SkipSummaryReport"/>.
/// </summary>
/// <param name="Window">Half-open UTC time window over <c>Panel_Numeric_Date</c>.</param>
/// <param name="MachineIds">Optional restriction to a subset of AOI machines.</param>
/// <param name="ProductIds">Optional restriction to a subset of products.</param>
/// <param name="OnlyLastInspection">
/// When <c>true</c> (default) and the source supports it, restricts to
/// the most recent inspection of each panel so re-inspected boards are
/// not double-counted. Sources without
/// <see cref="Capabilities.IsLastInspectionFilter"/> ignore it.
/// </param>
/// <param name="Config">
/// Skip-classification thresholds / button-label map. <c>null</c> uses
/// <see cref="SkipClassificationConfig.Default"/>.
/// </param>
public sealed record SkipSummaryFilter(
    DateRange Window,
    IReadOnlyCollection<int>? MachineIds = null,
    IReadOnlyCollection<int>? ProductIds = null,
    bool OnlyLastInspection = true,
    SkipClassificationConfig? Config = null);

/// <summary>
/// Card and component tallies for a single <see cref="SkipClass"/>.
/// </summary>
/// <param name="Class">The skip class this row counts.</param>
/// <param name="CardCount">Number of sub-panels classified as <see cref="Class"/>.</param>
/// <param name="ComponentCount">
/// Sum of <c>CARDS.Number_Of_Component</c> over those cards — the
/// production volume the class represents.
/// </param>
/// <param name="CardPercent">
/// <see cref="CardCount"/> as a percentage of the window's total cards
/// (0 when there are no cards).
/// </param>
public sealed record SkipClassCount(
    SkipClass Class,
    long CardCount,
    long ComponentCount,
    double CardPercent);

/// <summary>
/// Result of <see cref="SkipSummaryReport"/>: how many sub-panels in the
/// window were skipped and why, so an analyst can quantify the pollution
/// and target its reduction. <see cref="Classes"/> always carries one
/// row per <see cref="SkipClass"/> member (including
/// <see cref="SkipClass.None"/>) in enum order for a stable shape.
/// </summary>
public sealed record SkipSummaryResult(
    SourceDescriptor Source,
    DateRange Window,
    long TotalCards,
    long TotalComponents,
    long SkippedCards,
    double SkippedCardPercent,
    IReadOnlyList<SkipClassCount> Classes);
