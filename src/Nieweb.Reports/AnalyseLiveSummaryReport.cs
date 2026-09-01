using Nieweb.DataSources;

namespace Nieweb.Reports;

/// <summary>
/// ANA-03 first data slice: Live dashboard summary counters for panels
/// over a window, with capability-aware last-inspection behavior.
/// </summary>
public sealed class AnalyseLiveSummaryReport : IReport<AnalyseDashboardFilter, AnalyseLiveSummaryResult>
{
    public static readonly AnalyseLiveSummaryReport Instance = new();

    private AnalyseLiveSummaryReport()
    {
    }

    public ReportDescriptor Descriptor { get; } = new(
        Id: "analyse-live-summary",
        DisplayName: "Analyse Live Summary",
        Category: ReportCategory.Table,
        Description: "Live dashboard headline counters over a UTC window.");

    public async Task<AnalyseLiveSummaryResult> RunAsync(
        IAoiSource source,
        AnalyseDashboardFilter input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(input);

        var supportsLastInspection = source.Descriptor.Caps.HasFlag(Capabilities.IsLastInspectionFilter);
        var useInMemoryDedupe = input.OnlyLastInspection && !supportsLastInspection;

        var query = new PanelQuery
        {
            Window = input.Window,
            MachineIds = input.MachineIds,
            ProductIds = input.ProductIds,
            // Sources that support this flag apply dedupe at the DB level.
            // Sources that do not support it ignore it; we optionally dedupe
            // in-memory below.
            OnlyLastInspection = input.OnlyLastInspection,
        };

        var acc = new Accumulator();

        if (useInMemoryDedupe)
        {
            var latestPerPanel = new Dictionary<(string Barcode, int Face), PanelRow>(StringTupleComparer.Ordinal);
            await foreach (var panel in source.StreamPanelsAsync(query, cancellationToken).ConfigureAwait(false))
            {
                var key = (panel.PanelBarCode, panel.FaceNumber ?? -1);
                if (!latestPerPanel.TryGetValue(key, out var previous)
                    || panel.PanelNumericDate > previous.PanelNumericDate
                    || (panel.PanelNumericDate == previous.PanelNumericDate && panel.PanelId > previous.PanelId))
                {
                    latestPerPanel[key] = panel;
                }
            }

            foreach (var panel in latestPerPanel.Values)
            {
                acc.Add(panel.PanelStatus);
            }
        }
        else
        {
            await foreach (var panel in source.StreamPanelsAsync(query, cancellationToken).ConfigureAwait(false))
            {
                acc.Add(panel.PanelStatus);
            }
        }

        var kpi = acc.ToKpi();
        return new AnalyseLiveSummaryResult(
            Source: source.Descriptor,
            Filter: input,
            Kpi: kpi,
            DedupeAppliedInMemory: useInMemoryDedupe,
            DedupeNote: useInMemoryDedupe
                ? "Source lacks IS_LAST_INSPECTION; dedupe applied in memory by (Panel_Bar_Code, Face_Number)."
                : null);
    }

    private sealed class Accumulator
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

        public AnalyseLiveSummaryKpi ToKpi()
        {
            var inspected = _good + _faulty;
            var total = inspected + _notInspected;
            var fpy = inspected == 0 ? 0d : 100d * _good / inspected;
            return new AnalyseLiveSummaryKpi(
                TotalPanels: total,
                InspectedPanels: inspected,
                GoodPanels: _good,
                FaultyPanels: _faulty,
                NotInspectedPanels: _notInspected,
                FpyPercent: fpy);
        }
    }

    // ValueTuple<,> does not provide ordinal string semantics by default.
    private sealed class StringTupleComparer : IEqualityComparer<(string Barcode, int Face)>
    {
        public static readonly StringTupleComparer Ordinal = new();

        public bool Equals((string Barcode, int Face) x, (string Barcode, int Face) y)
            => StringComparer.Ordinal.Equals(x.Barcode, y.Barcode) && x.Face == y.Face;

        public int GetHashCode((string Barcode, int Face) obj)
            => HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Barcode), obj.Face);
    }
}

public sealed record AnalyseLiveSummaryResult(
    SourceDescriptor Source,
    AnalyseDashboardFilter Filter,
    AnalyseLiveSummaryKpi Kpi,
    bool DedupeAppliedInMemory,
    string? DedupeNote);

public sealed record AnalyseLiveSummaryKpi(
    long TotalPanels,
    long InspectedPanels,
    long GoodPanels,
    long FaultyPanels,
    long NotInspectedPanels,
    double FpyPercent);
