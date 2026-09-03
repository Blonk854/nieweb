using Nieweb.DataSources;
using Nieweb.Reports.Common.Defects;

namespace Nieweb.Reports;

/// <summary>
/// ANA-05 Panel dashboard slice: per-panel defect ranking across the window.
/// Analyst-oriented companion to Board Trace (operator-oriented single-barcode
/// view): ranks panels by defect-bit count so engineers can spot the worst
/// boards, then drill into Board Trace for defect list → repair sanction.
/// </summary>
public sealed class AnalysePanelSummaryReport : IReport<AnalyseDashboardFilter, AnalysePanelSummaryResult>
{
    public static readonly AnalysePanelSummaryReport Instance = new();

    private const int ObjectTypeComponentBit = 0x00000001;

    /// <summary>Cap on rows returned so the payload stays bounded for large windows.</summary>
    public const int MaxRows = 50;

    private AnalysePanelSummaryReport()
    {
    }

    public ReportDescriptor Descriptor { get; } = new(
        Id: "analyse-panel-summary",
        DisplayName: "Analyse Panel Summary",
        Category: ReportCategory.Chart,
        Description: "Worst panels by defect-bit count with product/machine context.");

    public async Task<AnalysePanelSummaryResult> RunAsync(
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
        var panels = new Dictionary<int, PanelAccumulator2>();

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
            panels[panel.PanelId] = new PanelAccumulator2(panel);
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
            if (panels.TryGetValue((int)obj.PanelId, out var bucket))
            {
                bucket.DefectBitCount += defectBits;
                bucket.TestedObjectCount++;
                foreach (var defect in DefectBitDecoder.Decode(obj.ErrorTable))
                {
                    bucket.DefectBits.TryGetValue(defect.BitNumber, out var current);
                    bucket.DefectBits[defect.BitNumber] = current + 1;
                }
            }
        }

        var productNames = (await source.ListProductsAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(p => p.ProductId, p => p.ProductName);
        var machineNames = (await source.ListMachinesAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(m => m.MachineId, m => (string?)m.MachineName);

        var rows = panels.Values
            .OrderByDescending(r => r.DefectBitCount)
            .ThenByDescending(r => r.Panel.PanelNumericDate)
            .ThenBy(r => r.Panel.PanelId)
            .Take(MaxRows)
            .Select(r => new AnalysePanelSummaryRow(
                PanelId: r.Panel.PanelId,
                Barcode: r.Panel.PanelBarCode,
                PanelUtc: DateTimeOffset.FromUnixTimeSeconds(r.Panel.PanelNumericDate),
                ProductId: r.Panel.ProductId,
                ProductName: productNames.TryGetValue(r.Panel.ProductId, out var pname) ? pname : null,
                MachineId: r.Panel.MachineId,
                MachineName: machineNames.TryGetValue(r.Panel.MachineId, out var mname) ? mname : null,
                PanelStatus: r.Panel.PanelStatus,
                DefectBitCount: r.DefectBitCount,
                TestedObjectCount: r.TestedObjectCount,
                TopDefectBits: r.DefectBits
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key)
                    .Take(3)
                    .Select(kvp => new AnalyseProductDefectCount(kvp.Key, kvp.Value))
                    .ToList()))
            .ToList();

        return new AnalysePanelSummaryResult(
            Source: source.Descriptor,
            Filter: input,
            OverallYield: overallYield.ToKpi(),
            OverallDpmo: overallDpmo.ToKpi(),
            TotalPanels: panels.Count,
            Panels: rows,
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

    private sealed class PanelAccumulator2
    {
        public PanelAccumulator2(PanelRow panel)
        {
            Panel = panel;
        }

        public PanelRow Panel { get; }
        public long DefectBitCount { get; set; }
        public long TestedObjectCount { get; set; }
        public Dictionary<int, long> DefectBits { get; } = new();
    }
}

public sealed record AnalysePanelSummaryRow(
    int PanelId,
    string Barcode,
    DateTimeOffset PanelUtc,
    int ProductId,
    string? ProductName,
    int MachineId,
    string? MachineName,
    int PanelStatus,
    long DefectBitCount,
    long TestedObjectCount,
    IReadOnlyList<AnalyseProductDefectCount> TopDefectBits);

public sealed record AnalysePanelSummaryResult(
    SourceDescriptor Source,
    AnalyseDashboardFilter Filter,
    PanelYieldKpi OverallYield,
    DpmoKpi OverallDpmo,
    int TotalPanels,
    IReadOnlyList<AnalysePanelSummaryRow> Panels,
    bool DedupeAppliedInMemory,
    string? DedupeNote);
