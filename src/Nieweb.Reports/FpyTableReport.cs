using Nieweb.DataSources;

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

        if (filter.Granularity == FpyGranularity.Panel)
        {
            var panelQuery = new PanelQuery
            {
                Window = filter.Window,
                MachineIds = filter.MachineIds,
                ProductIds = filter.ProductIds,
                RecipeIds = filter.RecipeIds,
                OnlyLastInspection = filter.OnlyLastInspection,
            };
            await foreach (var panel in source.StreamPanelsAsync(panelQuery, cancellationToken).ConfigureAwait(false))
            {
                overall.Add(panel.PanelStatus);
                var key = filter.GroupBy == FpyGroupBy.AoiMachine ? panel.MachineId : panel.ProductId;
                if (!perGroup.TryGetValue(key, out var bucket))
                {
                    bucket = new Accumulator();
                    perGroup[key] = bucket;
                }
                bucket.Add(panel.PanelStatus);
            }
        }
        else
        {
            var cardQuery = new CardQuery
            {
                Window = filter.Window,
                MachineIds = filter.MachineIds,
                ProductIds = filter.ProductIds,
                RecipeIds = filter.RecipeIds,
            };
            await foreach (var card in source.StreamCardsAsync(cardQuery, cancellationToken).ConfigureAwait(false))
            {
                overall.Add(card.CardStatus);
                var key = filter.GroupBy == FpyGroupBy.AoiMachine ? card.MachineId : card.ProductId;
                if (!perGroup.TryGetValue(key, out var bucket))
                {
                    bucket = new Accumulator();
                    perGroup[key] = bucket;
                }
                bucket.Add(card.CardStatus);
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
            Rows: rows);
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
