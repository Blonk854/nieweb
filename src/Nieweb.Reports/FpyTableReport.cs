using Nieweb.DataSources;
using Nieweb.Reports.Common.Skips;

namespace Nieweb.Reports;

/// <summary>
/// FPY (First Pass Yield) table: rows per AOI machine or per product,
/// counting panels or boards in the three Vieweb FPY flavours
/// (AOI / Diagnostic / After Repair). Implements Vieweb §3.1.6.4
/// verbatim ("FPY tables can show data by AOI or by product … FPY
/// analysis can be done on panels or boards. Table ordered by
/// increasing FPY value").
/// </summary>
/// <remarks>
/// <para>
/// For <see cref="FpyGranularity.Panel"/> the report streams
/// <see cref="IAoiSource.StreamPanelsAsync"/> and folds each row by
/// <see cref="PanelRow.PanelStatus"/>. For
/// <see cref="FpyGranularity.Board"/> it streams
/// <see cref="IAoiSource.StreamCardsAsync"/> and folds each row by
/// <see cref="CardRow.CardStatus"/> — <c>CardRow</c> already carries
/// <c>MachineId</c> and <c>ProductId</c> from the parent panel so the
/// aggregation needs no additional joins at report level.
/// </para>
/// <para>
/// Aggregation is count-first / divide-last: for every row we bump
/// one of four integer buckets (GoodAoi / GoodDiagnostic / GoodAfterRepair
/// / Faulty / NotInspected) and only compute the three FPY percentages
/// at the end. This makes it impossible to reproduce Vieweb bug #12421
/// (weekly FPY differing from the sum of daily FPYs).
/// </para>
/// <para>
/// Machine / product name resolution happens after streaming, from a
/// single small <c>ListMachinesAsync</c> / <c>ListProductsAsync</c>
/// call. Rows whose grouping id is missing from the catalogue still
/// surface with <see cref="FpyTableRow.GroupName"/> = <c>null</c> —
/// legitimate for decommissioned machines / archived products.
/// </para>
/// </remarks>
public sealed class FpyTableReport : IReport<FpyTableFilter, FpyTableResult>
{
    /// <summary>Stable metadata for this report.</summary>
    public static readonly ReportDescriptor ReportDescriptor = new(
        Id: "fpy-table",
        DisplayName: "FPY table",
        Category: ReportCategory.Table,
        Description: "First Pass Yield table (panel or board) per AOI machine or product.");

    /// <summary>Stateless singleton; safe to share across all callers.</summary>
    public static readonly FpyTableReport Instance = new();

    /// <inheritdoc />
    public ReportDescriptor Descriptor => ReportDescriptor;

    /// <inheritdoc />
    /// <remarks>The class-level remarks describe the aggregation contract.</remarks>
    public async Task<FpyTableResult> RunAsync(
        IAoiSource source,
        FpyTableFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filter);

        var overall = new Accumulator();
        var perGroup = new Dictionary<int, Accumulator>();
        long skipExcludedRows = 0;

        // Two composable per-board skip predicates:
        //   * SkipExclusion.Clean drops any non-None (skipped) board.
        //   * SkipStatuses (when set) keeps only boards whose class is in
        //     the set (positive narrowing, e.g. "ManualSkip only").
        // A board is kept iff it satisfies BOTH. Whenever either predicate
        // is active we must classify boards, and panel-level FPY is
        // re-derived from the surviving boards (the AOI's own Panel_Status
        // reflects every board, so it can't be trusted once we drop some).
        var config = filter.SkipConfig ?? SkipClassificationConfig.Default;
        var statusFilter = filter.SkipStatuses is { Count: > 0 }
            ? new HashSet<SkipClass>(filter.SkipStatuses)
            : null;
        var needsIndex = filter.SkipExclusion == SkipExclusion.Clean || statusFilter is not null;
        bool KeepClass(SkipClass cls)
        {
            // No status filter: Clean drops skipped (non-None) boards; Raw keeps all.
            if (statusFilter is null)
            {
                return filter.SkipExclusion != SkipExclusion.Clean || cls == SkipClass.None;
            }
            // With a status filter set:
            //  - Clean: the selected classes are kept as exceptions alongside None.
            //  - Raw:   the selected classes act as a positive "show only these" filter.
            return filter.SkipExclusion == SkipExclusion.Clean
                ? cls == SkipClass.None || statusFilter.Contains(cls)
                : statusFilter.Contains(cls);
        }

        // NOGO exclusion: drop every product whose name contains "NOGO"
        // (case-insensitive) from every counting path, so changeover
        // calibration coupons never skew the FPY numerator or denominator.
        var nogoProductIds = await NogoProducts.BuildAsync(
            source, filter.ExcludeNogo, cancellationToken).ConfigureAwait(false);

        if (needsIndex)
        {
            // Classify every board and keep only the ones the predicate
            // admits (manual X-OUT / machine skip mark / disabled-skip
            // missing are dropped in Clean mode; a status filter narrows
            // further to the selected classes).
            var index = await SkipInputsIndex.BuildAsync(
                source, filter.Window, filter.MachineIds, filter.ProductIds,
                filter.OnlyLastInspection, config, cancellationToken).ConfigureAwait(false);

            skipExcludedRows = filter.Granularity == FpyGranularity.Board
                ? await AccumulateFilteredBoardAsync(
                    source, filter, index, config, KeepClass, nogoProductIds, overall, perGroup, cancellationToken).ConfigureAwait(false)
                : await AccumulateFilteredPanelAsync(
                    source, filter, index, config, KeepClass, nogoProductIds, overall, perGroup, cancellationToken).ConfigureAwait(false);
        }
        else if (filter.Granularity == FpyGranularity.Panel)
        {
            var panelQuery = new PanelQuery
            {
                Window = filter.Window,
                MachineIds = filter.MachineIds,
                ProductIds = filter.ProductIds,
                OnlyLastInspection = filter.OnlyLastInspection,
            };
            await foreach (var panel in source.StreamPanelsAsync(panelQuery, cancellationToken).ConfigureAwait(false))
            {
                if (nogoProductIds is not null && nogoProductIds.Contains(panel.ProductId))
                {
                    continue;
                }
                overall.Add(panel.PanelStatus);
                var key = filter.GroupBy == FpyGroupBy.AoiMachine ? panel.MachineId : panel.ProductId;
                GetBucket(perGroup, key).Add(panel.PanelStatus);
            }
        }
        else
        {
            var cardQuery = new CardQuery
            {
                Window = filter.Window,
                MachineIds = filter.MachineIds,
                ProductIds = filter.ProductIds,
            };
            await foreach (var card in source.StreamCardsAsync(cardQuery, cancellationToken).ConfigureAwait(false))
            {
                if (nogoProductIds is not null && nogoProductIds.Contains(card.ProductId))
                {
                    continue;
                }
                overall.Add(card.CardStatus);
                var key = filter.GroupBy == FpyGroupBy.AoiMachine ? card.MachineId : card.ProductId;
                GetBucket(perGroup, key).Add(card.CardStatus);
            }
        }

        // Resolve display names for the axis. One small read per grouping
        // axis; not both, so we do not pull catalogues we don't need.
        var groupNames = filter.GroupBy == FpyGroupBy.AoiMachine
            ? (await source.ListMachinesAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(m => m.MachineId, m => (string?)m.MachineName)
            : (await source.ListProductsAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(p => p.ProductId, p => p.ProductName);

        var rows = perGroup
            .Select(kvp => new FpyTableRow(
                GroupKey: kvp.Key,
                GroupName: groupNames.TryGetValue(kvp.Key, out var name) ? name : null,
                Kpi: kvp.Value.ToKpi()))
            // Vieweb: "ordered by increasing FPY value" — break ties by
            // GroupKey so snapshots are stable.
            .OrderBy(r => r.Kpi.FpyAoiPercent)
            .ThenBy(r => r.GroupKey)
            .ToList();

        return new FpyTableResult(
            Source: source.Descriptor,
            Window: filter.Window,
            Granularity: filter.Granularity,
            GroupBy: filter.GroupBy,
            Overall: overall.ToKpi(),
            Rows: rows,
            SkipExclusion: filter.SkipExclusion,
            SkipExcludedRows: skipExcludedRows);
    }

    private static Accumulator GetBucket(Dictionary<int, Accumulator> perGroup, int key)
    {
        if (!perGroup.TryGetValue(key, out var bucket))
        {
            bucket = new Accumulator();
            perGroup[key] = bucket;
        }
        return bucket;
    }

    /// <summary>
    /// Filtered board-level FPY: classify each board and keep only the
    /// ones <paramref name="keep"/> admits. Returns the number of
    /// excluded boards.
    /// </summary>
    private static async Task<long> AccumulateFilteredBoardAsync(
        IAoiSource source,
        FpyTableFilter filter,
        SkipInputsIndex index,
        SkipClassificationConfig config,
        Func<SkipClass, bool> keep,
        HashSet<int>? nogoProductIds,
        Accumulator overall,
        Dictionary<int, Accumulator> perGroup,
        CancellationToken cancellationToken)
    {
        long excluded = 0;
        var cardQuery = new CardQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
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
            overall.Add(card.CardStatus);
            var key = filter.GroupBy == FpyGroupBy.AoiMachine ? card.MachineId : card.ProductId;
            GetBucket(perGroup, key).Add(card.CardStatus);
        }
        return excluded;
    }

    /// <summary>
    /// Filtered panel-level FPY: a panel with no excluded board keeps the
    /// AOI's own <c>Panel_Status</c>; a panel with some excluded boards is
    /// re-derived from its surviving (kept) boards; a fully-excluded panel
    /// is dropped. Returns the number of excluded panels.
    /// </summary>
    private static async Task<long> AccumulateFilteredPanelAsync(
        IAoiSource source,
        FpyTableFilter filter,
        SkipInputsIndex index,
        SkipClassificationConfig config,
        Func<SkipClass, bool> keep,
        HashSet<int>? nogoProductIds,
        Accumulator overall,
        Dictionary<int, Accumulator> perGroup,
        CancellationToken cancellationToken)
    {
        // First pass: group cards by panel, recording whether the panel
        // has any excluded board and the statuses of the survivors.
        var perPanel = new Dictionary<long, PanelCards>();
        var cardQuery = new CardQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
        };
        await foreach (var card in source.StreamCardsAsync(cardQuery, cancellationToken).ConfigureAwait(false))
        {
            var kept = keep(index.Classify(card, config));
            if (!perPanel.TryGetValue(card.PanelId, out var cards))
            {
                cards = new PanelCards();
                perPanel[card.PanelId] = cards;
            }
            if (!kept)
            {
                cards.HasSkip = true;
            }
            else
            {
                cards.NonSkipStatuses.Add(card.CardStatus);
            }
        }

        // Second pass: fold every panel in scope into the FPY buckets.
        long excluded = 0;
        foreach (var (panelId, info) in index.Panels)
        {
            if (nogoProductIds is not null && nogoProductIds.Contains(info.ProductId))
            {
                continue;
            }
            int effectiveStatus;
            if (!perPanel.TryGetValue(panelId, out var cards) || !cards.HasSkip)
            {
                // No excluded board — trust the AOI's own panel verdict so
                // a no-op filter is identical to raw.
                effectiveStatus = info.PanelStatus;
            }
            else if (cards.NonSkipStatuses.Count == 0)
            {
                // Every board on the panel was excluded.
                excluded++;
                continue;
            }
            else
            {
                effectiveStatus = EffectivePanelStatus(cards.NonSkipStatuses);
            }

            overall.Add(effectiveStatus);
            var key = filter.GroupBy == FpyGroupBy.AoiMachine ? info.MachineId : info.ProductId;
            GetBucket(perGroup, key).Add(effectiveStatus);
        }
        return excluded;
    }

    /// <summary>
    /// The effective status of a panel re-derived from its surviving
    /// (non-skip) boards: the panel is only as good as its worst board.
    /// Goodness order (best → worst): 1 (good AOI) &lt; 2 (good
    /// diagnostic) &lt; 3 (repaired) &lt; faulty. Status 0 (not inspected)
    /// is ignored unless it is all that is present, in which case the
    /// panel is not-inspected.
    /// </summary>
    private static int EffectivePanelStatus(List<int> nonSkipStatuses)
    {
        var worstStatus = 0;
        var worstRank = -1;
        foreach (var status in nonSkipStatuses)
        {
            if (status == 0)
            {
                continue;
            }
            var rank = status switch { 1 => 0, 2 => 1, 3 => 2, _ => 3 }; // -1 / -2 / unknown = faulty
            if (rank > worstRank)
            {
                worstRank = rank;
                worstStatus = status;
            }
        }
        return worstStatus;
    }

    private sealed class PanelCards
    {
        public bool HasSkip { get; set; }
        public List<int> NonSkipStatuses { get; } = [];
    }

    /// <summary>
    /// Mutable counter that translates a Panel_Status / Card_Status
    /// enum value into the four count buckets and produces an
    /// immutable <see cref="FpyKpi"/> at the end.
    /// </summary>
    private sealed class Accumulator
    {
        private long _total;
        private long _notInspected;
        private long _faulty;
        private long _goodAoi;          // status = 1
        private long _goodDummyOnly;    // status = 2 (all defects dummy)
        private long _goodRepaired;     // status = 3

        public void Add(int status)
        {
            _total++;
            switch (status)
            {
                case 1:
                    _goodAoi++;
                    break;
                case 2:
                    _goodDummyOnly++;
                    break;
                case 3:
                    _goodRepaired++;
                    break;
                case -1 or -2:
                    _faulty++;
                    break;
                case 0:
                    _notInspected++;
                    break;
                default:
                    // Unknown status code — treat as not-inspected so
                    // FPY numerators stay honest. See aoi-quality-metrics
                    // skill: the canonical enum is {-2,-1,0,1,2,3}; hitting
                    // this branch means the schema changed.
                    _notInspected++;
                    break;
            }
        }

        public FpyKpi ToKpi()
        {
            var goodDiag = _goodAoi + _goodDummyOnly;
            var goodAr = goodDiag + _goodRepaired;
            var inspected = _total - _notInspected;

            var fpyAoi = inspected == 0 ? 0d : 100d * _goodAoi / inspected;
            var fpyDiag = inspected == 0 ? 0d : 100d * goodDiag / inspected;
            var fpyAr = inspected == 0 ? 0d : 100d * goodAr / inspected;

            return new FpyKpi(
                TotalRows: _total,
                InspectedCount: inspected,
                NotInspectedCount: _notInspected,
                FaultyCount: _faulty,
                GoodAoiCount: _goodAoi,
                GoodDiagnosticCount: goodDiag,
                GoodAfterRepairCount: goodAr,
                FpyAoiPercent: fpyAoi,
                FpyDiagnosticPercent: fpyDiag,
                FpyAfterRepairPercent: fpyAr);
        }
    }
}
