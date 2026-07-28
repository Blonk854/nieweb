using Nieweb.DataSources;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Minimal-API endpoint group for AOI data-source discovery:
/// <c>GET /api/sources</c> returns every configured source's descriptor,
/// capability set, and freshness timestamp.
/// </summary>
/// <remarks>
/// The freshness timestamp is derived by asking each source for the
/// latest <c>Panel_Numeric_Date</c> in its <c>PANELS</c> table
/// (see <see cref="IAoiSource.GetLatestPanelUtcAsync"/>). That call
/// touches the production DB but is a single indexed <c>MAX()</c> and is
/// safe under the read-only discipline. Individual failures degrade to
/// <c>latestPanelUtc = null</c> and a warning log entry rather than
/// failing the whole response, so a temporarily unreachable source does
/// not break source discovery for the rest.
/// </remarks>
public static partial class SourceEndpoints
{
    /// <summary>
    /// Registers the <c>/api/sources</c> endpoints on <paramref name="routes"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/sources")
            .WithTags("Sources")
            .RequireAuthorization();

        group.MapGet(string.Empty, ListSourcesAsync)
            .WithName("SourcesList");

        group.MapGet("/{id}/machines", ListMachinesAsync)
            .WithName("SourcesListMachines");

        group.MapGet("/{id}/operators", ListOperatorsAsync)
            .WithName("SourcesListOperators");

        group.MapGet("/{id}/products", ListProductsAsync)
            .WithName("SourcesListProducts");

        group.MapGet("/{id}/active-filters", ListActiveFiltersAsync)
            .WithName("SourcesListActiveFilters");

        return routes;
    }

    /// <summary>
    /// One item in the <c>GET /api/sources</c> response.
    /// </summary>
    /// <param name="Id">Stable id, e.g. <c>"postreflow"</c> / <c>"prereflow"</c>.</param>
    /// <param name="DisplayName">Human-facing label.</param>
    /// <param name="SchemaVersion">Vision3D CR4/CR5 schema string, e.g. <c>"5.0"</c>.</param>
    /// <param name="Capabilities">The names of every <see cref="Capabilities"/> flag the source advertises.</param>
    /// <param name="LatestPanelUtc">Wall-clock UTC of the most recent <c>PANELS</c> row, or <c>null</c> if empty/unreachable.</param>
    /// <param name="Available"><c>true</c> if the freshness probe succeeded; <c>false</c> if it threw.</param>
    public sealed record SourceInfo(
        string Id,
        string DisplayName,
        string SchemaVersion,
        IReadOnlyList<string> Capabilities,
        DateTime? LatestPanelUtc,
        bool Available);

    private static async Task<IResult> ListSourcesAsync(
        IEnumerable<IAoiSource> sources,
        ILogger<SourcesMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        // Kick off freshness probes in parallel so a slow source does not
        // serialize the response. Each probe swallows its own exception
        // and returns Available=false; the endpoint stays 200 OK.
        var infos = await Task.WhenAll(
            sources.Select(s => ToInfoAsync(s, logger, cancellationToken)))
            .ConfigureAwait(false);

        // Stable, alphabetical ordering by Id so front-end diffs are quiet.
        return Results.Ok(infos.OrderBy(i => i.Id, StringComparer.Ordinal).ToArray());
    }

    private static async Task<SourceInfo> ToInfoAsync(
        IAoiSource source,
        ILogger logger,
        CancellationToken ct)
    {
        DateTime? latest = null;
        var available = true;
        try
        {
            latest = await source.GetLatestPanelUtcAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // catch general exception - defensive per-source isolation is the whole point
        catch (Exception ex)
        {
            available = false;
            LogFreshnessProbeFailed(logger, source.Descriptor.Id, ex);
        }
#pragma warning restore CA1031

        return new SourceInfo(
            Id: source.Descriptor.Id,
            DisplayName: source.Descriptor.DisplayName,
            SchemaVersion: source.Descriptor.SchemaVersion,
            Capabilities: ExpandCapabilities(source.Descriptor.Caps),
            LatestPanelUtc: latest,
            Available: available);
    }

    /// <summary>
    /// Expands a <see cref="Capabilities"/> flag set into the sorted list
    /// of set-flag names, excluding the sentinel <c>None</c> value so an
    /// empty capability set serializes to <c>[]</c> rather than <c>["None"]</c>.
    /// </summary>
    private static List<string> ExpandCapabilities(Capabilities caps)
    {
        var result = new List<string>();
        foreach (var value in Enum.GetValues<Capabilities>())
        {
            if (value == Capabilities.None)
            {
                continue;
            }
            if (caps.HasFlag(value))
            {
                result.Add(value.ToString());
            }
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>
    /// One item in the <c>GET /api/sources/{id}/machines</c> response.
    /// Slimmer than the raw <see cref="Machine"/> record: the UI only
    /// needs the id + display strings for the multi-select.
    /// </summary>
    public sealed record MachineOption(int Id, string Name, string? TypeName);

    /// <summary>
    /// One item in the <c>GET /api/sources/{id}/operators</c> response.
    /// Slimmer than the raw <see cref="ReviewOperator"/> record: the traceability
    /// UI only needs the id → name lookup so it can render a review
    /// operator by numeric id from a <c>TESTED_OBJECT.Operator_Id</c> row.
    /// </summary>
    public sealed record OperatorOption(int Id, string Name);

    /// <summary>One item in <c>GET /api/sources/{id}/products</c>.</summary>
    public sealed record ProductOption(int Id, string Name, string? Revision);

    /// <summary>
    /// A distinct (Machine_Id, Product_Id) pair that produced a panel inside
    /// the requested window (one item of the <c>active-filters</c> response).
    /// </summary>
    public sealed record ActiveFilterPair(int MachineId, int ProductId);

    /// <summary>
    /// Response for <c>GET /api/sources/{id}/active-filters</c>. The UI derives
    /// the cascaded machine / product dropdown contents from this pair set so
    /// it only offers combinations that actually ran in the window.
    /// </summary>
    public sealed record ActiveFiltersResponse(IReadOnlyList<ActiveFilterPair> Pairs);

    private static IAoiSource? FindSource(IEnumerable<IAoiSource> sources, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        return sources.FirstOrDefault(s =>
            string.Equals(s.Descriptor.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static IResult SourceNotFound(string? id) =>
        Results.Problem(
            title: "Unknown source id.",
            detail: $"No AOI source is registered with id '{id}'.",
            statusCode: StatusCodes.Status404NotFound);

    private static async Task<IResult> ListMachinesAsync(
        string id,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        var source = FindSource(sources, id);
        if (source is null)
        {
            return SourceNotFound(id);
        }
        var raw = await source.ListMachinesAsync(cancellationToken).ConfigureAwait(false);
        var options = raw
            .Select(m => new MachineOption(m.MachineId, m.MachineName, m.MachineTypeName))
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Id)
            .ToArray();
        return Results.Ok(options);
    }

    private static async Task<IResult> ListOperatorsAsync(
        string id,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        var source = FindSource(sources, id);
        if (source is null)
        {
            return SourceNotFound(id);
        }
        var raw = await source.ListOperatorsAsync(cancellationToken).ConfigureAwait(false);
        var options = raw
            .Select(o => new OperatorOption(o.OperatorId, o.OperatorName))
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Id)
            .ToArray();
        return Results.Ok(options);
    }

    private static async Task<IResult> ListProductsAsync(
        string id,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        var source = FindSource(sources, id);
        if (source is null)
        {
            return SourceNotFound(id);
        }
        var raw = await source.ListProductsAsync(cancellationToken).ConfigureAwait(false);
        var options = raw
            .Select(p => new ProductOption(p.ProductId, p.ProductName ?? string.Empty, p.Revision))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id)
            .ToArray();
        return Results.Ok(options);
    }

    /// <summary>
    /// <c>GET /api/sources/{id}/active-filters?startUtc=&amp;endUtc=</c>.
    /// Returns the distinct (machine, product) pairs that produced a panel in
    /// the window, so the UI can cascade its machine / product dropdowns.
    /// A single windowed <c>SELECT DISTINCT</c> — same read-only safety class
    /// as the freshness probe.
    /// </summary>
    private static async Task<IResult> ListActiveFiltersAsync(
        string id,
        string? startUtc,
        string? endUtc,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        var source = FindSource(sources, id);
        if (source is null)
        {
            return SourceNotFound(id);
        }
        if (!TryParseWindow(startUtc, endUtc, out var window, out var error))
        {
            return error;
        }
        var keys = await source.ListActivePanelKeysAsync(window, cancellationToken).ConfigureAwait(false);
        var pairs = keys
            .Select(k => new ActiveFilterPair(k.MachineId, k.ProductId))
            .ToArray();
        return Results.Ok(new ActiveFiltersResponse(pairs));
    }

    /// <summary>
    /// Parses the <c>startUtc</c> / <c>endUtc</c> query pair into a
    /// <see cref="DateRange"/>, or returns <c>false</c> with a 400 problem in
    /// <paramref name="error"/>.
    /// </summary>
    private static bool TryParseWindow(
        string? startUtc,
        string? endUtc,
        out DateRange window,
        out IResult error)
    {
        window = default;
        error = Results.Problem(
            title: "Invalid window.",
            detail: "startUtc and endUtc must be ISO-8601 instants with endUtc after startUtc.",
            statusCode: StatusCodes.Status400BadRequest);

        if (!TryParseUtc(startUtc, out var start) || !TryParseUtc(endUtc, out var end) || end <= start)
        {
            return false;
        }
        try
        {
            window = new DateRange(start, end);
        }
        catch (ArgumentException)
        {
            return false;
        }
        error = null!;
        return true;
    }

    private static bool TryParseUtc(string? raw, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }
        if (DateTimeOffset.TryParse(
                raw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal
                    | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            value = parsed.ToUniversalTime();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Marker type used only to name the <see cref="ILogger{T}"/> category
    /// for <c>/api/sources</c>.
    /// </summary>
    public sealed class SourcesMarker
    {
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "Freshness probe failed for source '{SourceId}'; reporting Available=false")]
    private static partial void LogFreshnessProbeFailed(ILogger logger, string sourceId, Exception exception);
}
