using System.Globalization;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

using Nieweb.Api.BoardSvgs;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Public read endpoint for cached panel-layout SVGs
/// (docs/phase-2.md §7.5 TC4 Phase C). Serves files that the
/// <see cref="BoardSvgSyncService"/> has copied into the local
/// cache directory (<see cref="BoardSvgSyncOptions.CacheDirectory"/>).
/// Auth-gated the same as the traceability endpoints — any
/// signed-in user can fetch a board SVG so the SPA viewer works
/// for every reviewer.
/// </summary>
public static partial class BoardSvgsEndpoints
{
    /// <summary>
    /// Content-type served for cached board SVGs.
    /// </summary>
    public const string SvgContentType = "image/svg+xml";

    /// <summary>
    /// Long-ish public cache lifetime. Files are effectively
    /// immutable per product+recipe version, but revalidation via
    /// ETag stays cheap so we don't have to bust when a product's
    /// SVG genuinely changes.
    /// </summary>
    public const int DefaultCacheMaxAgeSeconds = 3600;

    /// <summary>
    /// Registers <c>GET /api/board-svgs/{productName}</c>. Any
    /// authenticated user may read; no admin role required.
    /// </summary>
    public static IEndpointRouteBuilder MapBoardSvgsEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/board-svgs")
            .WithTags("BoardSvgs")
            .RequireAuthorization();

        _ = group.MapGet("/{productName}", GetSvgAsync)
            .WithName("GetBoardSvg")
            .Produces(StatusCodes.Status200OK, contentType: SvgContentType)
            .Produces(StatusCodes.Status304NotModified)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> GetSvgAsync(
        string productName,
        HttpContext httpContext,
        IBoardSvgFileSystem fs,
        IOptions<BoardSvgSyncOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var log = loggerFactory.CreateLogger(typeof(BoardSvgsEndpoints));

        if (string.IsNullOrWhiteSpace(productName))
        {
            return TypedResults.Problem(
                title: "Missing product name.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Reject anything that could escape the cache directory or
        // reference a non-file path. Same guard the coordinator
        // uses when picking which products to cache.
        if (productName.Contains("..", StringComparison.Ordinal)
            || productName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            LogRejectedName(log, productName);
            return TypedResults.Problem(
                title: "Invalid product name.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var opts = options.Value;
        var fullPath = Path.Combine(opts.CacheDirectory, productName + ".svg");
        var info = fs.GetFileInfo(fullPath);
        if (info is null)
        {
            return TypedResults.NotFound();
        }

        // ETag = "W/\"{ticks}-{size}\"". Weak because we don't
        // byte-compare — mtime + size collisions are vanishingly
        // rare and mtime resolution differs across filesystems.
        var etag = FormatEtag(info.LastWriteTimeUtc, info.SizeBytes);

        var ifNoneMatch = httpContext.Request.Headers.IfNoneMatch;
        if (MatchesAny(ifNoneMatch, etag))
        {
            SetCacheHeaders(httpContext.Response, etag, info.LastWriteTimeUtc);
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        var bytes = await fs.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        SetCacheHeaders(httpContext.Response, etag, info.LastWriteTimeUtc);
        return TypedResults.File(bytes, contentType: SvgContentType, lastModified: info.LastWriteTimeUtc);
    }

    private static string FormatEtag(DateTime lastWriteUtc, long sizeBytes)
    {
        var ticks = lastWriteUtc.ToUniversalTime().Ticks;
        return "W/\"" + ticks.ToString(CultureInfo.InvariantCulture)
            + "-" + sizeBytes.ToString(CultureInfo.InvariantCulture) + "\"";
    }

    private static bool MatchesAny(StringValues headerValues, string etag)
    {
        foreach (var raw in headerValues)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }
            // A single header line may hold multiple ETags,
            // comma-separated per RFC 9110 §13.1.2.
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part == "*" || string.Equals(part, etag, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static void SetCacheHeaders(HttpResponse response, string etag, DateTime lastModifiedUtc)
    {
        response.Headers[HeaderNames.ETag] = etag;
        response.Headers[HeaderNames.CacheControl] =
            "private, max-age=" + DefaultCacheMaxAgeSeconds.ToString(CultureInfo.InvariantCulture)
            + ", must-revalidate";
        response.Headers[HeaderNames.LastModified] =
            lastModifiedUtc.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture);
    }

    [LoggerMessage(EventId = 3540, Level = LogLevel.Warning,
        Message = "Rejected board-svg request for suspicious product name '{ProductName}'.")]
    private static partial void LogRejectedName(ILogger logger, string productName);
}
