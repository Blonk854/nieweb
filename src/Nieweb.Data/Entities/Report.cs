namespace Nieweb.Data.Entities;

/// <summary>
/// A user-composed dashboard: a title + optional description + an
/// ordered list of <see cref="ReportEntity"/> tiles. Ports Vieweb's
/// <c>report</c> table (§2.4.5 / §3) with two deliberate
/// simplifications:
/// <list type="bullet">
///   <item>
///   The header / footer text columns are replaced by a single
///   <see cref="ChromeJson"/> blob so the SPA can extend chrome
///   configuration without a schema migration.
///   </item>
///   <item>
///   Ownership is a lightweight <see cref="OwnerUserId"/> +
///   <see cref="OwnerDisplayName"/> pair rather than a
///   <c>user_report</c> many-to-many join — the join concept moves
///   to RC3 (locked / password-protected reports) and RC4
///   (home-page pinning).
///   </item>
/// </list>
/// </summary>
/// <remarks>
/// Deleting a report cascade-deletes its entities. Deleting a
/// <see cref="ReportGroup"/> nulls the report's <see cref="ReportGroupId"/>
/// instead of cascading — the report survives being un-grouped.
/// </remarks>
public sealed class Report
{
    /// <summary>Auto-generated surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Report title as shown on the home page and the SPA header
    /// (Vieweb's <c>REPORT.TITLE</c>). Required; the admin UI treats
    /// an empty title as a validation error.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional long-form description (Vieweb <c>REPORT.DESCRIPTION</c>).</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional group affiliation (Vieweb <c>REPORT.REPORT_GROUP_ID</c>).
    /// A report belongs to at most one group at a time.
    /// </summary>
    public int? ReportGroupId { get; set; }

    /// <summary>Navigation to the affiliated group (may be null).</summary>
    public ReportGroup? Group { get; set; }

    /// <summary>
    /// Snapshot of the creating user's <c>NiewebUser.Id</c>. Kept as
    /// a nullable integer with no navigation so deleting a user does
    /// not cascade into report deletion; the SPA falls back to
    /// <see cref="OwnerDisplayName"/> when the id no longer resolves.
    /// </summary>
    public int? OwnerUserId { get; set; }

    /// <summary>
    /// Frozen copy of the creating user's <c>DisplayName</c>. Mirrors
    /// Vieweb's <c>REPORT.USER_NAME_CREATION</c> so the SPA can render
    /// authorship even after a user is disabled.
    /// </summary>
    public string OwnerDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// When <c>true</c> the report is "locked" per RC3 — the header
    /// cannot be edited nor tiles added / removed / reordered without
    /// first supplying the lock password. Anyone can still
    /// <em>duplicate</em> a locked report into their own unlocked copy.
    /// Toggled exclusively through the <c>/lock</c> and <c>/unlock</c>
    /// endpoints; the header <c>PUT</c> preserves this bit.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// PHC-encoded Argon2id hash of the lock password (RC3). <c>null</c>
    /// exactly when <see cref="IsLocked"/> is <c>false</c>. The value is
    /// never returned to any client — it is only used server-side to
    /// verify <c>/unlock</c> calls.
    /// </summary>
    public string? LockPasswordHash { get; set; }

    /// <summary>
    /// When <c>true</c> the report appears on every user's home page
    /// (site-wide pin). Per-user pinning is a separate feature that
    /// will land with RC4.
    /// </summary>
    public bool IsPinnedHome { get; set; }

    /// <summary>
    /// Optional auto-refresh interval in seconds (Vieweb
    /// <c>REPORT.REFRESH_FREQUENCY</c>). <c>null</c> means "no
    /// auto-refresh". Range validated at the API layer (must be
    /// positive when supplied).
    /// </summary>
    public int? RefreshFrequencySeconds { get; set; }

    /// <summary>
    /// Optional chrome configuration blob (JSON). Reserved for
    /// header / footer text, logo url, single-column vs multi-column
    /// layout, etc. Kept as opaque JSON so RC2's editor can extend
    /// without a schema migration.
    /// </summary>
    public string? ChromeJson { get; set; }

    /// <summary>Manual sort key within the parent group / home page.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>UTC timestamp of first insert.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of the last successful update.</summary>
    public DateTime LastModifiedUtc { get; set; }

    /// <summary>
    /// Ordered list of tiles composing the report. Cascade-deleted
    /// with the report itself.
    /// </summary>
    public ICollection<ReportEntity> Entities { get; set; } = new List<ReportEntity>();
}
