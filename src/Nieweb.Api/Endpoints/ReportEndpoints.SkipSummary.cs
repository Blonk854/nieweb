using Nieweb.Api.SkipClassification;
using Nieweb.DataSources;
using Nieweb.Reports;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Skip-summary endpoint (<c>GET /api/reports/skip-summary</c>) wired
/// over <see cref="SkipSummaryReport"/>. Reports how many sub-panels in
/// the window were skipped (manual X-OUT, machine skip mark, or the
/// disabled-skip missing heuristic) so FPY / DPMO can be read on the
/// clean population.
/// </summary>
public static partial class ReportEndpoints
{
    /// <summary>
    /// <c>GET /api/reports/skip-summary</c>. Returns a
    /// <see cref="SkipSummaryResult"/> for the requested source /
    /// window. Uses the default skip-classification thresholds
    /// (<c>SkipClassificationConfig.Default</c>); site overrides arrive
    /// with the admin configuration surface.
    /// </summary>
    /// <param name="sourceId">Registered <see cref="SourceDescriptor.Id"/>.</param>
    /// <param name="startUtc">Window start, inclusive.</param>
    /// <param name="endUtc">Window end, exclusive.</param>
    /// <param name="machineIds">Optional comma-separated int list.</param>
    /// <param name="productIds">Optional comma-separated int list.</param>
    /// <param name="onlyLastInspection">
    /// When <c>true</c> (default) and supported, restricts to the most
    /// recent inspection of each panel.
    /// </param>
    /// <param name="sources">All registered AOI sources (DI-injected).</param>
    /// <param name="skipConfigProvider">Resolves the admin-tuned skip thresholds.</param>
    /// <param name="logger">Endpoint logger.</param>
    /// <param name="cancellationToken">Request abort signal.</param>
    private static async Task<IResult> RunSkipSummaryAsync(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        var baseParse = TryBuildBaseRequest(sourceId, startUtc, endUtc, sources);
        if (baseParse.Error is not null)
        {
            return baseParse.Error;
        }

        var filter = new SkipSummaryFilter(
            Window: baseParse.Window,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            OnlyLastInspection: onlyLastInspection ?? true,
            Config: await skipConfigProvider.GetAsync(cancellationToken).ConfigureAwait(false));

        LogRunningSkipSummary(
            logger,
            baseParse.Source!.Descriptor.Id,
            filter.Window.StartUtc,
            filter.Window.EndUtcExclusive);

        var result = await SkipSummaryReport.Instance
            .RunAsync(baseParse.Source, filter, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(result);
    }

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information,
        Message = "Running skip-summary on '{SourceId}' for window {StartUtc:o}..{EndUtc:o}")]
    private static partial void LogRunningSkipSummary(
        ILogger logger,
        string sourceId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc);
}
