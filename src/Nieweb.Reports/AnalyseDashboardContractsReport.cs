using Nieweb.DataSources;

namespace Nieweb.Reports;

/// <summary>
/// ANA-01 scaffold report: computes source capability support for each
/// Analyse dashboard and its optional sub-features.
/// </summary>
public sealed class AnalyseDashboardContractsReport : IReport<AnalyseDashboardFilter, AnalyseDashboardContractsResult>
{
    public static readonly AnalyseDashboardContractsReport Instance = new();

    private AnalyseDashboardContractsReport()
    {
    }

    public ReportDescriptor Descriptor { get; } = new(
        Id: "analyse-contracts",
        DisplayName: "Analyse Contracts",
        Category: ReportCategory.Chart,
        Description: "Capability scaffold for AOI-only Analyse dashboards.");

    public Task<AnalyseDashboardContractsResult> RunAsync(
        IAoiSource source,
        AnalyseDashboardFilter input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var caps = source.Descriptor.Caps;

        var dashboards = new[]
        {
            BuildLive(caps),
            BuildLinePerformance(caps),
            BuildAlwaysSupported(AnalyseDashboardId.Product),
            BuildAlwaysSupported(AnalyseDashboardId.Panel),
            BuildAlwaysSupported(AnalyseDashboardId.CpCpk),
        };

        var result = new AnalyseDashboardContractsResult(
            Source: source.Descriptor,
            Filter: input,
            Dashboards: dashboards);

        return Task.FromResult(result);
    }

    private static AnalyseDashboardAvailability BuildLive(Capabilities caps)
    {
        var hasLastInspection = caps.HasFlag(Capabilities.IsLastInspectionFilter);
        return new AnalyseDashboardAvailability(
            Dashboard: AnalyseDashboardId.Live,
            Supported: true,
            MissingCapabilities: [],
            Features:
            [
                new AnalyseFeatureAvailability(
                    FeatureId: "latest-inspection-filter",
                    Supported: hasLastInspection,
                    MissingCapability: hasLastInspection ? null : Capabilities.IsLastInspectionFilter.ToString(),
                    Note: hasLastInspection
                        ? null
                        : "Source does not expose PANELS.IS_LAST_INSPECTION; live dedupe falls back to raw stream."),
            ]);
    }

    private static AnalyseDashboardAvailability BuildLinePerformance(Capabilities caps)
    {
        var hasTiming = caps.HasFlag(Capabilities.MachineEfficiencyTiming);
        return new AnalyseDashboardAvailability(
            Dashboard: AnalyseDashboardId.LinePerformance,
            Supported: true,
            MissingCapabilities: [],
            Features:
            [
                new AnalyseFeatureAvailability(
                    FeatureId: "machine-efficiency-time-pie",
                    Supported: hasTiming,
                    MissingCapability: hasTiming ? null : Capabilities.MachineEfficiencyTiming.ToString(),
                    Note: hasTiming
                        ? null
                        : "Source lacks PANELS timing columns; hide time-decomposition widgets."),
            ]);
    }

    private static AnalyseDashboardAvailability BuildAlwaysSupported(AnalyseDashboardId id)
        => new(
            Dashboard: id,
            Supported: true,
            MissingCapabilities: [],
            Features: []);
}
