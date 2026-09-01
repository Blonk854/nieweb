using Nieweb.DataSources;

namespace Nieweb.Reports;

/// <summary>
/// Shared filter contract for Analyse dashboards. This is the common
/// envelope ANA-02..ANA-06 routes build on.
/// </summary>
public sealed record AnalyseDashboardFilter(
    DateRange Window,
    IReadOnlyCollection<int>? MachineIds = null,
    IReadOnlyCollection<int>? ProductIds = null,
    bool OnlyLastInspection = true);

/// <summary>Canonical Analyse dashboard ids.</summary>
public enum AnalyseDashboardId
{
    Live = 0,
    LinePerformance = 1,
    Product = 2,
    Panel = 3,
    CpCpk = 4,
}

/// <summary>
/// Support status for one dashboard feature toggle (for example a tile
/// that needs a source capability not present on pre-reflow).
/// </summary>
public sealed record AnalyseFeatureAvailability(
    string FeatureId,
    bool Supported,
    string? MissingCapability,
    string? Note = null);

/// <summary>
/// Source support status for one Analyse dashboard.
/// </summary>
public sealed record AnalyseDashboardAvailability(
    AnalyseDashboardId Dashboard,
    bool Supported,
    IReadOnlyList<string> MissingCapabilities,
    IReadOnlyList<AnalyseFeatureAvailability> Features);

/// <summary>
/// Result envelope returned by the Analyse contract scaffold.
/// </summary>
public sealed record AnalyseDashboardContractsResult(
    SourceDescriptor Source,
    AnalyseDashboardFilter Filter,
    IReadOnlyList<AnalyseDashboardAvailability> Dashboards);
