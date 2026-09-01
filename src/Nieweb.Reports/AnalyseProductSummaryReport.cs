using Nieweb.DataSources;
using Nieweb.Reports.Common.Defects;

namespace Nieweb.Reports;

/// <summary>
/// ANA-04 product dashboard slice: FPY, component DPMO, and a defect-count
/// Pareto preview per product across all AOI lines.
/// </summary>
public sealed class AnalyseProductSummaryReport : IReport<AnalyseDashboardFilter, AnalyseProductSummaryResult>
{
    public static readonly AnalyseProductSummaryReport Instance = new();

    private const int ObjectTypeComponentBit = 0x00000001;

    private AnalyseProductSummaryReport()
    {
    }

    public ReportDescriptor Descriptor { get; } = new(
        Id: "analyse-product-summary",
        DisplayName: "Analyse Product Summary",
        Category: ReportCategory.Chart,
        Description: "Per-product FPY, component DPMO, and defect preview across all lines.");

    public async Task<AnalyseProductSummaryResult> RunAsync(
        IAoiSource source,
        AnalyseDashboardFilter input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(input);

        var supportsLastInspection = source.Descriptor.Caps.HasFlag(Capabilities.IsLastInspectionFilter);
        var useInMemoryDedupe = input.OnlyLastInspection && !supportsLastInspection;

        HashSet<int>? keptPanelIds = null;
        if (useInMemoryDedupe)
        {
            keptPanelIds = await BuildLatestPanelIdsAsync(
                source, input.Window, input.MachineIds, input.ProductIds, cancellationToken).ConfigureAwait(false);
        }

        var overallYield = new PanelAccumulator();
        var overallDpmo = new DpmoAccumulator();
        var rows = new Dictionary<int, ProductAccumulator>();

        var panelQuery = new PanelQuery
        {
            Window = input.Window,
            MachineIds = input.MachineIds,
            ProductIds = input.ProductIds,
            OnlyLastInspection = input.OnlyLastInspection,
        };
        await foreach (var panel in source.StreamPanelsAsync(panelQuery, cancellationToken).ConfigureAwait(false))
        {
            if (keptPanelIds is not null && !keptPanelIds.Contains(panel.PanelId))
            {
                continue;
            }

            overallYield.Add(panel.PanelStatus);
            GetProduct(rows, panel.ProductId, panel.MachineId).Yield.Add(panel.PanelStatus);
        }

        var cardQuery = new CardQuery
        {
            Window = input.Window,
            MachineIds = input.MachineIds,
            ProductIds = input.ProductIds,
        };
        await foreach (var card in source.StreamCardsAsync(cardQuery, cancellationToken).ConfigureAwait(false))
        {
            if (keptPanelIds is not null && !keptPanelIds.Contains((int)card.PanelId))
            {
                continue;
            }

            overallDpmo.AddOpportunity(card.NbOfTestsOnComp);
            GetProduct(rows, card.ProductId, card.MachineId).Dpmo.AddOpportunity(card.NbOfTestsOnComp);
        }

        var objectQuery = new TestedObjectQuery
        {
            Window = input.Window,
            MachineIds = input.MachineIds,
            ProductIds = input.ProductIds,
            DefectsOnly = true,
        };
        await foreach (var obj in source.StreamTestedObjectsAsync(objectQuery, cancellationToken).ConfigureAwait(false))
        {
            if (keptPanelIds is not null && !keptPanelIds.Contains((int)obj.PanelId))
            {
                continue;
            }
            if ((obj.ObjectTypeId & ObjectTypeComponentBit) == 0)
            {
                continue;
            }

            var defectBits = CountBits(obj.ErrorTable);
            overallDpmo.AddDefects(defectBits);
            var bucket = GetProduct(rows, obj.ProductId, obj.MachineId);
            bucket.Dpmo.AddDefects(defectBits);
            bucket.DefectBitCount += defectBits;
            foreach (var defect in DefectBitDecoder.Decode(obj.ErrorTable))
            {
                bucket.DefectBits.TryGetValue(defect.BitNumber, out var current);
                bucket.DefectBits[defect.BitNumber] = current + 1;
            }
        }

        var productNames = (await source.ListProductsAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(p => p.ProductId, p => p.ProductName);

        var products = rows.Values
            .OrderByDescending(r => r.DefectBitCount)
            .ThenBy(r => r.ProductId)
            .Select(r => new AnalyseProductSummaryRow(
                ProductId: r.ProductId,
                ProductName: productNames.TryGetValue(r.ProductId, out var name) ? name : null,
                Yield: r.Yield.ToKpi(),
                Dpmo: r.Dpmo.ToKpi(),
                DefectBitCount: r.DefectBitCount,
                TopDefectBits: r.DefectBits
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key)
                    .Take(3)
                    .Select(kvp => new AnalyseProductDefectCount(kvp.Key, kvp.Value))
                    .ToList()))
            .ToList();

        return new AnalyseProductSummaryResult(
            Source: source.Descriptor,
            Filter: input,
            OverallYield: overallYield.ToKpi(),
            OverallDpmo: overallDpmo.ToKpi(),
            Products: products,
            DedupeAppliedInMemory: useInMemoryDedupe,
            DedupeNote: useInMemoryDedupe
                ? "Source lacks IS_LAST_INSPECTION; dedupe is applied in memory by panel id."
                : null);
    }

    private static async Task<HashSet<int>> BuildLatestPanelIdsAsync(
        IAoiSource source,
        DateRange window,
        IReadOnlyCollection<int>? machineIds,
        IReadOnlyCollection<int>? productIds,
        CancellationToken cancellationToken)
    {
        var latestByBarcodeFace = new Dictionary<(string Barcode, int Face), PanelRow>();
        var query = new PanelQuery
        {
            Window = window,
            MachineIds = machineIds,
            ProductIds = productIds,
            OnlyLastInspection = false,
        };

        await foreach (var panel in source.StreamPanelsAsync(query, cancellationToken).ConfigureAwait(false))
        {
            var key = (panel.PanelBarCode, panel.FaceNumber ?? -1);
            if (!latestByBarcodeFace.TryGetValue(key, out var previous)
                || panel.PanelNumericDate > previous.PanelNumericDate
                || (panel.PanelNumericDate == previous.PanelNumericDate && panel.PanelId > previous.PanelId))
            {
                latestByBarcodeFace[key] = panel;
            }
        }

        return latestByBarcodeFace.Values.Select(p => p.PanelId).ToHashSet();
    }

    private static long CountBits(long value)
    {
        var count = 0L;
        var remaining = unchecked((ulong)value);
        while (remaining != 0)
        {
            remaining &= remaining - 1;
            count++;
        }

        return count;
    }

    private static ProductAccumulator GetProduct(Dictionary<int, ProductAccumulator> rows, int productId, int machineId)
    {
        if (!rows.TryGetValue(productId, out var bucket))
        {
            bucket = new ProductAccumulator(productId, machineId);
            rows[productId] = bucket;
        }

        return bucket;
    }

    private sealed class PanelAccumulator
    {
        private long _good;
        private long _faulty;
        private long _notInspected;

        public void Add(int panelStatus)
        {
            switch (panelStatus)
            {
                case 1 or 2 or 3:
                    _good++;
                    break;
                case -2 or -1:
                    _faulty++;
                    break;
                case 0:
                    _notInspected++;
                    break;
                default:
                    _notInspected++;
                    break;
            }
        }

        public PanelYieldKpi ToKpi()
        {
            var inspected = _good + _faulty;
            var total = inspected + _notInspected;
            var fpy = inspected == 0 ? 0d : 100d * _good / inspected;
            return new PanelYieldKpi(total, inspected, _good, _faulty, _notInspected, fpy);
        }
    }

    private sealed class DpmoAccumulator
    {
        private long _testedObjects;
        private long _opportunities;
        private long _defects;

        public void AddOpportunity(long opportunities) => _opportunities += opportunities;

        public void AddDefects(long defects)
        {
            _testedObjects++;
            _defects += defects;
        }

        public DpmoKpi ToKpi() => new(_testedObjects, _opportunities, _defects, _opportunities == 0 ? 0d : 1_000_000d * _defects / _opportunities);
    }

    private sealed class ProductAccumulator
    {
        public ProductAccumulator(int productId, int machineId)
        {
            ProductId = productId;
            MachineId = machineId;
        }

        public int ProductId { get; }
        public int MachineId { get; }
        public PanelAccumulator Yield { get; } = new();
        public DpmoAccumulator Dpmo { get; } = new();
        public long DefectBitCount { get; set; }
        public Dictionary<int, long> DefectBits { get; } = new();
    }
}

public sealed record AnalyseProductDefectCount(int BitNumber, long Count);

public sealed record AnalyseProductSummaryRow(
    int ProductId,
    string? ProductName,
    PanelYieldKpi Yield,
    DpmoKpi Dpmo,
    long DefectBitCount,
    IReadOnlyList<AnalyseProductDefectCount> TopDefectBits);

public sealed record AnalyseProductSummaryResult(
    SourceDescriptor Source,
    AnalyseDashboardFilter Filter,
    PanelYieldKpi OverallYield,
    DpmoKpi OverallDpmo,
    IReadOnlyList<AnalyseProductSummaryRow> Products,
    bool DedupeAppliedInMemory,
    string? DedupeNote);