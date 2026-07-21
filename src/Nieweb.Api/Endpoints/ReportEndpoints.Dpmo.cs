using System.Globalization;
using Nieweb.DataSources;
using Nieweb.Reports;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// DPMO table endpoint (<c>GET /api/reports/dpmo-table</c>) wired
/// over <see cref="DpmoTableReport"/>.
/// </summary>
public static partial class ReportEndpoints
{
    /// <summary>
    /// <c>GET /api/reports/dpmo-table</c>. Returns a
    /// <see cref="DpmoTableResult"/> for the requested source / window
    /// / group-by axis. Supports Vieweb's three numerators (AOI /
    /// Real / Dummy) and three opportunity filters (All / Components
    /// / Paste), and every group-by axis from Vieweb §3.1.6.5.
    /// </summary>
    /// <param name="sourceId">Registered <see cref="SourceDescriptor.Id"/>.</param>
    /// <param name="startUtc">Window start, inclusive.</param>
    /// <param name="endUtc">Window end, exclusive.</param>
    /// <param name="groupBy">
    /// Group-by axis. Accepts either the kebab-case slug
    /// (<c>aoi-machine</c>, <c>defect</c>, <c>product</c>,
    /// <c>reference-designator</c>, <c>part-number</c>, <c>jedec</c>)
    /// or the raw <see cref="DpmoGroupBy"/> member name.
    /// </param>
    /// <param name="numerator">
    /// One of <c>real</c> (default), <c>aoi</c>, or <c>dummy</c>.
    /// </param>
    /// <param name="opportunity">
    /// One of <c>all</c> (default), <c>components</c>, or <c>paste</c>.
    /// </param>
    /// <param name="machineIds">Optional comma-separated int list.</param>
    /// <param name="productIds">Optional comma-separated int list.</param>
    /// <param name="recipeIds">Optional comma-separated int list.</param>
    /// <param name="includeObsoleteBits">
    /// When <c>true</c> and <see cref="DpmoGroupBy.Defect"/>, emit rows
    /// for defect bits flagged obsolete in the catalogue. Default <c>false</c>.
    /// </param>
    /// <param name="sources">All registered AOI sources (DI-injected).</param>
    /// <param name="logger">Endpoint logger.</param>
    /// <param name="cancellationToken">Request abort signal.</param>
    private static async Task<IResult> RunDpmoTableAsync(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? groupBy,
        string? numerator,
        string? opportunity,
        string? machineIds,
        string? productIds,
        string? recipeIds,
        bool? includeObsoleteBits,
        IEnumerable<IAoiSource> sources,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        var built = TryBuildDpmoRequest(
            sourceId, startUtc, endUtc, groupBy, numerator, opportunity,
            machineIds, productIds, recipeIds, includeObsoleteBits, sources);
        if (built.Error is not null)
        {
            return built.Error;
        }

        LogRunningDpmo(
            logger,
            built.Source!.Descriptor.Id,
            built.Filter!.GroupBy,
            built.Filter.Numerator,
            built.Filter.Window.StartUtc,
            built.Filter.Window.EndUtcExclusive);

        var result = await DpmoTableReport.Instance
            .RunAsync(built.Source, built.Filter, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static (IAoiSource? Source, DpmoTableFilter? Filter, IResult? Error) TryBuildDpmoRequest(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? groupBy,
        string? numerator,
        string? opportunity,
        string? machineIds,
        string? productIds,
        string? recipeIds,
        bool? includeObsoleteBits,
        IEnumerable<IAoiSource> sources)
    {
        var baseParse = TryBuildBaseRequest(sourceId, startUtc, endUtc, sources);
        if (baseParse.Error is not null)
        {
            return (null, null, baseParse.Error);
        }

        if (!TryParseEnumAlias<DpmoGroupBy>(groupBy, required: true, out var groupByValue, out var error))
        {
            return (null, null, ProblemFor("groupBy", error!));
        }
        if (!TryParseEnumAlias<DpmoNumerator>(numerator, required: false, out var numeratorValue, out error, defaultValue: DpmoNumerator.Real))
        {
            return (null, null, ProblemFor("numerator", error!));
        }
        if (!TryParseEnumAlias<DpmoOpportunity>(opportunity, required: false, out var opportunityValue, out error, defaultValue: DpmoOpportunity.All))
        {
            return (null, null, ProblemFor("opportunity", error!));
        }

        var filter = new DpmoTableFilter(
            Window: baseParse.Window,
            GroupBy: groupByValue,
            Numerator: numeratorValue,
            Opportunity: opportunityValue,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            RecipeIds: ParseIntList(recipeIds),
            IncludeObsoleteBits: includeObsoleteBits ?? false);

        return (baseParse.Source, filter, null);
    }

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information,
        Message = "Running dpmo-table on '{SourceId}' groupBy={GroupBy} numerator={Numerator} for window {StartUtc:o}..{EndUtc:o}")]
    private static partial void LogRunningDpmo(
        ILogger logger,
        string sourceId,
        DpmoGroupBy groupBy,
        DpmoNumerator numerator,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc);

    /// <summary>
    /// Case-insensitive enum parser that also strips dashes so
    /// kebab-case URL parameters (e.g. <c>reference-designator</c>)
    /// match the underlying PascalCase enum member names.
    /// Returns <c>false</c> and populates <paramref name="error"/>
    /// with a client-safe message when the input is invalid.
    /// </summary>
    private static bool TryParseEnumAlias<TEnum>(
        string? raw,
        bool required,
        out TEnum value,
        out string? error,
        TEnum defaultValue = default)
        where TEnum : struct, Enum
    {
        value = defaultValue;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (required)
            {
                error = "value is required.";
                return false;
            }
            return true;
        }
        // Strip dashes and underscores so kebab / snake case match
        // the CLR PascalCase member names (Enum.TryParse is
        // case-insensitive already).
        var normalized = raw.Replace("-", string.Empty, StringComparison.Ordinal)
                            .Replace("_", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<TEnum>(normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            value = parsed;
            return true;
        }
        error = $"'{raw}' is not a valid {typeof(TEnum).Name}. Allowed values: {string.Join(", ", Enum.GetNames<TEnum>())}.";
        return false;
    }

    private static IResult ProblemFor(string field, string detail) =>
        Results.Problem(
            title: $"Query parameter '{field}' is invalid: {detail}",
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Shared source-id + window parser used by every non-panel-yield
    /// endpoint (DPMO, Pareto, and any future reports that consume
    /// the same three query parameters). Returns either
    /// (<paramref name="sources"/>-resolved source + validated window)
    /// or a ready-to-return 4xx <see cref="IResult"/>.
    /// </summary>
    private static (IAoiSource? Source, DateRange Window, IResult? Error) TryBuildBaseRequest(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        IEnumerable<IAoiSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return (null, default, Results.Problem(
                title: "Missing required query parameter 'sourceId'.",
                statusCode: StatusCodes.Status400BadRequest));
        }
        var source = sources.FirstOrDefault(s =>
            string.Equals(s.Descriptor.Id, sourceId, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return (null, default, Results.Problem(
                title: $"Unknown sourceId '{sourceId}'.",
                statusCode: StatusCodes.Status404NotFound));
        }
        if (!TryParseUtc(startUtc, out var start))
        {
            return (null, default, Results.Problem(
                title: "Query parameter 'startUtc' is missing or not a valid ISO-8601 UTC instant.",
                statusCode: StatusCodes.Status400BadRequest));
        }
        if (!TryParseUtc(endUtc, out var end))
        {
            return (null, default, Results.Problem(
                title: "Query parameter 'endUtc' is missing or not a valid ISO-8601 UTC instant.",
                statusCode: StatusCodes.Status400BadRequest));
        }
        if (end <= start)
        {
            return (null, default, Results.Problem(
                title: "'endUtc' must be strictly after 'startUtc'.",
                statusCode: StatusCodes.Status400BadRequest));
        }
        DateRange window;
        try
        {
            window = new DateRange(start, end);
        }
#pragma warning disable CA1031 // catch general exception - report a client-friendly 400 for any DateRange rejection
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return (null, default, Results.Problem(
                title: "Invalid window: " + ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
#pragma warning restore CA1031
        return (source, window, null);
    }

    private static List<string>? ParseStringList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : new List<string>(parts);
    }

    // ReSharper disable once UnusedMember.Local - kept for future percent parsing.
    private static bool TryParsePercent(string? raw, out double value, out string? error)
    {
        value = 0d;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            error = $"'{raw}' is not a valid decimal number.";
            return false;
        }
        if (value < 0 || value > 100)
        {
            error = $"must be between 0 and 100 (got {value.ToString(CultureInfo.InvariantCulture)}).";
            return false;
        }
        return true;
    }
}
