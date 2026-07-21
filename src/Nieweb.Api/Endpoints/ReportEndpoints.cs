using System.Globalization;
using Nieweb.DataSources;
using Nieweb.Reports;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Minimal-API endpoint group for Nieweb reports:
/// <c>GET /api/reports/panel-yield</c> runs
/// <see cref="PanelYieldByLineReport"/> against a named source.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint is a thin HTTP shell over the pure report function -
/// it does not aggregate or format anything itself. This keeps the
/// report reusable (batch jobs, scheduled snapshots, tests) and makes
/// its numeric parity with Vieweb / Sigmalink Analyse a property of
/// the report project, not of the web layer.
/// </para>
/// <para>
/// The endpoint requires authentication. It never writes to the AOI
/// database - <see cref="IAoiSource"/> implementations are read-only
/// per the project-wide read-only discipline.
/// </para>
/// </remarks>
public static partial class ReportEndpoints
{
    /// <summary>
    /// Registers the <c>/api/reports</c> endpoints on <paramref name="routes"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/reports")
            .WithTags("Reports")
            .RequireAuthorization();

        group.MapGet("/panel-yield", RunPanelYieldAsync)
            .WithName("ReportsPanelYield");

        return routes;
    }

    /// <summary>
    /// <c>GET /api/reports/panel-yield</c>.
    /// </summary>
    /// <param name="sourceId">
    /// <see cref="SourceDescriptor.Id"/> of the source to query
    /// (e.g. <c>postreflow</c>, <c>prereflow</c>). Case-insensitive.
    /// </param>
    /// <param name="startUtc">Window start, inclusive. Any parseable ISO-8601 instant.</param>
    /// <param name="endUtc">Window end, exclusive. Must be strictly after <paramref name="startUtc"/>.</param>
    /// <param name="machineIds">Optional comma-separated list of machine ids to include.</param>
    /// <param name="productIds">Optional comma-separated list of product ids to include.</param>
    /// <param name="recipeIds">Optional comma-separated list of recipe ids to include.</param>
    /// <param name="onlyLastInspection">
    /// When <c>true</c> (default), restrict to each panel's latest inspection
    /// on sources that support <see cref="Capabilities.IsLastInspectionFilter"/>.
    /// </param>
    /// <param name="sources">All registered AOI sources (DI-injected).</param>
    /// <param name="logger">Endpoint logger.</param>
    /// <param name="cancellationToken">Request abort signal.</param>
    private static async Task<IResult> RunPanelYieldAsync(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        string? productIds,
        string? recipeIds,
        bool? onlyLastInspection,
        IEnumerable<IAoiSource> sources,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return Results.Problem(
                title: "Missing required query parameter 'sourceId'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var source = sources.FirstOrDefault(s =>
            string.Equals(s.Descriptor.Id, sourceId, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return Results.Problem(
                title: $"Unknown sourceId '{sourceId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!TryParseUtc(startUtc, out var start))
        {
            return Results.Problem(
                title: "Query parameter 'startUtc' is missing or not a valid ISO-8601 UTC instant.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        if (!TryParseUtc(endUtc, out var end))
        {
            return Results.Problem(
                title: "Query parameter 'endUtc' is missing or not a valid ISO-8601 UTC instant.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        if (end <= start)
        {
            return Results.Problem(
                title: "'endUtc' must be strictly after 'startUtc'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        DateRange window;
        try
        {
            window = new DateRange(start, end);
        }
#pragma warning disable CA1031 // catch general exception - report a client-friendly 400 for any DateRange rejection
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return Results.Problem(
                title: "Invalid window: " + ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
#pragma warning restore CA1031

        var filter = new PanelYieldFilter(
            Window: window,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            RecipeIds: ParseIntList(recipeIds),
            OnlyLastInspection: onlyLastInspection ?? true);

        LogRunning(logger, source.Descriptor.Id, start, end);
        var result = await PanelYieldByLineReport
            .RunAsync(source, filter, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(result);
    }

    /// <summary>
    /// Parses an ISO-8601 instant and normalizes it to UTC. Accepts both
    /// offset-bearing timestamps (<c>...+00:00</c>) and bare timestamps
    /// (which are assumed to be UTC to match how AOI PANELS timestamps
    /// are already stored).
    /// </summary>
    private static bool TryParseUtc(string? raw, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }
        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            value = parsed.ToUniversalTime();
            return true;
        }
        return false;
    }

    private static List<int>? ParseIntList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                list.Add(id);
            }
        }
        return list.Count == 0 ? null : list;
    }

    /// <summary>
    /// Marker type used only to name the <see cref="ILogger{T}"/> category
    /// for <c>/api/reports</c>.
    /// </summary>
    public sealed class ReportsMarker
    {
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Running panel-yield report on '{SourceId}' for window {StartUtc:o}..{EndUtc:o}")]
    private static partial void LogRunning(ILogger logger, string sourceId, DateTimeOffset startUtc, DateTimeOffset endUtc);
}
