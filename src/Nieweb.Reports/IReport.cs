using Nieweb.DataSources;

namespace Nieweb.Reports;

/// <summary>
/// Contract implemented by every Nieweb report. A report reads from an
/// <see cref="IAoiSource"/>, projects the raw AOI facts into an
/// aggregated output shape (<typeparamref name="TOutput"/>), and stays
/// pure: no writes, no side effects, no ambient state.
/// </summary>
/// <typeparam name="TInput">
/// Filter DTO the caller supplies. Must be a stable, versionable record
/// (report inputs get serialized into batch schedules and audit logs).
/// </typeparam>
/// <typeparam name="TOutput">
/// Result DTO the report produces. Should be self-describing enough to
/// round-trip through JSON without additional metadata (the REST layer
/// serializes it verbatim).
/// </typeparam>
/// <remarks>
/// <para>
/// This interface is the RI1 foundation of Phase 2 (docs/phase-2.md
/// §7.1). Individual reports live in <c>Nieweb.Reports</c>; they are
/// instantiated once (they are stateless) and can be registered in DI
/// as singletons. The shared snapshot-test scaffold in
/// <c>Nieweb.Reports.TestKit</c> exercises any <see cref="IReport{TInput, TOutput}"/>
/// without knowing its concrete type.
/// </para>
/// <para>
/// Implementations must not mutate the input, must honour the
/// cancellation token on every I/O boundary, and must aggregate counts
/// before dividing (count-first / divide-last) so legacy Vieweb bug
/// #12421 (per-bucket totals disagreeing with the summed bucket total)
/// cannot recur.
/// </para>
/// </remarks>
public interface IReport<in TInput, TOutput>
    where TInput : notnull
    where TOutput : notnull
{
    /// <summary>
    /// Static metadata about the report: identity, display name, and
    /// which of the five Vieweb entity families it belongs to. Used by
    /// the UI catalogue, the batch scheduler, and the audit log.
    /// </summary>
    ReportDescriptor Descriptor { get; }

    /// <summary>
    /// Runs the report against <paramref name="source"/> using
    /// <paramref name="input"/>. Cancellable via
    /// <paramref name="cancellationToken"/>.
    /// </summary>
    Task<TOutput> RunAsync(
        IAoiSource source,
        TInput input,
        CancellationToken cancellationToken);
}

/// <summary>
/// Static metadata about a report, mirroring the legacy Vieweb
/// <c>Report</c> entity's identity + category fields. Populated once
/// per implementation and exposed via <see cref="IReport{TInput, TOutput}.Descriptor"/>.
/// </summary>
/// <param name="Id">
/// Stable slug (kebab-case) that survives renames and appears in URLs,
/// batch schedules, and audit logs. Must be globally unique.
/// </param>
/// <param name="DisplayName">
/// Human-readable name shown in the UI catalogue. Localization is
/// applied at the presentation layer via an i18n key derived from
/// <see cref="Id"/>; this field is the English canonical name.
/// </param>
/// <param name="Category">
/// One of the five Vieweb entity families
/// (<see cref="ReportCategory"/>).
/// </param>
/// <param name="Description">
/// Optional short blurb (one sentence) surfaced next to the report in
/// the catalogue. Not translated automatically.
/// </param>
public sealed record ReportDescriptor(
    string Id,
    string DisplayName,
    ReportCategory Category,
    string? Description = null);

/// <summary>
/// The five Vieweb-legacy report families. Nieweb keeps these names
/// intact so historical rows in the internal DB remain queryable, and
/// so operators moving from Vieweb to Nieweb see the same taxonomy.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>
///     <term><see cref="Table"/></term>
///     <description>Row/column aggregations (FPY table, DPMO table, …).
///     Vieweb <c>TableEntity</c>.</description>
///   </item>
///   <item>
///     <term><see cref="Chart"/></term>
///     <description>Pareto / deviation / trend charts. Vieweb
///     <c>GraphEntity</c>.</description>
///   </item>
///   <item>
///     <term><see cref="ProcessCapability"/></term>
///     <description>Cp/Cpk plus per-line KPI grid. Vieweb
///     <c>ProcessCapabilityEntity</c>.</description>
///   </item>
///   <item>
///     <term><see cref="Traceability"/></term>
///     <description>Barcode / lot / serial-number drill-downs. Vieweb
///     <c>TracabilityEntity</c> (sic).</description>
///   </item>
///   <item>
///     <term><see cref="TestEmptyMaster"/></term>
///     <description>Empty-panel golden-master tests. Vieweb
///     <c>TestEmptyMasterEntity</c>. Not delivered in Phase 2 (see
///     docs/phase-2.md §9).</description>
///   </item>
/// </list>
/// <para>
/// MSA (Cp/Cpk/GR&amp;R on a dedicated empty-panel DB) is deferred
/// (docs/phase-2.md §10 Q1) and so has no enum member yet.
/// </para>
/// </remarks>
public enum ReportCategory
{
    Table,
    Chart,
    ProcessCapability,
    Traceability,
    TestEmptyMaster,
}
