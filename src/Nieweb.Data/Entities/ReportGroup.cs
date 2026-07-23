namespace Nieweb.Data.Entities;

/// <summary>
/// A named container for <see cref="Report"/> entries. Ports Vieweb's
/// <c>reportgroup</c> table (Vieweb §2.4.5) — a group is optional,
/// common to all users, and a report belongs to at most one group at
/// a time. Groups exist purely to organise the home-page listing;
/// they carry no permission semantics of their own.
/// </summary>
/// <remarks>
/// Deleting a group leaves its reports intact: the <c>ReportGroupId</c>
/// FK on <see cref="Report"/> is set to <c>null</c> (SetNull cascade).
/// Legacy Vieweb only allowed deleting an empty group; Nieweb relaxes
/// that constraint because the report row still carries its own audit
/// trail and can be re-grouped at any time.
/// </remarks>
public sealed class ReportGroup
{
    /// <summary>Auto-generated surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Human-readable group name (e.g. <c>"Daily production"</c>).
    /// Unique across the tenant so admins cannot accidentally create
    /// two "Daily production" entries.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Manual sort key. Ties break on <see cref="Name"/> ascending.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>UTC timestamp of first insert.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of the last successful update.</summary>
    public DateTime LastModifiedUtc { get; set; }

    /// <summary>
    /// Reports currently affected to the group. This navigation is
    /// used for display-only ordering / counting; deleting a group
    /// nulls each row's <c>ReportGroupId</c> rather than cascading.
    /// </summary>
    public ICollection<Report> Reports { get; set; } = new List<Report>();
}
