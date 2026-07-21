namespace Nieweb.Data.Entities;

/// <summary>
/// A named, persisted filter/layout combination for one report. Analogous
/// to legacy Vieweb <c>Report</c> + <c>Filter</c> saved views, but
/// simplified: the filter payload is a single JSON blob rather than a
/// relational filter graph.
/// </summary>
public sealed class SavedView
{
    /// <summary>Auto-generated surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// User who owns this view. If <see cref="IsShared"/> is false, only
    /// the owner sees it.
    /// </summary>
    public int OwnerUserId { get; set; }

    /// <summary>
    /// Display name shown in the report's saved-view dropdown.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Stable identifier of the report this view applies to
    /// (e.g. <c>"panel-yield"</c>, <c>"defect-pareto"</c>).
    /// </summary>
    public string ReportKey { get; set; } = string.Empty;

    /// <summary>
    /// JSON-encoded filter payload. Report-specific shape - each report
    /// defines its own filter DTO.
    /// </summary>
    public string FilterJson { get; set; } = "{}";

    /// <summary>
    /// When true, other users can see and apply this view (read-only).
    /// </summary>
    public bool IsShared { get; set; }

    /// <summary>UTC timestamp when the view was first saved.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of the most recent update.</summary>
    public DateTime LastModifiedUtc { get; set; }

    /// <summary>
    /// Concurrency token to detect simultaneous edits by two users of a
    /// shared view.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[]? RowVersion { get; set; }
}
