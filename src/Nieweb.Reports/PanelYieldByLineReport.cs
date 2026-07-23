using Nieweb.DataSources;

namespace Nieweb.Reports;

/// <summary>
/// The MVP vertical-slice report: panel-level yield per AOI machine
/// (a.k.a. per "line" once machines are grouped by production line
/// in a later phase).
/// </summary>
/// <remarks>
/// <para>
/// Streams <c>PANELS</c> rows for the requested window via
/// <see cref="IAoiSource.StreamPanelsAsync"/> and folds each row into a
/// per-machine bucket plus the grand total. Machine display names are
/// resolved once via <see cref="IAoiSource.ListMachinesAsync"/> at the
/// end of the fold (a single small read against the <c>MACHINE</c>
/// table); rows for machines missing from the catalogue still surface,
/// with <c>MachineName = null</c>.
/// </para>
/// <para>
/// The formula is the canonical FPY (AOI) definition: see the
/// <c>aoi-quality-metrics</c> skill and <see cref="PanelYieldKpi"/>
/// remarks for the exact status classification. Aggregation is
/// count-first / divide-last so this report cannot repeat legacy Vieweb
/// bug #12421 (weekly totals disagreeing with the sum of daily totals).
/// </para>
/// <para>
/// Since RI1 the report is an <see cref="IReport{TInput, TOutput}"/>
/// implementation. Call it through the <see cref="Instance"/> singleton
/// or resolve it from DI as
/// <c>IReport&lt;PanelYieldFilter, PanelYieldResult&gt;</c>.
/// </para>
/// </remarks>
public sealed class PanelYieldByLineReport : IReport<PanelYieldFilter, PanelYieldResult>
{
    /// <summary>
    /// The stable metadata for this report. Exposed statically so it
    /// can be referenced from tests and the report catalogue without
    /// having to instantiate the report class.
    /// </summary>
    public static readonly ReportDescriptor ReportDescriptor = new(
        Id: "panel-yield-by-line",
        DisplayName: "Panel yield by line",
        Category: ReportCategory.Table,
        Description: "Panel-level FPY per AOI machine over a UTC window.");

    /// <summary>
    /// Stateless singleton. The report holds no mutable fields, so a
    /// single instance is safe to share across all callers.
    /// </summary>
    public static readonly PanelYieldByLineReport Instance = new();

    /// <inheritdoc />
    public ReportDescriptor Descriptor => ReportDescriptor;

    /// <inheritdoc />
    /// <remarks>
    /// The class-level remarks describe the aggregation contract.
    /// </remarks>
    public async Task<PanelYieldResult> RunAsync(
        IAoiSource source,
        PanelYieldFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filter);

        var query = new PanelQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
            OnlyLastInspection = filter.OnlyLastInspection,
        };

        var overall = new Accumulator();
        var perMachine = new Dictionary<int, Accumulator>();

        await foreach (var panel in source.StreamPanelsAsync(query, cancellationToken).ConfigureAwait(false))
        {
            overall.Add(panel.PanelStatus);

            if (!perMachine.TryGetValue(panel.MachineId, out var bucket))
            {
                bucket = new Accumulator();
                perMachine[panel.MachineId] = bucket;
            }
            bucket.Add(panel.PanelStatus);
        }

        // Machine catalogue lookup is one small round trip; do it after
        // the streaming pass so the DB does not have to hold two
        // cursors open at once.
        var machines = await source.ListMachinesAsync(cancellationToken).ConfigureAwait(false);
        var machineNames = machines.ToDictionary(m => m.MachineId, m => m.MachineName);

        var byMachine = perMachine
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new PanelYieldByMachine(
                MachineId: kvp.Key,
                MachineName: machineNames.TryGetValue(kvp.Key, out var name) ? name : null,
                Kpi: kvp.Value.ToKpi()))
            .ToList();

        return new PanelYieldResult(
            Source: source.Descriptor,
            Window: filter.Window,
            Overall: overall.ToKpi(),
            ByMachine: byMachine);
    }

    /// <summary>
    /// Mutable panel-counter that translates statuses into the four
    /// count buckets and produces an immutable <see cref="PanelYieldKpi"/>
    /// at the end.
    /// </summary>
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
                    // Unknown status codes are counted as not-inspected so
                    // they do not corrupt FPY. The Superviseur schema
                    // documents {-2,-1,0,1,2,3} exhaustively today, so
                    // hitting this branch means the schema changed and
                    // the code (and the aoi-quality-metrics skill) needs
                    // an update.
                    _notInspected++;
                    break;
            }
        }

        public PanelYieldKpi ToKpi()
        {
            var inspected = _good + _faulty;
            var total = inspected + _notInspected;
            var fpy = inspected == 0 ? 0d : 100d * _good / inspected;
            return new PanelYieldKpi(
                TotalPanels: total,
                InspectedPanels: inspected,
                GoodPanels: _good,
                FaultyPanels: _faulty,
                NotInspectedPanels: _notInspected,
                FpyPercent: fpy);
        }
    }
}
