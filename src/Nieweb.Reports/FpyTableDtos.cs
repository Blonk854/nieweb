using Nieweb.DataSources;

namespace Nieweb.Reports;

/// <summary>
/// Whether an FPY table aggregates whole-panel or per-board rows
/// (Vieweb §3.1.6.4: "FPY analysis can be done on panels or boards").
/// </summary>
public enum FpyGranularity
{
    /// <summary>Panel-level FPY (uses <c>PANELS.Panel_Status</c>).</summary>
    Panel = 0,

    /// <summary>Board / sub-panel-level FPY (uses <c>CARDS.Card_Status</c>).</summary>
    Board = 1,
}

/// <summary>
/// Column-grouping axis for an FPY table (Vieweb §3.1.6.4: "FPY tables
/// can show data by AOI or by product").
/// </summary>
public enum FpyGroupBy
{
    /// <summary>One row per <c>Machine_Id</c>.</summary>
    AoiMachine = 0,

    /// <summary>One row per <c>Product_Id</c>.</summary>
    Product = 1,
}

/// <summary>
/// Filter accepted by <see cref="FpyTableReport"/>.
/// </summary>
/// <param name="Window">Half-open UTC time window over <c>Panel_Numeric_Date</c>.</param>
/// <param name="Granularity">Panel-level or board-level FPY.</param>
/// <param name="GroupBy">Rows grouped by AOI machine or product.</param>
/// <param name="MachineIds">Optional restriction to a subset of AOI machines.</param>
/// <param name="ProductIds">Optional restriction to a subset of products.</param>
/// <param name="RecipeIds">Optional restriction to a subset of recipes.</param>
/// <param name="OnlyLastInspection">
/// When <c>true</c> (default) and the source supports it, restricts to the
/// most recent inspection of each panel. Sources without
/// <see cref="Capabilities.IsLastInspectionFilter"/> silently ignore this.
/// </param>
public sealed record FpyTableFilter(
    DateRange Window,
    FpyGranularity Granularity,
    FpyGroupBy GroupBy,
    IReadOnlyCollection<int>? MachineIds = null,
    IReadOnlyCollection<int>? ProductIds = null,
    IReadOnlyCollection<int>? RecipeIds = null,
    bool OnlyLastInspection = true);

/// <summary>
/// FPY / status counts for a single scope (row-level or grand total).
/// All three FPY flavours share the same denominator
/// (<see cref="InspectedCount"/>) so a UI can present them side-by-side
/// without weighted-average bugs (legacy Vieweb bug #12421).
/// </summary>
/// <remarks>
/// <para>
/// FPY definitions from Vieweb 1.6.2 glossary + <c>aoi-quality-metrics</c>
/// skill, using <c>Panel_Status</c> / <c>Card_Status</c> ∈
/// {-2, -1, 0, 1, 2, 3}:
/// </para>
/// <list type="bullet">
///   <item><description><c>FPY AOI = 100 · GoodAoi / Inspected</c> where <c>GoodAoi = count(status = 1)</c>. Raw AOI performance.</description></item>
///   <item><description><c>FPY Diagnostic = 100 · GoodDiagnostic / Inspected</c> where <c>GoodDiagnostic = count(status ∈ {1, 2})</c>. Excludes dummy faults categorised as OK by the repair operator.</description></item>
///   <item><description><c>FPY After Repair = 100 · GoodAfterRepair / Inspected</c> where <c>GoodAfterRepair = count(status ∈ {1, 2, 3})</c>. Includes repaired panels.</description></item>
///   <item><description><c>Inspected = count(status ≠ 0)</c> — everything except "not inspected".</description></item>
///   <item><description>All ratios return <c>0</c> when <c>Inspected = 0</c> (avoids division-by-zero divergence between summed and averaged buckets).</description></item>
/// </list>
/// </remarks>
public sealed record FpyKpi(
    long TotalRows,
    long InspectedCount,
    long NotInspectedCount,
    long FaultyCount,
    long GoodAoiCount,
    long GoodDiagnosticCount,
    long GoodAfterRepairCount,
    double FpyAoiPercent,
    double FpyDiagnosticPercent,
    double FpyAfterRepairPercent);

/// <summary>
/// One row of the FPY table result. <see cref="GroupKey"/> is the
/// discriminator (MachineId or ProductId depending on
/// <see cref="FpyTableFilter.GroupBy"/>). <see cref="GroupName"/>
/// resolves to the machine or product display name, or <c>null</c>
/// when the grouped id has no matching catalogue row.
/// </summary>
public sealed record FpyTableRow(
    int GroupKey,
    string? GroupName,
    FpyKpi Kpi);

/// <summary>
/// Result of running <see cref="FpyTableReport"/>. Rows are sorted
/// ascending by <see cref="FpyTableRow.Kpi"/>.<c>FpyAoiPercent</c>
/// (matching Vieweb §3.1.6.4: "table ordered by increasing FPY value").
/// Ties are broken by <see cref="FpyTableRow.GroupKey"/> to keep the
/// output stable for snapshot tests.
/// </summary>
public sealed record FpyTableResult(
    SourceDescriptor Source,
    DateRange Window,
    FpyGranularity Granularity,
    FpyGroupBy GroupBy,
    FpyKpi Overall,
    IReadOnlyList<FpyTableRow> Rows);
