using Nieweb.DataSources;
using Nieweb.Reports.Common;
using Nieweb.Reports.Common.Skips;

namespace Nieweb.Reports;

/// <summary>
/// FPY trend by line: one FPY series per AOI machine, bucketed by day or
/// week over the requested window. Each point carries all three FPY
/// flavours (AOI / Diagnostic / After Repair) via <see cref="FpyKpi"/> so a
/// client can toggle between them without a refetch, and panel- vs
/// sub-panel (board) granularity is selectable via
/// <see cref="FpyTrendFilter.Granularity"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the FPY-table aggregation re-keyed by <c>(machineId, bucketIndex)</c>
/// instead of by machine / product. It reuses the shared
/// <see cref="FpyAccumulator"/> (count-first / divide-last) and the same
/// skip-exclusion machinery (<see cref="SkipInputsIndex"/>,
/// <see cref="FpyPanelStatus"/>) so the numbers agree with the FPY table and
/// the Skip Summary for the same scope.
/// </para>
/// <para>
/// Time bucketing uses <see cref="TimeBucketer.Decompose"/> (wall-clock
/// aligned in the site time zone) and routes each row to a bucket by binary
/// search over the bucket start epochs — the same pattern the Pareto report
/// uses for its Day axis.
/// </para>
/// </remarks>
public sealed class FpyTrendByLineReport : IReport<FpyTrendFilter, FpyTrendResult>
{
    /// <summary>Stable metadata for this report.</summary>
    public static readonly ReportDescriptor ReportDescriptor = new(
        Id: "fpy-trend",
        DisplayName: "FPY Trend",
        Category: ReportCategory.Chart,
        Description: "First Pass Yield over time (day / week) per AOI line.");

    /// <summary>Stateless singleton; safe to share across all callers.</summary>
    public static readonly FpyTrendByLineReport Instance = new();

    /// <inheritdoc />
    public ReportDescriptor Descriptor => ReportDescriptor;

    /// <inheritdoc />
    public async Task<FpyTrendResult> RunAsync(
        IAoiSource source,
        FpyTrendFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Bucket is not (TimeBucket.Day or TimeBucket.Week))
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter), filter.Bucket,
                "FPY trend supports only Day or Week buckets.");
        }

        var timeZone = filter.SiteTimeZone ?? TimeZoneInfo.Utc;
        var buckets = TimeBucketer.Decompose(
            filter.Window.StartUtc, filter.Window.EndUtcExclusive, filter.Bucket, timeZone);
        var bucketStartEpochs = buckets.Select(b => b.StartUtc.ToUnixTimeSeconds()).ToArray();

        // FPY accumulators keyed by (machine, bucket) for the chart points
        // and by machine for the per-line window total.
        var cells = new Dictionary<(int MachineId, int BucketIndex), FpyAccumulator>();
        var lineOverall = new Dictionary<int, FpyAccumulator>();

        void Fold(int machineId, int panelNumericDate, int status)
        {
            var bucketIndex = FindBucketIndex(buckets, bucketStartEpochs, panelNumericDate);
            if (bucketIndex < 0)
            {
                return; // Row falls outside every bucket (edge of window).
            }
            var cellKey = (machineId, bucketIndex);
            if (!cells.TryGetValue(cellKey, out var cell))
            {
                cell = new FpyAccumulator();
                cells[cellKey] = cell;
            }
            cell.Add(status);

            if (!lineOverall.TryGetValue(machineId, out var overall))
            {
                overall = new FpyAccumulator();
                lineOverall[machineId] = overall;
            }
            overall.Add(status);
        }

        // Skip predicates: identical to FpyTableReport so Clean / status
        // filtering produces the same board population.
        var config = filter.SkipConfig ?? SkipClassificationConfig.Default;
        var statusFilter = filter.SkipStatuses is { Count: > 0 }
            ? new HashSet<SkipClass>(filter.SkipStatuses)
            : null;
        var needsIndex = filter.SkipExclusion == SkipExclusion.Clean || statusFilter is not null;
        bool KeepClass(SkipClass cls)
        {
            if (statusFilter is null)
            {
                return filter.SkipExclusion != SkipExclusion.Clean || cls == SkipClass.None;
            }
            return filter.SkipExclusion == SkipExclusion.Clean
                ? cls == SkipClass.None || statusFilter.Contains(cls)
                : statusFilter.Contains(cls);
        }

        var nogoProductIds = await NogoProducts.BuildAsync(
            source, filter.ExcludeNogo, cancellationToken).ConfigureAwait(false);

        long skipExcludedRows = 0;
        if (needsIndex)
        {
            var index = await SkipInputsIndex.BuildAsync(
                source, filter.Window, filter.MachineIds, filter.ProductIds,
                filter.OnlyLastInspection, config, cancellationToken).ConfigureAwait(false);

            skipExcludedRows = filter.Granularity == FpyGranularity.Board
                ? await FoldFilteredBoardAsync(
                    source, filter, index, config, KeepClass, nogoProductIds, Fold, cancellationToken).ConfigureAwait(false)
                : await FoldFilteredPanelAsync(
                    source, filter, index, config, KeepClass, nogoProductIds, Fold, cancellationToken).ConfigureAwait(false);
        }
        else if (filter.Granularity == FpyGranularity.Panel)
        {
            var panelQuery = new PanelQuery
            {
                Window = filter.Window,
                MachineIds = filter.MachineIds,
                ProductIds = filter.ProductIds,
                OnlyLastInspection = filter.OnlyLastInspection,
                // Large pages: this report reads the whole window, so keyset
                // paging is pure overhead — minimise round trips.
                PageSize = 10_000,
            };
            await foreach (var panel in source.StreamPanelsAsync(panelQuery, cancellationToken).ConfigureAwait(false))
            {
                if (nogoProductIds is not null && nogoProductIds.Contains(panel.ProductId))
                {
                    continue;
                }
                Fold(panel.MachineId, panel.PanelNumericDate, panel.PanelStatus);
            }
        }
        else
        {
            var cardQuery = new CardQuery
            {
                Window = filter.Window,
                MachineIds = filter.MachineIds,
                ProductIds = filter.ProductIds,
                PageSize = 10_000,
            };
            await foreach (var card in source.StreamCardsAsync(cardQuery, cancellationToken).ConfigureAwait(false))
            {
                if (nogoProductIds is not null && nogoProductIds.Contains(card.ProductId))
                {
                    continue;
                }
                Fold(card.MachineId, card.PanelNumericDate, card.CardStatus);
            }
        }

        var machineNames = (await source.ListMachinesAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(m => m.MachineId, m => (string?)m.MachineName);

        var bucketDtos = buckets
            .Select((b, i) => new FpyTrendBucket(i, b.Label, b.StartUtc, b.EndUtcExclusive))
            .ToList();

        var lines = lineOverall.Keys
            .Select(machineId =>
            {
                var points = new List<FpyTrendPoint>(buckets.Count);
                for (var i = 0; i < buckets.Count; i++)
                {
                    if (cells.TryGetValue((machineId, i), out var cell))
                    {
                        points.Add(new FpyTrendPoint(i, cell.ToKpi()));
                    }
                }
                return new FpyTrendLine(
                    MachineId: machineId,
                    MachineName: machineNames.TryGetValue(machineId, out var name) ? name : null,
                    Points: points,
                    Overall: lineOverall[machineId].ToKpi());
            })
            // Stable, human-friendly ordering: by name, then id for ties /
            // catalogue-missing machines.
            .OrderBy(l => l.MachineName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.MachineId)
            .ToList();

        return new FpyTrendResult(
            Source: source.Descriptor,
            Window: filter.Window,
            Bucket: filter.Bucket,
            Granularity: filter.Granularity,
            SkipExclusion: filter.SkipExclusion,
            Buckets: bucketDtos,
            Lines: lines,
            SkipExcludedRows: skipExcludedRows);
    }

    /// <summary>Board-level filtered fold: keep only boards the predicate admits.</summary>
    private static async Task<long> FoldFilteredBoardAsync(
        IAoiSource source,
        FpyTrendFilter filter,
        SkipInputsIndex index,
        SkipClassificationConfig config,
        Func<SkipClass, bool> keep,
        HashSet<int>? nogoProductIds,
        Action<int, int, int> fold,
        CancellationToken cancellationToken)
    {
        long excluded = 0;
        var cardQuery = new CardQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
            PageSize = 10_000,
        };
        await foreach (var card in source.StreamCardsAsync(cardQuery, cancellationToken).ConfigureAwait(false))
        {
            if (nogoProductIds is not null && nogoProductIds.Contains(card.ProductId))
            {
                continue;
            }
            if (!keep(index.Classify(card, config)))
            {
                excluded++;
                continue;
            }
            fold(card.MachineId, card.PanelNumericDate, card.CardStatus);
        }
        return excluded;
    }

    /// <summary>
    /// Panel-level filtered fold: a panel with no excluded board keeps the
    /// AOI's own <c>Panel_Status</c>; a panel with some excluded boards is
    /// re-derived from its survivors via <see cref="FpyPanelStatus.Effective"/>;
    /// a fully-excluded panel is dropped. The panel timestamp comes from its
    /// cards (all cards of a panel share <c>Panel_Numeric_Date</c>).
    /// </summary>
    private static async Task<long> FoldFilteredPanelAsync(
        IAoiSource source,
        FpyTrendFilter filter,
        SkipInputsIndex index,
        SkipClassificationConfig config,
        Func<SkipClass, bool> keep,
        HashSet<int>? nogoProductIds,
        Action<int, int, int> fold,
        CancellationToken cancellationToken)
    {
        var perPanel = new Dictionary<long, PanelCards>();
        var cardQuery = new CardQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
            PageSize = 10_000,
        };
        await foreach (var card in source.StreamCardsAsync(cardQuery, cancellationToken).ConfigureAwait(false))
        {
            if (!perPanel.TryGetValue(card.PanelId, out var cards))
            {
                cards = new PanelCards { PanelNumericDate = card.PanelNumericDate };
                perPanel[card.PanelId] = cards;
            }
            if (!keep(index.Classify(card, config)))
            {
                cards.HasSkip = true;
            }
            else
            {
                cards.NonSkipStatuses.Add(card.CardStatus);
            }
        }

        long excluded = 0;
        foreach (var (panelId, info) in index.Panels)
        {
            if (nogoProductIds is not null && nogoProductIds.Contains(info.ProductId))
            {
                continue;
            }
            if (!perPanel.TryGetValue(panelId, out var cards))
            {
                // No cards streamed for this panel — no timestamp to bucket on.
                continue;
            }

            int effectiveStatus;
            if (!cards.HasSkip)
            {
                effectiveStatus = info.PanelStatus;
            }
            else if (cards.NonSkipStatuses.Count == 0)
            {
                excluded++;
                continue;
            }
            else
            {
                effectiveStatus = FpyPanelStatus.Effective(cards.NonSkipStatuses);
            }

            fold(info.MachineId, cards.PanelNumericDate, effectiveStatus);
        }
        return excluded;
    }

    /// <summary>
    /// Binary search: index of the bucket whose half-open
    /// <c>[StartEpoch, next-start)</c> contains <paramref name="panelNumericDate"/>,
    /// or <c>-1</c> when the timestamp falls outside the last bucket's
    /// exclusive end.
    /// </summary>
    private static int FindBucketIndex(
        IReadOnlyList<TimeBucketRange> buckets,
        long[] bucketStartEpochs,
        int panelNumericDate)
    {
        long epoch = panelNumericDate;
        var idx = Array.BinarySearch(bucketStartEpochs, epoch);
        if (idx < 0)
        {
            idx = ~idx - 1;
            if (idx < 0)
            {
                return -1;
            }
        }
        return epoch < buckets[idx].EndUtcExclusive.ToUnixTimeSeconds() ? idx : -1;
    }

    private sealed class PanelCards
    {
        public int PanelNumericDate { get; init; }
        public bool HasSkip { get; set; }
        public List<int> NonSkipStatuses { get; } = [];
    }
}
