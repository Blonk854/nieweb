using Microsoft.EntityFrameworkCore;

using Nieweb.Data;
using Nieweb.Data.Entities;

namespace Nieweb.Api.BoardSvgs;

/// <summary>
/// EF-backed <see cref="IBoardSvgSources"/> that persists rows through
/// <see cref="NiewebDbContext"/>. Shares the caller's scoped context so
/// writes commit inside the same unit of work as the surrounding
/// endpoint (or hosted-service scope for Phase B). Read paths are
/// <c>AsNoTracking</c> with deterministic ordering.
/// </summary>
public sealed class EfBoardSvgSources : IBoardSvgSources
{
    private readonly NiewebDbContext _db;
    private readonly TimeProvider _time;

    public EfBoardSvgSources(NiewebDbContext db, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(time);
        _db = db;
        _time = time;
    }

    public async Task<IReadOnlyList<BoardSvgSourceRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.BoardSvgSources
            .AsNoTracking()
            .OrderBy(s => s.MachineName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToRow).ToList();
    }

    public async Task<BoardSvgSourceRow?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var row = await _db.BoardSvgSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToRow(row);
    }

    public async Task<BoardSvgSourceRow> CreateAsync(
        string machineName,
        string uncPath,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(uncPath);

        var trimmedName = machineName.Trim();
        var trimmedPath = uncPath.Trim();

        var duplicate = await _db.BoardSvgSources
            .AnyAsync(s => s.MachineName == trimmedName, cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
        {
            throw new BoardSvgSourceConflictException(
                $"Board-SVG source '{trimmedName}' already exists.");
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var entity = new BoardSvgSource
        {
            MachineName = trimmedName,
            UncPath = trimmedPath,
            IsEnabled = isEnabled,
            CreatedUtc = now,
            LastModifiedUtc = now,
        };
        _db.BoardSvgSources.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToRow(entity);
    }

    public async Task<BoardSvgSourceRow?> UpdateAsync(
        int id,
        string machineName,
        string uncPath,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(uncPath);

        var entity = await _db.BoardSvgSources
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        var trimmedName = machineName.Trim();
        var trimmedPath = uncPath.Trim();

        // Guard against renaming into a sibling row.
        var conflict = await _db.BoardSvgSources
            .AnyAsync(
                s => s.Id != id && s.MachineName == trimmedName,
                cancellationToken)
            .ConfigureAwait(false);
        if (conflict)
        {
            throw new BoardSvgSourceConflictException(
                $"Board-SVG source '{trimmedName}' already exists.");
        }

        entity.MachineName = trimmedName;
        entity.UncPath = trimmedPath;
        entity.IsEnabled = isEnabled;
        entity.LastModifiedUtc = _time.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToRow(entity);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.BoardSvgSources
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }
        _db.BoardSvgSources.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task RecordSyncSuccessAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.BoardSvgSources
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }
        entity.LastSyncedUtc = _time.GetUtcNow().UtcDateTime;
        entity.LastSyncErrorUtc = null;
        entity.LastSyncError = null;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordSyncFailureAsync(int id, string errorMessage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(errorMessage);
        var entity = await _db.BoardSvgSources
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }
        entity.LastSyncErrorUtc = _time.GetUtcNow().UtcDateTime;
        // Truncate noisy stack traces so a single loud error cannot
        // bloat the row past its 500-char cap.
        entity.LastSyncError = errorMessage.Length <= 500
            ? errorMessage
            : errorMessage[..500];
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static BoardSvgSourceRow ToRow(BoardSvgSource e) => new(
        e.Id,
        e.MachineName,
        e.UncPath,
        e.IsEnabled,
        e.LastSyncedUtc,
        e.LastSyncErrorUtc,
        e.LastSyncError,
        e.CreatedUtc,
        e.LastModifiedUtc);
}
