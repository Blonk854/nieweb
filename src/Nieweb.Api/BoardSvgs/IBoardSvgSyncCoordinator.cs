namespace Nieweb.Api.BoardSvgs;

/// <summary>
/// Runs one full sweep of the board-SVG sync pipeline
/// (docs/phase-2.md §7.5 <c>TC4</c> Phase B).
/// </summary>
/// <remarks>
/// <para>
/// Iterates every enabled <see cref="Nieweb.Data.Entities.BoardSvgSource"/>,
/// unions the product list across every configured
/// <see cref="Nieweb.DataSources.IAoiSource"/>, and copies the newest
/// matching <c>{ProductName}.svg</c> file from any reachable source
/// into the local cache directory. Never deletes local files
/// (products may age out of the DB but the historical SVG must
/// remain per TC4 §3).
/// </para>
/// <para>
/// The coordinator is <b>scoped</b> because it depends on the EF-
/// backed <see cref="IBoardSvgSources"/>. The background service
/// creates a scope per tick; the admin "sync now" endpoint invokes
/// this directly from its per-request scope.
/// </para>
/// </remarks>
public interface IBoardSvgSyncCoordinator
{
    /// <summary>
    /// Executes one sweep. Never throws — I/O errors are captured
    /// per-source in the returned <see cref="BoardSvgSyncResult"/>.
    /// </summary>
    Task<BoardSvgSyncResult> SyncOnceAsync(CancellationToken cancellationToken);
}

/// <summary>Aggregate result of one sync sweep.</summary>
public sealed record BoardSvgSyncResult(
    DateTime StartedUtc,
    DateTime CompletedUtc,
    string CacheDirectory,
    IReadOnlyList<BoardSvgSyncSourceOutcome> Sources,
    IReadOnlyList<BoardSvgSyncProductOutcome> Products);

/// <summary>Per-source outcome (share reachable? how many files?).</summary>
public sealed record BoardSvgSyncSourceOutcome(
    int SourceId,
    string MachineName,
    string UncPath,
    bool Enabled,
    bool Reachable,
    int FilesEnumerated,
    string? Error);

/// <summary>Per-product outcome (already cached? copied? errored?).</summary>
public sealed record BoardSvgSyncProductOutcome(
    string ProductName,
    bool AlreadyCached,
    bool Copied,
    string? SourceMachineName,
    DateTime? SourceFileLastWriteUtc,
    long? BytesCopied,
    string? Error);
