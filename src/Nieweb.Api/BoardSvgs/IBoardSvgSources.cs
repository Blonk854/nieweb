using Nieweb.Data.Entities;

namespace Nieweb.Api.BoardSvgs;

/// <summary>
/// Read/write access to <see cref="BoardSvgSource"/> rows (TC4
/// Phase A). Backs the admin "Board SVG sources" page and is the
/// input list for the sync <c>IHostedService</c> that lands in
/// Phase B.
/// </summary>
/// <remarks>
/// The service enforces the "unique machine name" invariant at the
/// DB layer via a unique index; attempts to add or rename a source
/// so its name collides throw <see cref="BoardSvgSourceConflictException"/>
/// which the endpoint layer surfaces as HTTP 409. Sync-status
/// mutation (<see cref="RecordSyncSuccessAsync"/> /
/// <see cref="RecordSyncFailureAsync"/>) is on the interface so
/// Phase B can update the row without going through the admin
/// endpoints.
/// </remarks>
public interface IBoardSvgSources
{
    /// <summary>Returns every source, ordered by machine name.</summary>
    Task<IReadOnlyList<BoardSvgSourceRow>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the source, or <c>null</c> if none.
    /// </summary>
    Task<BoardSvgSourceRow?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new source. Throws
    /// <see cref="BoardSvgSourceConflictException"/> if the machine
    /// name is already used.
    /// </summary>
    Task<BoardSvgSourceRow> CreateAsync(
        string machineName,
        string uncPath,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames / re-paths / toggles a source. Returns <c>null</c> when
    /// the row does not exist. Throws
    /// <see cref="BoardSvgSourceConflictException"/> on name clash.
    /// </summary>
    Task<BoardSvgSourceRow?> UpdateAsync(
        int id,
        string machineName,
        string uncPath,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a source. Returns <c>true</c> if a row was removed.
    /// The local cache directory is left intact.
    /// </summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a successful sync sweep (updates
    /// <see cref="BoardSvgSource.LastSyncedUtc"/> and clears the
    /// error columns). No-op when the row does not exist.
    /// </summary>
    Task RecordSyncSuccessAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed sync sweep (updates
    /// <see cref="BoardSvgSource.LastSyncErrorUtc"/> and the
    /// diagnostic message; leaves <see cref="BoardSvgSource.LastSyncedUtc"/>
    /// untouched so operators can tell whether the source has ever
    /// worked). No-op when the row does not exist.
    /// </summary>
    Task RecordSyncFailureAsync(int id, string errorMessage, CancellationToken cancellationToken = default);
}

/// <summary>Row snapshot returned by list / create / update.</summary>
public sealed record BoardSvgSourceRow(
    int Id,
    string MachineName,
    string UncPath,
    bool IsEnabled,
    DateTime? LastSyncedUtc,
    DateTime? LastSyncErrorUtc,
    string? LastSyncError,
    DateTime CreatedUtc,
    DateTime LastModifiedUtc);

/// <summary>
/// Thrown when a create / update call would violate the unique
/// machine-name invariant. Endpoints surface these as HTTP 409.
/// </summary>
public sealed class BoardSvgSourceConflictException : InvalidOperationException
{
    public BoardSvgSourceConflictException(string message) : base(message) { }
    public BoardSvgSourceConflictException(string message, Exception inner) : base(message, inner) { }
    public BoardSvgSourceConflictException() { }
}
