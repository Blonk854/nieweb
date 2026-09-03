using Nieweb.DataSources;

namespace Nieweb.Reports;

/// <summary>
/// ANA-06 Cp/Cpk dashboard slice (AOI-only): process capability per deviation
/// axis computed from TESTED_OBJECT Delta_* samples in the window.
///
/// Canonical formulas (aoi-quality-metrics, do NOT re-derive):
///   Cp  = IT / (6σ)
///   Cpk = min(IT/2 − d̄, IT/2 + d̄) / (3σ)
/// where IT is the tolerance interval for the axis, d̄ the sample mean,
/// and σ the Bessel-corrected sample std-dev (Welford online).
///
/// Tolerance intervals come from AppParameter keys
/// (tolerance.component.itx/ity/its, tolerance.paste.itx/ity/its) resolved
/// at the endpoint layer and passed in via the filter. When IT is missing
/// or ≤ 0 the axis reports ToleranceConfigured=false and null Cp/Cpk —
/// matching how Vieweb behaved with untuned ViewebParameters.properties.
/// </summary>
public sealed class AnalyseCpCpkReport : IReport<AnalyseCpCpkFilter, AnalyseCpCpkResult>
{
    public static readonly AnalyseCpCpkReport Instance = new();

    private const int ObjectTypeComponentBit = 0x00000001;
    private const int ObjectTypePastePadBit = 0x00000010;

    private AnalyseCpCpkReport()
    {
    }

    public ReportDescriptor Descriptor { get; } = new(
        Id: "analyse-cp-cpk",
        DisplayName: "Analyse Cp/Cpk",
        Category: ReportCategory.Chart,
        Description: "Process capability (Cp/Cpk) per deviation axis from AOI samples.");

    public async Task<AnalyseCpCpkResult> RunAsync(
        IAoiSource source,
        AnalyseCpCpkFilter input,
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

        var cells = new Dictionary<(DeviationAxis Axis, DpmoOpportunity Opportunity), AxisAccumulator>();
        foreach (var axis in Enum.GetValues<DeviationAxis>())
        {
            cells[(axis, DpmoOpportunity.Components)] = new AxisAccumulator();
            cells[(axis, DpmoOpportunity.Paste)] = new AxisAccumulator();
        }

        var query = new TestedObjectQuery
        {
            Window = input.Window,
            MachineIds = input.MachineIds,
            ProductIds = input.ProductIds,
        };

        await foreach (var obj in source.StreamTestedObjectsAsync(query, cancellationToken).ConfigureAwait(false))
        {
            if (keptPanelIds is not null && !keptPanelIds.Contains((int)obj.PanelId))
            {
                continue;
            }

            DpmoOpportunity? opportunity = null;
            if ((obj.ObjectTypeId & ObjectTypeComponentBit) != 0)
            {
                opportunity = DpmoOpportunity.Components;
            }
            else if ((obj.ObjectTypeId & ObjectTypePastePadBit) != 0)
            {
                opportunity = DpmoOpportunity.Paste;
            }

            if (opportunity is null)
            {
                continue;
            }

            AddSample(cells[(DeviationAxis.DeltaX, opportunity.Value)], obj.DeltaXUm);
            AddSample(cells[(DeviationAxis.DeltaY, opportunity.Value)], obj.DeltaYUm);
            AddSample(cells[(DeviationAxis.DeltaTheta, opportunity.Value)], obj.DeltaThetaDeg);
            AddSample(cells[(DeviationAxis.DeltaThickness, opportunity.Value)], obj.DeltaThicknessUm);
            AddSample(cells[(DeviationAxis.DeltaSurface, opportunity.Value)], obj.DeltaSurface);
        }

        var rows = new List<AnalyseCpCpkRow>();
        foreach (var ((axis, opportunity), cell) in cells)
        {
            var it = ResolveTolerance(input, axis, opportunity);
            var (mean, stdDev) = cell.ToStats();
            double? cp = null;
            double? cpk = null;
            var configured = it is > 0 && cell.Count >= 2 && stdDev > 0;
            if (configured)
            {
                cp = it!.Value / (6 * stdDev);
                var half = it.Value / 2;
                cpk = Math.Min(half - mean, half + mean) / (3 * stdDev);
            }

            rows.Add(new AnalyseCpCpkRow(
                Axis: axis,
                Opportunity: opportunity,
                SampleCount: cell.Count,
                Mean: cell.Count == 0 ? null : mean,
                StdDev: cell.Count < 2 ? 0 : stdDev,
                Min: cell.Count == 0 ? null : cell.Min,
                Max: cell.Count == 0 ? null : cell.Max,
                ToleranceInterval: it,
                ToleranceConfigured: configured,
                Cp: cp,
                Cpk: cpk));
        }

        rows.Sort(static (a, b) =>
        {
            var c = a.Opportunity.CompareTo(b.Opportunity);
            return c != 0 ? c : a.Axis.CompareTo(b.Axis);
        });

        return new AnalyseCpCpkResult(
            Source: source.Descriptor,
            Filter: input,
            Rows: rows,
            DedupeAppliedInMemory: useInMemoryDedupe,
            DedupeNote: useInMemoryDedupe
                ? "Source lacks IS_LAST_INSPECTION; dedupe is applied in memory by panel id."
                : null);
    }

    private static double? ResolveTolerance(AnalyseCpCpkFilter input, DeviationAxis axis, DpmoOpportunity opportunity)
    {
        // ITx/ITy/ITS map onto DeltaX/DeltaY/DeltaSurface. Theta and Thickness
        // have no tolerance-interval key in Vieweb §2.4.2 → always unconfigured.
        if (axis is not (DeviationAxis.DeltaX or DeviationAxis.DeltaY or DeviationAxis.DeltaSurface))
        {
            return null;
        }

        double? raw = (opportunity, axis) switch
        {
            (DpmoOpportunity.Components, DeviationAxis.DeltaX) => input.ComponentItx,
            (DpmoOpportunity.Components, DeviationAxis.DeltaY) => input.ComponentIty,
            (DpmoOpportunity.Components, DeviationAxis.DeltaSurface) => input.ComponentIts,
            (DpmoOpportunity.Paste, DeviationAxis.DeltaX) => input.PasteItx,
            (DpmoOpportunity.Paste, DeviationAxis.DeltaY) => input.PasteIty,
            (DpmoOpportunity.Paste, DeviationAxis.DeltaSurface) => input.PasteIts,
            _ => null,
        };

        return raw is > 0 ? raw : null;
    }

    private static void AddSample(AxisAccumulator cell, double? value)
    {
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return;
        }

        cell.Add(value.Value);
    }

    private static async Task<HashSet<int>> BuildLatestPanelIdsAsync(
        IAoiSource source,
        DateRange window,
        IReadOnlyCollection<int>? machineIds,
        IReadOnlyCollection<int>? productIds,
        CancellationToken cancellationToken)
    {
        var latestByBarcodeFace = new Dictionary<(string Barcode, int Face), PanelRow>();
        var panelQuery = new PanelQuery
        {
            Window = window,
            MachineIds = machineIds,
            ProductIds = productIds,
            OnlyLastInspection = false,
        };

        await foreach (var panel in source.StreamPanelsAsync(panelQuery, cancellationToken).ConfigureAwait(false))
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

    private sealed class AxisAccumulator
    {
        private long _count;
        private double _mean;
        private double _m2;
        private double _min = double.PositiveInfinity;
        private double _max = double.NegativeInfinity;

        public long Count => _count;
        public double Min => _min;
        public double Max => _max;

        public void Add(double value)
        {
            _count++;
            var delta = value - _mean;
            _mean += delta / _count;
            _m2 += delta * (value - _mean);
            if (value < _min)
            {
                _min = value;
            }
            if (value > _max)
            {
                _max = value;
            }
        }

        public (double Mean, double StdDev) ToStats()
        {
            if (_count == 0)
            {
                return (0, 0);
            }

            if (_count == 1)
            {
                return (_mean, 0);
            }

            return (_mean, Math.Sqrt(_m2 / (_count - 1)));
        }
    }
}

/// <summary>
/// Filter for <see cref="AnalyseCpCpkReport"/>. Tolerance intervals are
/// resolved from AppParameter at the endpoint layer so the report stays pure.
/// Null or ≤ 0 means "not configured" for that axis.
/// </summary>
public sealed record AnalyseCpCpkFilter(
    DateRange Window,
    IReadOnlyCollection<int>? MachineIds = null,
    IReadOnlyCollection<int>? ProductIds = null,
    bool OnlyLastInspection = true,
    double? ComponentItx = null,
    double? ComponentIty = null,
    double? ComponentIts = null,
    double? PasteItx = null,
    double? PasteIty = null,
    double? PasteIts = null);

public sealed record AnalyseCpCpkRow(
    DeviationAxis Axis,
    DpmoOpportunity Opportunity,
    long SampleCount,
    double? Mean,
    double StdDev,
    double? Min,
    double? Max,
    double? ToleranceInterval,
    bool ToleranceConfigured,
    double? Cp,
    double? Cpk);

public sealed record AnalyseCpCpkResult(
    SourceDescriptor Source,
    AnalyseCpCpkFilter Filter,
    IReadOnlyList<AnalyseCpCpkRow> Rows,
    bool DedupeAppliedInMemory,
    string? DedupeNote);
