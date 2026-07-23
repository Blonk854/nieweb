using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Nieweb.Api.BoardSvgs;
using Nieweb.Api.Startup;
using Nieweb.DataSources;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Admin-only operational endpoints for the board-SVG cache
/// (docs/phase-2.md §7.5 <c>TC4</c> Phase B):
/// <list type="bullet">
///   <item><description>
///     <c>GET /api/admin/board-svgs/status</c> — per-source sync
///     health, cache inventory, and the set of missing products.
///   </description></item>
///   <item><description>
///     <c>POST /api/admin/board-svgs/sync</c> — triggers an
///     on-demand sweep; returns the same <see cref="BoardSvgSyncResult"/>
///     the background service would emit.
///   </description></item>
/// </list>
/// The CRUD endpoints on <c>/api/admin/board-svgs/sources</c> live
/// in <see cref="AdminBoardSvgSourcesEndpoints"/>.
/// </summary>
public static partial class AdminBoardSvgOperationsEndpoints
{
    /// <summary>Marker type for <see cref="ILogger{TCategoryName}"/>.</summary>
    public sealed class AdminBoardSvgOperationsMarker;

    /// <summary>Registers the endpoints on <paramref name="routes"/>.</summary>
    public static IEndpointRouteBuilder MapAdminBoardSvgOperationsEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/admin/board-svgs")
            .WithTags("AdminBoardSvgOperations")
            .RequireAuthorization(policy => policy.RequireRole(BootstrapAdmin.RoleAdmin));

        group.MapGet("/status", GetStatusAsync).WithName("AdminBoardSvgStatus");
        group.MapPost("/sync", PostSyncAsync).WithName("AdminBoardSvgSync");

        return routes;
    }

    /// <summary>Response DTO for GET /status.</summary>
    public sealed record BoardSvgStatusDto(
        string CacheDirectory,
        bool CacheDirectoryExists,
        int IntervalSeconds,
        bool SyncEnabled,
        IReadOnlyList<BoardSvgStatusSourceDto> Sources,
        IReadOnlyList<BoardSvgStatusCacheEntryDto> Cache,
        IReadOnlyList<string> KnownProducts,
        IReadOnlyList<string> MissingProducts);

    /// <summary>Per-source status entry.</summary>
    public sealed record BoardSvgStatusSourceDto(
        int Id,
        string MachineName,
        string UncPath,
        bool IsEnabled,
        DateTime? LastSyncedUtc,
        DateTime? LastSyncErrorUtc,
        string? LastSyncError);

    /// <summary>Per-cached-file entry.</summary>
    public sealed record BoardSvgStatusCacheEntryDto(
        string ProductName,
        string FileName,
        long SizeBytes,
        DateTime LastWriteTimeUtc);

    /// <summary>Response DTO for POST /sync — mirrors
    /// <see cref="BoardSvgSyncResult"/> so the SPA can render a
    /// per-source / per-product summary.</summary>
    public sealed record BoardSvgSyncResultDto(
        DateTime StartedUtc,
        DateTime CompletedUtc,
        string CacheDirectory,
        IReadOnlyList<BoardSvgSyncSourceOutcome> Sources,
        IReadOnlyList<BoardSvgSyncProductOutcome> Products);

    private static async Task<Ok<BoardSvgStatusDto>> GetStatusAsync(
        IBoardSvgSources sourcesRepo,
        IBoardSvgFileSystem fs,
        IEnumerable<IAoiSource> aoiSources,
        IOptions<BoardSvgSyncOptions> options,
        ILogger<AdminBoardSvgOperationsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourcesRepo);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(aoiSources);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var opts = options.Value;

        var sourceRows = await sourcesRepo.ListAsync(cancellationToken).ConfigureAwait(false);
        var sources = sourceRows
            .Select(s => new BoardSvgStatusSourceDto(
                s.Id, s.MachineName, s.UncPath, s.IsEnabled,
                s.LastSyncedUtc, s.LastSyncErrorUtc, s.LastSyncError))
            .ToList();

        var cacheExists = fs.DirectoryExists(opts.CacheDirectory);
        IReadOnlyList<BoardSvgFileInfo> cachedFiles;
        try
        {
            cachedFiles = cacheExists ? fs.ListSvgFiles(opts.CacheDirectory) : Array.Empty<BoardSvgFileInfo>();
        }
#pragma warning disable CA1031 // Do not catch general exception types — status endpoint should not throw on cache probe failures.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogStatusCacheListFailed(logger, opts.CacheDirectory, ex);
            cachedFiles = Array.Empty<BoardSvgFileInfo>();
        }
        var cache = cachedFiles
            .Select(f => new BoardSvgStatusCacheEntryDto(
                ProductName: Path.GetFileNameWithoutExtension(f.FileName),
                FileName: f.FileName,
                SizeBytes: f.SizeBytes,
                LastWriteTimeUtc: f.LastWriteTimeUtc))
            .OrderBy(c => c.ProductName, StringComparer.Ordinal)
            .ToList();

        // Union AOI product names across every source. Failures per
        // source are swallowed here — the caller learns about them
        // via the /sync endpoint, not the read-only status probe.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var aoi in aoiSources)
        {
            IReadOnlyList<Product> products;
            try
            {
                products = await aoi.ListProductsAsync(cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Do not catch general exception types — a broken AOI DB must not blank the status page for the other DB.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogStatusAoiFailed(logger, aoi.Descriptor.Id, ex);
                continue;
            }
            foreach (var p in products)
            {
                if (!string.IsNullOrWhiteSpace(p.ProductName))
                {
                    _ = known.Add(p.ProductName);
                }
            }
        }
        var knownList = known.OrderBy(n => n, StringComparer.Ordinal).ToList();
        var cachedNameSet = new HashSet<string>(cache.Select(c => c.ProductName), StringComparer.OrdinalIgnoreCase);
        var missing = knownList.Where(n => !cachedNameSet.Contains(n)).ToList();

        return TypedResults.Ok(new BoardSvgStatusDto(
            CacheDirectory: opts.CacheDirectory,
            CacheDirectoryExists: cacheExists,
            IntervalSeconds: opts.IntervalSeconds,
            SyncEnabled: opts.Enabled,
            Sources: sources,
            Cache: cache,
            KnownProducts: knownList,
            MissingProducts: missing));
    }

    private static async Task<Ok<BoardSvgSyncResultDto>> PostSyncAsync(
        IBoardSvgSyncCoordinator coordinator,
        ILogger<AdminBoardSvgOperationsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(logger);

        LogSyncRequested(logger);
        var result = await coordinator.SyncOnceAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new BoardSvgSyncResultDto(
            result.StartedUtc,
            result.CompletedUtc,
            result.CacheDirectory,
            result.Sources,
            result.Products));
    }

    [LoggerMessage(EventId = 3530, Level = LogLevel.Information,
        Message = "Admin triggered board-SVG sync via POST /api/admin/board-svgs/sync")]
    private static partial void LogSyncRequested(ILogger logger);

    [LoggerMessage(EventId = 3531, Level = LogLevel.Warning,
        Message = "Board-SVG status: cache listing failed for {CacheDirectory}")]
    private static partial void LogStatusCacheListFailed(ILogger logger, string cacheDirectory, Exception exception);

    [LoggerMessage(EventId = 3532, Level = LogLevel.Warning,
        Message = "Board-SVG status: ListProductsAsync failed for AOI source {AoiSourceId}")]
    private static partial void LogStatusAoiFailed(ILogger logger, string aoiSourceId, Exception exception);
}
