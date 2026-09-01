using Nieweb.DataSources;

namespace Nieweb.Reports;

/// <summary>
/// ANA-03 line-performance summary: combines per-line FPY and component
/// DPMO so the dashboard can show a compact production overview before
/// the richer shift-specific analysis lands.
/// </summary>
public sealed class AnalyseLinePerformanceReport : IReport<AnalyseDashboardFilter, AnalyseLinePerformanceResult>
{
    public static readonly AnalyseLinePerformanceReport Instance = new();

    private const int ObjectTypeComponentBit = 0x00000001;

    private AnalyseLinePerformanceReport()
    {
    }

    public ReportDescriptor Descriptor { get; } = new(
        Id: "analyse-line-performance",
        DisplayName: "Analyse Line Performance",
        Category: ReportCategory.Chart,
        Description: "Per-line FPY and component DPMO over a UTC window.");

    public async Task<AnalyseLinePerformanceResult> RunAsync(
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
            keptPanelIds = await BuildLatestPanelIdsAsync(source, input.Window, input.MachineIds, input.ProductIds, cancellationToken)
                .ConfigureAwait(false);
        }

        var yieldOverall = new PanelAccumulator();
        var yieldByMachine = new Dictionary<int, PanelAccumulator>();
        var dpmoOverall = new DpmoAccumulator();
        var dpmoByMachine = new Dictionary<int, DpmoAccumulator>();

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

            yieldOverall.Add(panel.PanelStatus);
            if (!yieldByMachine.TryGetValue(panel.MachineId, out var yieldBucket))
            {
                yieldBucket = new PanelAccumulator();
                yieldByMachine[panel.MachineId] = yieldBucket;
            }
            yieldBucket.Add(panel.PanelStatus);
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

            var opportunities = card.NbOfTestsOnComp;
            dpmoOverall.AddOpportunities(opportunities);
            if (!dpmoByMachine.TryGetValue(card.MachineId, out var dpmoBucket))
            {
                dpmoBucket = new DpmoAccumulator();
                dpmoByMachine[card.MachineId] = dpmoBucket;
            }
            dpmoBucket.AddOpportunities(opportunities);
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
            dpmoOverall.AddTestedObject();
            dpmoOverall.AddDefects(defectBits);
            if (!dpmoByMachine.TryGetValue(obj.MachineId, out var dpmoBucket))
            {
                dpmoBucket = new DpmoAccumulator();
                dpmoByMachine[obj.MachineId] = dpmoBucket;
            }
            dpmoBucket.AddTestedObject();
            dpmoBucket.AddDefects(defectBits);
        }

        var machineNames = (await source.ListMachinesAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(m => m.MachineId, m => (string?)m.MachineName);

        var rows = yieldByMachine.Keys
            .Union(dpmoByMachine.Keys)
            .Distinct()
            .OrderBy(machineId => machineId)
            .Select(machineId => new AnalyseLinePerformanceLine(
                MachineId: machineId,
                MachineName: machineNames.TryGetValue(machineId, out var name) ? name : null,
                Yield: yieldByMachine.TryGetValue(machineId, out var y) ? y.ToKpi() : new PanelYieldKpi(0, 0, 0, 0, 0, 0),
                Dpmo: dpmoByMachine.TryGetValue(machineId, out var d) ? d.ToKpi() : new DpmoKpi(0, 0, 0, 0d)))
            .ToList();

        return new AnalyseLinePerformanceResult(
            Source: source.Descriptor,
            Filter: input,
            OverallYield: yieldOverall.ToKpi(),
            OverallDpmo: dpmoOverall.ToKpi(),
            ByMachine: rows,
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

        public void AddTestedObject() => _testedObjects++;

        public void AddOpportunities(long opportunities) => _opportunities += opportunities;

        public void AddDefects(long defects) => _defects += defects;

        public DpmoKpi ToKpi() => new(_testedObjects, _opportunities, _defects, _opportunities == 0 ? 0d : 1_000_000d * _defects / _opportunities);
    }
}

public sealed record AnalyseLinePerformanceResult(
    SourceDescriptor Source,
    AnalyseDashboardFilter Filter,
    PanelYieldKpi OverallYield,
    DpmoKpi OverallDpmo,
    IReadOnlyList<AnalyseLinePerformanceLine> ByMachine,
    bool DedupeAppliedInMemory,
    string? DedupeNote);

public sealed record AnalyseLinePerformanceLine(
    int MachineId,
    string? MachineName,
    PanelYieldKpi Yield,
    DpmoKpi Dpmo);