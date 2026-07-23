using Nieweb.Data.Entities;

namespace Nieweb.Api.Reports;

/// <summary>
/// Read/write access to <see cref="ReportGroup"/>, <see cref="Report"/>,
/// and <see cref="ReportEntity"/>. Backs the admin "Reports" pages
/// (Vieweb §2.4.5 / §3, docs/phase-2.md §7.6 <c>RC1</c>).
/// </summary>
/// <remarks>
/// <para>
/// RC1 admin CRUD scope: full lifecycle for groups and reports plus
/// per-report tile add / remove / reorder. Group and report name /
/// title uniqueness is enforced at the DB layer via a case-sensitive
/// unique index on group name; report titles are intentionally not
/// unique because two authors can legitimately produce different
/// dashboards with the same name.
/// </para>
/// <para>
/// User-owned CRUD (Authors creating their own reports through the
/// SPA) layers on top of this service in RC2; the same service
/// signatures are reused with an owner-scoped authorisation policy.
/// </para>
/// </remarks>
public interface IReports
{
    /// <summary>Returns every group ordered by (DisplayOrder, Name).</summary>
    Task<IReadOnlyList<ReportGroupRow>> ListGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new report group. Throws
    /// <see cref="ReportConflictException"/> when the name is already used.
    /// </summary>
    Task<ReportGroupRow> CreateGroupAsync(
        string name,
        int displayOrder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames / re-sorts a report group. Returns <c>null</c> when the
    /// id is unknown. Throws <see cref="ReportConflictException"/> on
    /// name clash.
    /// </summary>
    Task<ReportGroupRow?> UpdateGroupAsync(
        int id,
        string name,
        int displayOrder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a report group. Affected reports have their
    /// <c>ReportGroupId</c> nulled (SetNull cascade). Returns
    /// <c>true</c> if a row was removed.
    /// </summary>
    Task<bool> DeleteGroupAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every report ordered by (DisplayOrder, Title). Excludes
    /// entities (a report's tiles are fetched on demand via
    /// <see cref="GetReportAsync"/>).
    /// </summary>
    Task<IReadOnlyList<ReportRow>> ListReportsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the pinned-to-home reports (RC4). Locked pinned
    /// reports are included so users can discover them and unlock
    /// on click; the SPA renders a "locked" badge. Ordered by
    /// (DisplayOrder, Title).
    /// </summary>
    Task<IReadOnlyList<ReportRow>> ListHomeReportsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full report (header + ordered entities) or
    /// <c>null</c> if the id is unknown.
    /// </summary>
    Task<ReportDetail?> GetReportAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new report shell (no tiles). The caller supplies the
    /// snapshot of the owning user's id + display name so the row is
    /// self-describing even if the user is later deleted.
    /// </summary>
    Task<ReportRow> CreateReportAsync(
        CreateReportInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a report's header. Tiles are managed through
    /// <see cref="AddEntityAsync"/> / <see cref="UpdateEntityAsync"/> /
    /// <see cref="RemoveEntityAsync"/>. Returns <c>null</c> when the
    /// id is unknown.
    /// </summary>
    Task<ReportRow?> UpdateReportAsync(
        int id,
        UpdateReportInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a report and cascade-removes its tiles.</summary>
    Task<bool> DeleteReportAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a tile to the report. Returns <c>null</c> when the
    /// report does not exist. The new tile's <c>DisplayOrder</c>
    /// defaults to <c>max(existing) + 1</c> when negative.
    /// </summary>
    Task<ReportEntityRow?> AddEntityAsync(
        int reportId,
        AddEntityInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a tile in place. Returns <c>null</c> when the
    /// (reportId, entityId) pair is unknown.
    /// </summary>
    Task<ReportEntityRow?> UpdateEntityAsync(
        int reportId,
        int entityId,
        UpdateEntityInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a tile. Returns <c>true</c> if a row was removed.</summary>
    Task<bool> RemoveEntityAsync(int reportId, int entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks a report by hashing <paramref name="password"/> and
    /// setting <c>IsLocked=true</c> (docs/phase-2.md §7.6 <c>RC3</c>).
    /// The plain-text password is never persisted. Idempotent:
    /// re-locking an already-locked report replaces the hash so an
    /// owner can rotate a compromised password.
    /// </summary>
    Task<LockOutcome> LockReportAsync(
        int id,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies <paramref name="password"/> against the stored hash;
    /// on success clears <c>IsLocked</c> and <c>LockPasswordHash</c>.
    /// Wrong password returns <see cref="UnlockResult.WrongPassword"/>
    /// and does not touch the entity.
    /// </summary>
    Task<UnlockOutcome> UnlockReportAsync(
        int id,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clones the report header + all tiles into a new unlocked
    /// report owned by <paramref name="input"/>'s caller. The clone
    /// starts with <c>IsLocked=false</c>, <c>LockPasswordHash=null</c>,
    /// <c>IsPinnedHome=false</c>, and its tiles keep their original
    /// display order / config JSON. Returns <c>null</c> when the
    /// source id is unknown.
    /// </summary>
    Task<ReportRow?> DuplicateReportAsync(
        int id,
        DuplicateReportInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggles the <c>IsPinnedHome</c> flag on a report
    /// (docs/phase-2.md §7.9 <c>F14</c>). Returns the refreshed
    /// row, or <c>null</c> when the id is unknown. Idempotent:
    /// pinning an already-pinned report (or unpinning an already-
    /// unpinned one) still returns the current row and touches
    /// <c>LastModifiedUtc</c> so audit callers can observe the
    /// admin action.
    /// </summary>
    Task<ReportRow?> SetPinnedHomeAsync(
        int id,
        bool pinned,
        CancellationToken cancellationToken = default);
}

/// <summary>Row snapshot for group listings.</summary>
public sealed record ReportGroupRow(
    int Id,
    string Name,
    int DisplayOrder,
    int ReportCount,
    DateTime CreatedUtc,
    DateTime LastModifiedUtc);

/// <summary>Row snapshot for report listings (no tiles).</summary>
public sealed record ReportRow(
    int Id,
    string Title,
    string? Description,
    int? ReportGroupId,
    string? GroupName,
    int? OwnerUserId,
    string OwnerDisplayName,
    bool IsLocked,
    bool IsPinnedHome,
    int? RefreshFrequencySeconds,
    string? ChromeJson,
    int DisplayOrder,
    int EntityCount,
    DateTime CreatedUtc,
    DateTime LastModifiedUtc);

/// <summary>Row snapshot for a single tile.</summary>
public sealed record ReportEntityRow(
    int Id,
    int ReportId,
    string TileType,
    string? Title,
    int DisplayOrder,
    string ConfigJson,
    DateTime CreatedUtc,
    DateTime LastModifiedUtc);

/// <summary>Full report detail returned by GET /{id}.</summary>
public sealed record ReportDetail(
    ReportRow Report,
    IReadOnlyList<ReportEntityRow> Entities);

/// <summary>Create-payload for a report.</summary>
public sealed record CreateReportInput(
    string Title,
    string? Description,
    int? ReportGroupId,
    int? OwnerUserId,
    string OwnerDisplayName,
    bool IsLocked,
    bool IsPinnedHome,
    int? RefreshFrequencySeconds,
    string? ChromeJson,
    int DisplayOrder);

/// <summary>Update-payload for a report header.</summary>
public sealed record UpdateReportInput(
    string Title,
    string? Description,
    int? ReportGroupId,
    bool IsLocked,
    bool IsPinnedHome,
    int? RefreshFrequencySeconds,
    string? ChromeJson,
    int DisplayOrder);

/// <summary>Add-payload for a tile.</summary>
public sealed record AddEntityInput(
    string TileType,
    string? Title,
    int DisplayOrder,
    string ConfigJson);

/// <summary>Update-payload for a tile.</summary>
public sealed record UpdateEntityInput(
    string TileType,
    string? Title,
    int DisplayOrder,
    string ConfigJson);

/// <summary>Duplicate-payload for a report clone (RC3).</summary>
public sealed record DuplicateReportInput(
    string Title,
    int? OwnerUserId,
    string OwnerDisplayName);

/// <summary>Result classes for the RC3 lock lifecycle.</summary>
public enum LockResult
{
    /// <summary>Password accepted; report is now locked.</summary>
    Success,
    /// <summary>No report exists with the given id.</summary>
    NotFound,
    /// <summary>Password was empty / whitespace-only.</summary>
    PasswordEmpty,
}

/// <summary>Result classes for the RC3 unlock lifecycle.</summary>
public enum UnlockResult
{
    /// <summary>Password verified; report is now unlocked.</summary>
    Success,
    /// <summary>No report exists with the given id.</summary>
    NotFound,
    /// <summary>Report exists but was not locked to begin with.</summary>
    NotLocked,
    /// <summary>Password did not match the stored hash.</summary>
    WrongPassword,
}

/// <summary>Return value of <see cref="IReports.LockReportAsync"/>.</summary>
public sealed record LockOutcome(LockResult Result, ReportRow? Report);

/// <summary>Return value of <see cref="IReports.UnlockReportAsync"/>.</summary>
public sealed record UnlockOutcome(UnlockResult Result, ReportRow? Report);

/// <summary>
/// Thrown by <see cref="IReports"/> when a create / update call would
/// violate a uniqueness invariant (duplicate group name). Endpoints
/// surface this as HTTP 409.
/// </summary>
public sealed class ReportConflictException : InvalidOperationException
{
    public ReportConflictException(string message) : base(message) { }
    public ReportConflictException(string message, Exception inner) : base(message, inner) { }
    public ReportConflictException() { }
}
