namespace Nieweb.Data.Entities;

/// <summary>
/// A named per-machine folder that produces board-layout SVG files
/// (docs/phase-2.md §7.5 <c>TC4</c>). Each AOI machine (both pre- and
/// post-reflow lines) generates and stores a panel-layout SVG per
/// product locally on the machine's filesystem; Nieweb polls these
/// paths, pulls the newest matching file per product, and caches it
/// under <c>Nieweb:BoardSvgCacheDir</c> keyed by <c>ProductId</c>.
/// </summary>
/// <remarks>
/// <para>
/// A source has a stable <see cref="MachineName"/> (unique across the
/// tenant so admins cannot register two "AOI-Line1-Post" folders) and
/// a UNC path (<see cref="UncPath"/>) pointing at the machine's SVG
/// output directory. The path must be reachable read-only from the
/// Nieweb host — the sync worker never writes to the source path.
/// </para>
/// <para>
/// Deleting a source removes the row and stops future polling; the
/// local cache directory is left intact because products may age out
/// of the DB but the historical SVG must remain (per TC4 §3).
/// </para>
/// </remarks>
public sealed class BoardSvgSource
{
    /// <summary>Auto-generated surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Human-readable machine name (e.g. <c>"AOI-Line1-Post"</c>).
    /// Unique across the tenant.
    /// </summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>
    /// UNC path (or local absolute path in dev) pointing at the
    /// machine's SVG output directory. Read-only from Nieweb's
    /// perspective; the sync worker never writes here.
    /// </summary>
    public string UncPath { get; set; } = string.Empty;

    /// <summary>
    /// When <c>false</c>, the sync worker skips this source but the
    /// row stays available so an admin can toggle it back on without
    /// re-typing the path.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// UTC timestamp of the last successful sync sweep against this
    /// source. <c>null</c> until the sync worker has run at least
    /// once. Written by the sync worker (Phase B), not by admin CRUD.
    /// </summary>
    public DateTime? LastSyncedUtc { get; set; }

    /// <summary>
    /// UTC timestamp of the last sync sweep that raised an error
    /// against this source (unreachable path, permission denied,
    /// corrupt SVG). <c>null</c> when the last sweep succeeded.
    /// </summary>
    public DateTime? LastSyncErrorUtc { get; set; }

    /// <summary>
    /// Short human-readable diagnostic from the last failed sweep,
    /// or <c>null</c> when the last sweep succeeded. Truncated at
    /// 500 chars by the sync worker so noisy stack traces cannot
    /// bloat the row.
    /// </summary>
    public string? LastSyncError { get; set; }

    /// <summary>UTC timestamp of first insert.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of the last admin edit.</summary>
    public DateTime LastModifiedUtc { get; set; }
}
