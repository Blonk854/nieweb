using Microsoft.Extensions.Options;

using Nieweb.Api.Audit;
using Nieweb.DataSources;

namespace Nieweb.Api.BoardSvgs;

/// <summary>
/// Default <see cref="IBoardSvgSyncCoordinator"/>. Coordinates one
/// full sweep: enumerate sources, list products, pick newest matching
/// file per product, copy into cache. All I/O goes through
/// <see cref="IBoardSvgFileSystem"/> so unit tests can drive it
/// without touching real UNC shares.
/// </summary>
public sealed partial class BoardSvgSyncCoordinator : IBoardSvgSyncCoordinator
{
    private const string SvgExtension = ".svg";

    private readonly IBoardSvgSources _sourcesRepo;
    private readonly IEnumerable<IAoiSource> _aoiSources;
    private readonly IBoardSvgFileSystem _fs;
    private readonly IAuditLog _audit;
    private readonly IOptions<BoardSvgSyncOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BoardSvgSyncCoordinator> _logger;

    public BoardSvgSyncCoordinator(
        IBoardSvgSources sourcesRepo,
        IEnumerable<IAoiSource> aoiSources,
        IBoardSvgFileSystem fileSystem,
        IAuditLog audit,
        IOptions<BoardSvgSyncOptions> options,
        TimeProvider time,
        ILogger<BoardSvgSyncCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(sourcesRepo);
        ArgumentNullException.ThrowIfNull(aoiSources);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _sourcesRepo = sourcesRepo;
        _aoiSources = aoiSources;
        _fs = fileSystem;
        _audit = audit;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BoardSvgSyncResult> SyncOnceAsync(CancellationToken cancellationToken)
    {
        var cacheDir = _options.Value.CacheDirectory;
        var startedUtc = _time.GetUtcNow().UtcDateTime;
        LogSyncStarted(_logger, cacheDir);

        // Best-effort: ensure the cache directory exists. If this
        // fails (permission denied, invalid path) we can still record
        // the failure per-product below.
        try
        {
            _fs.EnsureDirectoryExists(cacheDir);
        }
#pragma warning disable CA1031 // Do not catch general exception types — we want to surface any I/O failure in the result rather than crashing the whole sweep.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCacheDirFailed(_logger, cacheDir, ex);
            var completedUtc = _time.GetUtcNow().UtcDateTime;
            return new BoardSvgSyncResult(
                startedUtc,
                completedUtc,
                cacheDir,
                Array.Empty<BoardSvgSyncSourceOutcome>(),
                new[]
                {
                    new BoardSvgSyncProductOutcome(
                        ProductName: "*",
                        AlreadyCached: false,
                        Copied: false,
                        SourceMachineName: null,
                        SourceFileLastWriteUtc: null,
                        BytesCopied: null,
                        Error: $"Cache directory unavailable: {ex.Message}"),
                });
        }

        // 1. Enumerate every configured source row.
        var sourceRows = await _sourcesRepo
            .ListAsync(cancellationToken)
            .ConfigureAwait(false);

        // 2. Scan each enabled source share. Keep the enumeration
        //    outcome so operators can tell what's reachable, and
        //    build a per-source dictionary keyed by product-name
        //    (case-insensitive to match Windows file semantics).
        var sourceOutcomes = new List<BoardSvgSyncSourceOutcome>(sourceRows.Count);
        var perSourceFiles = new Dictionary<int, IReadOnlyDictionary<string, BoardSvgFileInfo>>(sourceRows.Count);
        foreach (var source in sourceRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!source.IsEnabled)
            {
                sourceOutcomes.Add(new BoardSvgSyncSourceOutcome(
                    source.Id, source.MachineName, source.UncPath,
                    Enabled: false, Reachable: false, FilesEnumerated: 0,
                    Error: null));
                continue;
            }

            IReadOnlyList<BoardSvgFileInfo> files;
            try
            {
                files = _fs.ListSvgFiles(source.UncPath);
            }
#pragma warning disable CA1031 // Do not catch general exception types — we want unreachable shares to fail *this* source only, not the whole sweep.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                var msg = $"{ex.GetType().Name}: {ex.Message}";
                sourceOutcomes.Add(new BoardSvgSyncSourceOutcome(
                    source.Id, source.MachineName, source.UncPath,
                    Enabled: true, Reachable: false, FilesEnumerated: 0,
                    Error: msg));
                await _sourcesRepo
                    .RecordSyncFailureAsync(source.Id, msg, cancellationToken)
                    .ConfigureAwait(false);
                LogSourceUnreachable(_logger, source.Id, source.MachineName, ex);
                continue;
            }

            // Index by product name (filename stem).
            var byName = new Dictionary<string, BoardSvgFileInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                var stem = Path.GetFileNameWithoutExtension(f.FileName);
                if (string.IsNullOrEmpty(stem))
                {
                    continue;
                }
                byName[stem] = f;
            }
            perSourceFiles[source.Id] = byName;
            sourceOutcomes.Add(new BoardSvgSyncSourceOutcome(
                source.Id, source.MachineName, source.UncPath,
                Enabled: true, Reachable: true, FilesEnumerated: files.Count,
                Error: null));
        }

        // 3. Union the AOI product list across every configured
        //    source. We use a case-insensitive product-name set
        //    (matches Windows filename semantics) and skip names that
        //    contain filesystem-invalid characters — those cannot be
        //    represented on disk and would be a security risk (path
        //    traversal via '/', '..', etc.).
        var wantedProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var aoi in _aoiSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Product> products;
            try
            {
                products = await aoi
                    .ListProductsAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Do not catch general exception types — a broken AOI DB must not blank the sweep for the other DB.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogAoiSourceFailed(_logger, aoi.Descriptor.Id, ex);
                continue;
            }
            foreach (var p in products)
            {
                if (string.IsNullOrWhiteSpace(p.ProductName))
                {
                    continue;
                }
                if (p.ProductName.AsSpan().IndexOfAny(invalidChars) >= 0)
                {
                    LogSkippedInvalidName(_logger, p.ProductName);
                    continue;
                }
                if (p.ProductName.Contains(".."))
                {
                    LogSkippedInvalidName(_logger, p.ProductName);
                    continue;
                }
                _ = wantedProducts.Add(p.ProductName);
            }
        }

        // 4. Snapshot what's already cached.
        IReadOnlyList<BoardSvgFileInfo> cachedFiles;
        try
        {
            cachedFiles = _fs.ListSvgFiles(cacheDir);
        }
#pragma warning disable CA1031 // Do not catch general exception types — cache dir probe failures must not abort the sweep.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCacheListFailed(_logger, cacheDir, ex);
            cachedFiles = Array.Empty<BoardSvgFileInfo>();
        }
        var cachedByName = new HashSet<string>(
            cachedFiles.Select(f => Path.GetFileNameWithoutExtension(f.FileName)),
            StringComparer.OrdinalIgnoreCase);

        // 5. For each wanted product not yet cached, find the newest
        //    matching source file and copy it in.
        var productOutcomes = new List<BoardSvgSyncProductOutcome>();
        foreach (var productName in wantedProducts.OrderBy(p => p, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (cachedByName.Contains(productName))
            {
                productOutcomes.Add(new BoardSvgSyncProductOutcome(
                    productName,
                    AlreadyCached: true,
                    Copied: false,
                    SourceMachineName: null,
                    SourceFileLastWriteUtc: null,
                    BytesCopied: null,
                    Error: null));
                continue;
            }

            // Find best (newest) match across all reachable sources.
            BoardSvgFileInfo? best = null;
            string? bestSourceMachine = null;
            int bestSourceId = 0;
            foreach (var source in sourceRows)
            {
                if (!perSourceFiles.TryGetValue(source.Id, out var byName))
                {
                    continue;
                }
                if (!byName.TryGetValue(productName, out var candidate))
                {
                    continue;
                }
                if (best is null || candidate.LastWriteTimeUtc > best.LastWriteTimeUtc)
                {
                    best = candidate;
                    bestSourceMachine = source.MachineName;
                    bestSourceId = source.Id;
                }
            }

            if (best is null)
            {
                // Nothing to copy this round — not an error, just not
                // yet available. Skip the audit log to avoid noise.
                productOutcomes.Add(new BoardSvgSyncProductOutcome(
                    productName,
                    AlreadyCached: false,
                    Copied: false,
                    SourceMachineName: null,
                    SourceFileLastWriteUtc: null,
                    BytesCopied: null,
                    Error: null));
                continue;
            }

            // Copy: plain read + write (never robocopy).
            var targetPath = Path.Combine(cacheDir, productName + SvgExtension);
            try
            {
                var bytes = await _fs
                    .ReadAllBytesAsync(best.FullPath, cancellationToken)
                    .ConfigureAwait(false);
                await _fs
                    .WriteAllBytesAsync(targetPath, bytes, cancellationToken)
                    .ConfigureAwait(false);

                productOutcomes.Add(new BoardSvgSyncProductOutcome(
                    productName,
                    AlreadyCached: false,
                    Copied: true,
                    SourceMachineName: bestSourceMachine,
                    SourceFileLastWriteUtc: best.LastWriteTimeUtc,
                    BytesCopied: bytes.LongLength,
                    Error: null));
                LogProductCopied(_logger, productName, bestSourceMachine ?? "?", bytes.LongLength);

                await _audit.WriteAsync(
                    AuditEventTypes.BoardSvgSynced,
                    AuditTargetTypes.BoardSvg,
                    productName,
                    new
                    {
                        productName,
                        sourceId = bestSourceId,
                        sourceMachineName = bestSourceMachine,
                        sourceFileLastWriteUtc = best.LastWriteTimeUtc,
                        bytesCopied = bytes.LongLength,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Do not catch general exception types — a single unreadable/unwritable file must not stop the sweep.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                var msg = $"{ex.GetType().Name}: {ex.Message}";
                productOutcomes.Add(new BoardSvgSyncProductOutcome(
                    productName,
                    AlreadyCached: false,
                    Copied: false,
                    SourceMachineName: bestSourceMachine,
                    SourceFileLastWriteUtc: best.LastWriteTimeUtc,
                    BytesCopied: null,
                    Error: msg));
                LogProductCopyFailed(_logger, productName, bestSourceMachine ?? "?", ex);
                await _audit.WriteAsync(
                    AuditEventTypes.BoardSvgSyncFailed,
                    AuditTargetTypes.BoardSvg,
                    productName,
                    new
                    {
                        productName,
                        sourceId = bestSourceId,
                        sourceMachineName = bestSourceMachine,
                        error = msg,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        // 6. Record per-source success (reachable sources that
        //    completed enumeration without exception).
        foreach (var outcome in sourceOutcomes)
        {
            if (outcome is { Enabled: true, Reachable: true, Error: null })
            {
                await _sourcesRepo
                    .RecordSyncSuccessAsync(outcome.SourceId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var completed = _time.GetUtcNow().UtcDateTime;
        var copiedCount = productOutcomes.Count(p => p.Copied);
        var failedCount = productOutcomes.Count(p => p.Error is not null);
        LogSyncCompleted(_logger, copiedCount, failedCount, sourceOutcomes.Count);
        return new BoardSvgSyncResult(startedUtc, completed, cacheDir, sourceOutcomes, productOutcomes);
    }

    [LoggerMessage(EventId = 3510, Level = LogLevel.Information,
        Message = "Board-SVG sync started (cache dir: {CacheDirectory})")]
    private static partial void LogSyncStarted(ILogger logger, string cacheDirectory);

    [LoggerMessage(EventId = 3511, Level = LogLevel.Information,
        Message = "Board-SVG sync completed: {Copied} copied, {Failed} failed, {Sources} sources scanned")]
    private static partial void LogSyncCompleted(ILogger logger, int copied, int failed, int sources);

    [LoggerMessage(EventId = 3512, Level = LogLevel.Error,
        Message = "Board-SVG cache directory unavailable: {CacheDirectory}")]
    private static partial void LogCacheDirFailed(ILogger logger, string cacheDirectory, Exception exception);

    [LoggerMessage(EventId = 3513, Level = LogLevel.Warning,
        Message = "Board-SVG cache directory listing failed: {CacheDirectory}")]
    private static partial void LogCacheListFailed(ILogger logger, string cacheDirectory, Exception exception);

    [LoggerMessage(EventId = 3514, Level = LogLevel.Warning,
        Message = "Board-SVG source {SourceId} ({MachineName}) unreachable")]
    private static partial void LogSourceUnreachable(ILogger logger, int sourceId, string machineName, Exception exception);

    [LoggerMessage(EventId = 3515, Level = LogLevel.Warning,
        Message = "Board-SVG: ListProductsAsync failed for AOI source {AoiSourceId}")]
    private static partial void LogAoiSourceFailed(ILogger logger, string aoiSourceId, Exception exception);

    [LoggerMessage(EventId = 3516, Level = LogLevel.Warning,
        Message = "Board-SVG: skipped product '{ProductName}' — invalid characters for filesystem")]
    private static partial void LogSkippedInvalidName(ILogger logger, string productName);

    [LoggerMessage(EventId = 3517, Level = LogLevel.Information,
        Message = "Board-SVG copied '{ProductName}' from {MachineName} ({Bytes} bytes)")]
    private static partial void LogProductCopied(ILogger logger, string productName, string machineName, long bytes);

    [LoggerMessage(EventId = 3518, Level = LogLevel.Warning,
        Message = "Board-SVG copy failed for '{ProductName}' from {MachineName}")]
    private static partial void LogProductCopyFailed(ILogger logger, string productName, string machineName, Exception exception);
}
