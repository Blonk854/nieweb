using Microsoft.AspNetCore.Http.HttpResults;
using Nieweb.DataSources;
using Nieweb.Reports.Traceability;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Minimal-API endpoint group for the traceability drill-down (TC1):
/// panel → sub-panel → tested-object → pin lookups keyed by id or
/// barcode. All endpoints are read-only.
/// </summary>
public static class TraceabilityEndpoints
{
    /// <summary>
    /// Registers the <c>/api/traceability</c> endpoints on
    /// <paramref name="routes"/>. Requires authentication (any
    /// signed-in user) — TC1 does not add a new authorization role.
    /// </summary>
    public static IEndpointRouteBuilder MapTraceabilityEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/traceability")
            .WithTags("Traceability")
            .RequireAuthorization();

        group.MapGet("/panels/{sourceId}/by-id/{panelId:int}", GetPanelByIdAsync)
            .WithName("TraceabilityPanelById");

        group.MapGet("/panels/{sourceId}/by-barcode", GetPanelByBarcodeAsync)
            .WithName("TraceabilityPanelByBarcode");

        group.MapGet("/panels/{sourceId}/{panelId:int}/subpanels", ListSubpanelsAsync)
            .WithName("TraceabilitySubpanels");

        group.MapGet("/panels/{sourceId}/{panelId:int}/subpanels/{cardId:int}/objects", ListTestedObjectsAsync)
            .WithName("TraceabilityObjects");

        group.MapGet("/panels/{sourceId}/{panelId:int}/subpanels/{cardId:int}/objects/{objectId:int}", GetTestedObjectAsync)
            .WithName("TraceabilityObjectDetail");

        group.MapGet("/panels/{sourceId}/{panelId:int}/failed-objects", ListFailedObjectsAsync)
            .WithName("TraceabilityFailedObjects");

        group.MapGet("/boards/by-barcode", GetBoardByBarcodeAsync)
            .WithName("TraceabilityBoardByBarcode");

        return routes;
    }

    private static async Task<Results<Ok<TraceabilityPanel>, NotFound, ProblemHttpResult>> GetPanelByIdAsync(
        string sourceId,
        int panelId,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        var (source, error) = ResolveSource(sourceId, sources);
        if (source is null)
        {
            return error!;
        }
        var result = await TraceabilityReport
            .GetPanelDetailAsync(source, panelId, cancellationToken)
            .ConfigureAwait(false);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<TraceabilityPanel>, NotFound, ProblemHttpResult>> GetPanelByBarcodeAsync(
        string sourceId,
        string? barcode,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        var (source, error) = ResolveSource(sourceId, sources);
        if (source is null)
        {
            return error!;
        }
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return TypedResults.Problem(
                title: "Missing required query parameter 'barcode'.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        if (barcode.Length > 64)
        {
            return TypedResults.Problem(
                title: $"Panel barcode must be 64 characters or fewer (got {barcode.Length}).",
                statusCode: StatusCodes.Status400BadRequest);
        }
        var result = await TraceabilityReport
            .GetPanelDetailByBarcodeAsync(source, barcode, cancellationToken)
            .ConfigureAwait(false);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<SubpanelsResponse>, NotFound, ProblemHttpResult>> ListSubpanelsAsync(
        string sourceId,
        int panelId,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        var (source, error) = ResolveSource(sourceId, sources);
        if (source is null)
        {
            return error!;
        }
        var result = await TraceabilityReport
            .ListSubpanelsForPanelAsync(source, panelId, cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(new SubpanelsResponse(result.Value.Panel, result.Value.Cards));
    }

    private static async Task<Results<Ok<TestedObjectsResponse>, NotFound, ProblemHttpResult>> ListTestedObjectsAsync(
        string sourceId,
        int panelId,
        int cardId,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        var (source, error) = ResolveSource(sourceId, sources);
        if (source is null)
        {
            return error!;
        }
        var result = await TraceabilityReport
            .ListTestedObjectsForSubpanelAsync(source, panelId, cardId, cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(new TestedObjectsResponse(result.Value.Subpanel, result.Value.Objects));
    }

    private static async Task<Results<Ok<TraceabilityTestedObject>, NotFound, ProblemHttpResult>> GetTestedObjectAsync(
        string sourceId,
        int panelId,
        int cardId,
        int objectId,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        var (source, error) = ResolveSource(sourceId, sources);
        if (source is null)
        {
            return error!;
        }
        var result = await TraceabilityReport
            .GetTestedObjectDetailAsync(source, panelId, cardId, objectId, cancellationToken)
            .ConfigureAwait(false);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }

    /// <summary>
    /// TC5 Phase C handler — returns every failed tested object on
    /// the given panel, aggregated across all sub-panels. 404 when
    /// the panel does not exist on <paramref name="sourceId"/>; 200
    /// with an empty <see cref="FailedObjectsResponse.Objects"/>
    /// list when the panel exists but has no failing rows (so the
    /// SPA can render "no failures" against the correct panel
    /// breadcrumb).
    /// </summary>
    private static async Task<Results<Ok<FailedObjectsResponse>, NotFound, ProblemHttpResult>> ListFailedObjectsAsync(
        string sourceId,
        int panelId,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        var (source, error) = ResolveSource(sourceId, sources);
        if (source is null)
        {
            return error!;
        }
        var result = await TraceabilityReport
            .ListFailedObjectsForPanelAsync(source, panelId, cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(new FailedObjectsResponse(result.Value.Panel, result.Value.Objects));
    }

    /// <summary>
    /// TC2 handler — cross-DB board trace by barcode. Always returns
    /// 200 with one stage per configured source when at least one
    /// stage matched or errored; 404 when every stage returned
    /// <c>Panel = null</c> and no error (barcode never seen on any
    /// DB); 400 on missing / oversized barcode or malformed
    /// <c>panelId</c> syntax. Unknown source ids in <c>panelId</c>
    /// pins are dropped (not 400).
    /// </summary>
    private static async Task<Results<Ok<BoardTrace>, NotFound, ProblemHttpResult>> GetBoardByBarcodeAsync(
        string? barcode,
        string[]? panelId,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return TypedResults.Problem(
                title: "Missing required query parameter 'barcode'.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        if (barcode.Length > 64)
        {
            return TypedResults.Problem(
                title: $"Panel barcode must be 64 characters or fewer (got {barcode.Length}).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var knownSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            knownSources.Add(source.Descriptor.Id);
        }

        Dictionary<string, int>? selectedPanelIds = null;
        if (panelId is { Length: > 0 })
        {
            selectedPanelIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in panelId)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return TypedResults.Problem(
                        title: "Malformed panelId query parameter (expected sourceId:panelId).",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var colon = raw.IndexOf(':');
                if (colon <= 0 || colon >= raw.Length - 1)
                {
                    return TypedResults.Problem(
                        title: $"Malformed panelId '{raw}' (expected sourceId:panelId).",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var sourceKey = raw[..colon];
                var idText = raw[(colon + 1)..];
                if (string.IsNullOrWhiteSpace(sourceKey)
                    || !int.TryParse(idText, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsedId)
                    || parsedId <= 0)
                {
                    return TypedResults.Problem(
                        title: $"Malformed panelId '{raw}' (expected sourceId:panelId).",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                // Unknown / unconfigured source → drop silently so a
                // renamed source never blanks a valid barcode lookup.
                if (!knownSources.Contains(sourceKey))
                {
                    continue;
                }

                // Last-wins when the same source is pinned twice.
                selectedPanelIds[sourceKey] = parsedId;
            }

            if (selectedPanelIds.Count == 0)
            {
                selectedPanelIds = null;
            }
        }

        var result = await TraceabilityReport
            .GetBoardByBarcodeAsync(sources, barcode, selectedPanelIds, cancellationToken)
            .ConfigureAwait(false);

        if (result is null || result.Stages.Count == 0)
        {
            return TypedResults.NotFound();
        }

        var anyMatch = false;
        var anyError = false;
        foreach (var stage in result.Stages)
        {
            if (stage.Sides.Count > 0)
            {
                anyMatch = true;
            }
            if (stage.Error is not null)
            {
                anyError = true;
            }
        }
        if (!anyMatch && !anyError)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(result);
    }

    private static (IAoiSource? Source, ProblemHttpResult? Error) ResolveSource(
        string? sourceId, IEnumerable<IAoiSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return (null, TypedResults.Problem(
                title: "Missing required route parameter 'sourceId'.",
                statusCode: StatusCodes.Status400BadRequest));
        }
        foreach (var source in sources)
        {
            if (string.Equals(source.Descriptor.Id, sourceId, StringComparison.OrdinalIgnoreCase))
            {
                return (source, null);
            }
        }
        return (null, TypedResults.Problem(
            title: $"Unknown sourceId '{sourceId}'.",
            statusCode: StatusCodes.Status404NotFound));
    }
}

/// <summary>
/// Response body for
/// <c>GET /api/traceability/panels/{sourceId}/{panelId}/subpanels</c>.
/// The panel is repeated in every list response so the SPA can render
/// breadcrumbs without a second round-trip.
/// </summary>
public sealed record SubpanelsResponse(
    TraceabilityPanel Panel,
    IReadOnlyList<Nieweb.DataSources.CardRow> Cards);

/// <summary>
/// Response body for
/// <c>GET /api/traceability/panels/{sourceId}/{panelId}/subpanels/{cardId}/objects</c>.
/// </summary>
public sealed record TestedObjectsResponse(
    TraceabilitySubpanel Subpanel,
    IReadOnlyList<Nieweb.DataSources.TestedObjectRow> Objects);

/// <summary>
/// TC5 Phase C — response body for
/// <c>GET /api/traceability/panels/{sourceId}/{panelId}/failed-objects</c>.
/// Carries the panel breadcrumb (so the SPA can render "Panel …"
/// context without a second round-trip) plus every failing tested
/// object across all sub-panels, ordered by <c>Card_Number</c> then
/// <c>Tested_Object_Id</c>.
/// </summary>
public sealed record FailedObjectsResponse(
    TraceabilityPanel Panel,
    IReadOnlyList<Nieweb.DataSources.TestedObjectRow> Objects);
