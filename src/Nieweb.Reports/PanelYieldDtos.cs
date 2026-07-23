using Nieweb.DataSources;

namespace Nieweb.Reports;

/// <summary>
/// Filter accepted by <see cref="PanelYieldByLineReport"/>.
/// A copy of the fields shared by every fact-table query that a Nieweb
/// report accepts, promoted to a report-layer DTO so the report is
/// consumable without a direct reference to the low-level query types.
/// </summary>
/// <param name="Window">Half-open UTC time window over <c>Panel_Numeric_Date</c>.</param>
/// <param name="MachineIds">Optional restriction to a subset of AOI machines.</param>
/// <param name="ProductIds">Optional restriction to a subset of products.</param>
/// <param name="OnlyLastInspection">
/// When <c>true</c> (default) and the source supports it, restricts to the
/// most recent inspection of each panel. Sources without
/// <see cref="Capabilities.IsLastInspectionFilter"/> silently ignore this.
/// </param>
public sealed record PanelYieldFilter(
    DateRange Window,
    IReadOnlyCollection<int>? MachineIds = null,
    IReadOnlyCollection<int>? ProductIds = null,
    bool OnlyLastInspection = true);

/// <summary>
/// Result of running the <see cref="PanelYieldByLineReport"/>.
/// </summary>
/// <param name="Source">Descriptor of the source the report was run against.</param>
/// <param name="Window">The filter window the report was run over.</param>
/// <param name="Overall">Aggregate KPIs across every inspected panel in the window.</param>
/// <param name="ByMachine">Per-machine breakdown, sorted by <c>MachineId</c> ascending.</param>
public sealed record PanelYieldResult(
    SourceDescriptor Source,
    DateRange Window,
    PanelYieldKpi Overall,
    IReadOnlyList<PanelYieldByMachine> ByMachine);

/// <summary>
/// Panel-level yield KPIs, computed exclusively from <c>PANELS.Panel_Status</c>.
/// </summary>
/// <remarks>
/// <para>
/// Status classification follows the VIT canonical definition
/// (see <c>aoi-quality-metrics</c> skill, "FPY" section):
/// </para>
/// <list type="bullet">
///   <item>Good  = <c>Panel_Status ∈ {1, 2, 3}</c></item>
///   <item>Faulty = <c>Panel_Status ∈ {-2, -1}</c></item>
///   <item>Not inspected = <c>Panel_Status == 0</c></item>
///   <item>Inspected = Good + Faulty (excludes not-inspected)</item>
///   <item><c>FpyPercent = 100 · Good / Inspected</c>, or <c>0</c> when nothing was inspected.</item>
/// </list>
/// <para>
/// The classification includes <c>3</c> to cover the pre-reflow schema
/// v4.3.1 status set <c>{-2, -1, 0, 1, 2, 3}</c>; on post-reflow v5.0
/// (<c>{-2, -1, 0, 1, 2}</c>) it is a no-op. Legacy bug #12421 (weekly
/// totals disagreed with daily totals) was rooted in averaging FPY
/// percentages instead of summing raw counts and dividing once at the
/// end - this aggregator only ever sums raw counts.
/// </para>
/// </remarks>
public sealed record PanelYieldKpi(
    long TotalPanels,
    long InspectedPanels,
    long GoodPanels,
    long FaultyPanels,
    long NotInspectedPanels,
    double FpyPercent);

/// <summary>
/// KPI slice for a single AOI machine.
/// </summary>
/// <param name="MachineId">Machine primary key from the source's <c>MACHINE</c> table.</param>
/// <param name="MachineName">
/// Display name resolved from <see cref="IAoiSource.ListMachinesAsync"/>,
/// or <c>null</c> if the machine appears in <c>PANELS</c> but not in the
/// machine catalogue (a legitimate corner case for decommissioned lines).
/// </param>
/// <param name="Kpi">Yield KPIs restricted to this machine.</param>
public sealed record PanelYieldByMachine(
    int MachineId,
    string? MachineName,
    PanelYieldKpi Kpi);
