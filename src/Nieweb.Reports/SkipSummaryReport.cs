using Nieweb.DataSources;
using Nieweb.Reports.Common.Skips;

namespace Nieweb.Reports;

/// <summary>
/// Segregates skipped / empty sub-panels from real inspection results so
/// FPY and DPMO can be read on the clean production population. Classifies
/// every <c>CARDS</c> row in the window into a <see cref="SkipClass"/> via
/// the verified <see cref="SkipClassifier"/> and tallies cards + component
/// volume per class.
/// </summary>
/// <remarks>
/// <para>
/// The report is a three-pass join over the same window:
/// </para>
/// <list type="number">
///   <item><description>
///     <see cref="IAoiSource.StreamPanelsAsync"/> → a
///     <c>Panel_Id → Has_Been_Reviewed</c> map (gates
///     <see cref="SkipClass.ManualSkip"/>).
///   </description></item>
///   <item><description>
///     <see cref="IAoiSource.StreamTestedObjectsAsync"/> → per-card
///     "Object missing" count and a manual-skip-button flag. Production
///     <c>TESTED_OBJECT</c> is defect-only, so this stream is small even
///     over a wide window.
///   </description></item>
///   <item><description>
///     <see cref="IAoiSource.StreamCardsAsync"/> → classify each card
///     from its <c>Number_Of_Component</c> / <c>Anomaly_AR</c> plus the
///     joined aggregates.
///   </description></item>
/// </list>
/// <para>
/// All three streams honour the same window / machine / product scope and
/// the source's IS_LAST_INSPECTION de-duplication, so the join is
/// consistent. Counts accumulate as <see cref="long"/> and the
/// percentages are computed once at the end (count-first / divide-last).
/// </para>
/// </remarks>
public sealed class SkipSummaryReport : IReport<SkipSummaryFilter, SkipSummaryResult>
{
    /// <summary>Stable metadata for this report.</summary>
    public static readonly ReportDescriptor ReportDescriptor = new(
        Id: "skip-summary",
        DisplayName: "Skip summary",
        Category: ReportCategory.Table,
        Description: "Segregates skipped / empty sub-panels (manual X-OUT, machine skip mark, disabled-skip missing pollution) so FPY and DPMO can be read on the clean population.");

    /// <summary>Stateless singleton; safe to share across all callers.</summary>
    public static readonly SkipSummaryReport Instance = new();

    /// <inheritdoc />
    public ReportDescriptor Descriptor => ReportDescriptor;

    /// <inheritdoc />
    public async Task<SkipSummaryResult> RunAsync(
        IAoiSource source,
        SkipSummaryFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filter);
        var config = filter.Config ?? SkipClassificationConfig.Default;

        // Passes 1-2: pre-stream panels (review flag) + tested objects
        // (per-card missing count + manual-skip flag).
        var index = await SkipInputsIndex.BuildAsync(
            source, filter.Window, filter.MachineIds, filter.ProductIds,
            filter.OnlyLastInspection, config, cancellationToken).ConfigureAwait(false);

        // Pass 3: classify each card.
        var cardCounts = new long[SkipClassCardinality];
        var componentCounts = new long[SkipClassCardinality];
        long totalCards = 0;
        long totalComponents = 0;
        var cardQuery = new CardQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
        };
        await foreach (var card in source.StreamCardsAsync(cardQuery, cancellationToken).ConfigureAwait(false))
        {
            var cls = index.Classify(card, config);
            cardCounts[(int)cls]++;
            componentCounts[(int)cls] += card.NbOfTestedObject;
            totalCards++;
            totalComponents += card.NbOfTestedObject;
        }

        var classes = new List<SkipClassCount>(SkipClassCardinality);
        foreach (var cls in Enum.GetValues<SkipClass>())
        {
            var count = cardCounts[(int)cls];
            classes.Add(new SkipClassCount(
                Class: cls,
                CardCount: count,
                ComponentCount: componentCounts[(int)cls],
                CardPercent: totalCards == 0 ? 0d : 100d * count / totalCards));
        }

        var skippedCards =
            cardCounts[(int)SkipClass.ManualSkip]
            + cardCounts[(int)SkipClass.MachineFlagged]
            + cardCounts[(int)SkipClass.HeuristicMissing];

        return new SkipSummaryResult(
            Source: source.Descriptor,
            Window: filter.Window,
            TotalCards: totalCards,
            TotalComponents: totalComponents,
            SkippedCards: skippedCards,
            SkippedCardPercent: totalCards == 0 ? 0d : 100d * skippedCards / totalCards,
            Classes: classes);
    }

    // None / ManualSkip / MachineFlagged / HeuristicMissing — contiguous
    // 0..3, so an array indexed by (int)SkipClass is safe.
    private static readonly int SkipClassCardinality = Enum.GetValues<SkipClass>().Length;
}
