using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Nieweb.Data;
using Nieweb.Data.Entities;

namespace Nieweb.Api.Reports;

/// <summary>
/// EF-backed <see cref="IReports"/> that persists all three report
/// composition entities through <see cref="NiewebDbContext"/>. Read
/// paths use <c>AsNoTracking</c> with deterministic ordering
/// (DisplayOrder, then natural label) to match the admin UI's
/// expected sort. Write paths bump <see cref="Report.LastModifiedUtc"/>
/// on the parent report whenever a tile is added, updated, or
/// removed so the "last edited" column in the report list stays
/// truthful.
/// </summary>
public sealed class EfReports : IReports
{
    private readonly NiewebDbContext _db;
    private readonly TimeProvider _time;
    private readonly IPasswordHasher<Report> _lockHasher;

    public EfReports(
        NiewebDbContext db,
        TimeProvider time,
        IPasswordHasher<Report> lockHasher)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(lockHasher);
        _db = db;
        _time = time;
        _lockHasher = lockHasher;
    }

    // -------------------- Groups --------------------

    public async Task<IReadOnlyList<ReportGroupRow>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.ReportGroups
            .AsNoTracking()
            .OrderBy(g => g.DisplayOrder)
            .ThenBy(g => g.Name)
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.DisplayOrder,
                ReportCount = g.Reports.Count,
                g.CreatedUtc,
                g.LastModifiedUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => new ReportGroupRow(
            r.Id, r.Name, r.DisplayOrder, r.ReportCount, r.CreatedUtc, r.LastModifiedUtc)).ToList();
    }

    public async Task<ReportGroupRow> CreateGroupAsync(
        string name,
        int displayOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        var duplicate = await _db.ReportGroups
            .AnyAsync(g => g.Name == trimmed, cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
        {
            throw new ReportConflictException($"Report group '{trimmed}' already exists.");
        }
        var now = _time.GetUtcNow().UtcDateTime;
        var entity = new ReportGroup
        {
            Name = trimmed,
            DisplayOrder = displayOrder,
            CreatedUtc = now,
            LastModifiedUtc = now,
        };
        _db.ReportGroups.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ReportGroupRow(entity.Id, entity.Name, entity.DisplayOrder, 0, entity.CreatedUtc, entity.LastModifiedUtc);
    }

    public async Task<ReportGroupRow?> UpdateGroupAsync(
        int id,
        string name,
        int displayOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var entity = await _db.ReportGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }
        var trimmed = name.Trim();
        if (!string.Equals(entity.Name, trimmed, StringComparison.Ordinal))
        {
            var duplicate = await _db.ReportGroups
                .AnyAsync(g => g.Id != id && g.Name == trimmed, cancellationToken)
                .ConfigureAwait(false);
            if (duplicate)
            {
                throw new ReportConflictException($"Report group '{trimmed}' already exists.");
            }
        }
        entity.Name = trimmed;
        entity.DisplayOrder = displayOrder;
        entity.LastModifiedUtc = _time.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var reportCount = await _db.Reports
            .CountAsync(r => r.ReportGroupId == id, cancellationToken)
            .ConfigureAwait(false);
        return new ReportGroupRow(entity.Id, entity.Name, entity.DisplayOrder, reportCount, entity.CreatedUtc, entity.LastModifiedUtc);
    }

    public async Task<bool> DeleteGroupAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ReportGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }
        _db.ReportGroups.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // -------------------- Reports --------------------

    public async Task<IReadOnlyList<ReportRow>> ListReportsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.Reports
            .AsNoTracking()
            .OrderBy(r => r.DisplayOrder)
            .ThenBy(r => r.Title)
            .Select(r => new
            {
                r.Id,
                r.Title,
                r.Description,
                r.ReportGroupId,
                GroupName = r.Group == null ? null : r.Group.Name,
                r.OwnerUserId,
                r.OwnerDisplayName,
                r.IsLocked,
                r.IsPinnedHome,
                r.RefreshFrequencySeconds,
                r.ChromeJson,
                r.DisplayOrder,
                EntityCount = r.Entities.Count,
                r.CreatedUtc,
                r.LastModifiedUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => new ReportRow(
            r.Id, r.Title, r.Description, r.ReportGroupId, r.GroupName,
            r.OwnerUserId, r.OwnerDisplayName, r.IsLocked, r.IsPinnedHome,
            r.RefreshFrequencySeconds, r.ChromeJson, r.DisplayOrder,
            r.EntityCount, r.CreatedUtc, r.LastModifiedUtc)).ToList();
    }

    public async Task<IReadOnlyList<ReportRow>> ListHomeReportsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.Reports
            .AsNoTracking()
            .Where(r => r.IsPinnedHome)
            .OrderBy(r => r.DisplayOrder)
            .ThenBy(r => r.Title)
            .Select(r => new
            {
                r.Id,
                r.Title,
                r.Description,
                r.ReportGroupId,
                GroupName = r.Group == null ? null : r.Group.Name,
                r.OwnerUserId,
                r.OwnerDisplayName,
                r.IsLocked,
                r.IsPinnedHome,
                r.RefreshFrequencySeconds,
                r.ChromeJson,
                r.DisplayOrder,
                EntityCount = r.Entities.Count,
                r.CreatedUtc,
                r.LastModifiedUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => new ReportRow(
            r.Id, r.Title, r.Description, r.ReportGroupId, r.GroupName,
            r.OwnerUserId, r.OwnerDisplayName, r.IsLocked, r.IsPinnedHome,
            r.RefreshFrequencySeconds, r.ChromeJson, r.DisplayOrder,
            r.EntityCount, r.CreatedUtc, r.LastModifiedUtc)).ToList();
    }

    public async Task<ReportDetail?> GetReportAsync(int id, CancellationToken cancellationToken = default)
    {
        var report = await _db.Reports
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new
            {
                r.Id,
                r.Title,
                r.Description,
                r.ReportGroupId,
                GroupName = r.Group == null ? null : r.Group.Name,
                r.OwnerUserId,
                r.OwnerDisplayName,
                r.IsLocked,
                r.IsPinnedHome,
                r.RefreshFrequencySeconds,
                r.ChromeJson,
                r.DisplayOrder,
                r.CreatedUtc,
                r.LastModifiedUtc,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (report is null)
        {
            return null;
        }
        var entities = await _db.ReportEntities
            .AsNoTracking()
            .Where(e => e.ReportId == id)
            .OrderBy(e => e.DisplayOrder)
            .ThenBy(e => e.Id)
            .Select(e => new ReportEntityRow(
                e.Id, e.ReportId, e.TileType, e.Title, e.DisplayOrder, e.ConfigJson,
                e.CreatedUtc, e.LastModifiedUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var row = new ReportRow(
            report.Id, report.Title, report.Description, report.ReportGroupId, report.GroupName,
            report.OwnerUserId, report.OwnerDisplayName, report.IsLocked, report.IsPinnedHome,
            report.RefreshFrequencySeconds, report.ChromeJson, report.DisplayOrder,
            entities.Count, report.CreatedUtc, report.LastModifiedUtc);
        return new ReportDetail(row, entities);
    }

    public async Task<ReportRow> CreateReportAsync(
        CreateReportInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.OwnerDisplayName);

        if (input.ReportGroupId is int gid)
        {
            var groupExists = await _db.ReportGroups.AnyAsync(g => g.Id == gid, cancellationToken).ConfigureAwait(false);
            if (!groupExists)
            {
                throw new ReportConflictException($"Report group {gid} does not exist.");
            }
        }
        ValidateRefresh(input.RefreshFrequencySeconds);

        var now = _time.GetUtcNow().UtcDateTime;
        var entity = new Report
        {
            Title = input.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            ReportGroupId = input.ReportGroupId,
            OwnerUserId = input.OwnerUserId,
            OwnerDisplayName = input.OwnerDisplayName.Trim(),
            // Locking is exclusively driven by /lock and /unlock in RC3;
            // Create always yields an unlocked report regardless of the
            // (vestigial) IsLocked bit on the request.
            IsLocked = false,
            LockPasswordHash = null,
            IsPinnedHome = input.IsPinnedHome,
            RefreshFrequencySeconds = input.RefreshFrequencySeconds,
            ChromeJson = string.IsNullOrWhiteSpace(input.ChromeJson) ? null : input.ChromeJson,
            DisplayOrder = input.DisplayOrder,
            CreatedUtc = now,
            LastModifiedUtc = now,
        };
        _db.Reports.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ProjectReportAsync(entity.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Report vanished right after insert.");
    }

    public async Task<ReportRow?> UpdateReportAsync(
        int id,
        UpdateReportInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Title);

        var entity = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }
        if (input.ReportGroupId is int gid)
        {
            var groupExists = await _db.ReportGroups.AnyAsync(g => g.Id == gid, cancellationToken).ConfigureAwait(false);
            if (!groupExists)
            {
                throw new ReportConflictException($"Report group {gid} does not exist.");
            }
        }
        ValidateRefresh(input.RefreshFrequencySeconds);

        entity.Title = input.Title.Trim();
        entity.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        entity.ReportGroupId = input.ReportGroupId;
        // IsLocked is deliberately preserved here; /lock and /unlock own it
        // in RC3, and the header PUT ignores the incoming bit.
        entity.IsPinnedHome = input.IsPinnedHome;
        entity.RefreshFrequencySeconds = input.RefreshFrequencySeconds;
        entity.ChromeJson = string.IsNullOrWhiteSpace(input.ChromeJson) ? null : input.ChromeJson;
        entity.DisplayOrder = input.DisplayOrder;
        entity.LastModifiedUtc = _time.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ProjectReportAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteReportAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }
        _db.Reports.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // -------------------- Entities (tiles) --------------------

    public async Task<ReportEntityRow?> AddEntityAsync(
        int reportId,
        AddEntityInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.TileType);

        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return null;
        }
        var now = _time.GetUtcNow().UtcDateTime;
        var order = input.DisplayOrder;
        if (order < 0)
        {
            var maxOrder = await _db.ReportEntities
                .Where(e => e.ReportId == reportId)
                .Select(e => (int?)e.DisplayOrder)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false);
            order = (maxOrder ?? -1) + 1;
        }
        var entity = new ReportEntity
        {
            ReportId = reportId,
            TileType = input.TileType.Trim(),
            Title = string.IsNullOrWhiteSpace(input.Title) ? null : input.Title.Trim(),
            DisplayOrder = order,
            ConfigJson = string.IsNullOrWhiteSpace(input.ConfigJson) ? "{}" : input.ConfigJson,
            CreatedUtc = now,
            LastModifiedUtc = now,
        };
        _db.ReportEntities.Add(entity);
        report.LastModifiedUtc = now;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ReportEntityRow(
            entity.Id, entity.ReportId, entity.TileType, entity.Title, entity.DisplayOrder,
            entity.ConfigJson, entity.CreatedUtc, entity.LastModifiedUtc);
    }

    public async Task<ReportEntityRow?> UpdateEntityAsync(
        int reportId,
        int entityId,
        UpdateEntityInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.TileType);

        var entity = await _db.ReportEntities
            .FirstOrDefaultAsync(e => e.Id == entityId && e.ReportId == reportId, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }
        var now = _time.GetUtcNow().UtcDateTime;
        entity.TileType = input.TileType.Trim();
        entity.Title = string.IsNullOrWhiteSpace(input.Title) ? null : input.Title.Trim();
        entity.DisplayOrder = input.DisplayOrder;
        entity.ConfigJson = string.IsNullOrWhiteSpace(input.ConfigJson) ? "{}" : input.ConfigJson;
        entity.LastModifiedUtc = now;

        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken).ConfigureAwait(false);
        if (report is not null)
        {
            report.LastModifiedUtc = now;
        }
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ReportEntityRow(
            entity.Id, entity.ReportId, entity.TileType, entity.Title, entity.DisplayOrder,
            entity.ConfigJson, entity.CreatedUtc, entity.LastModifiedUtc);
    }

    public async Task<bool> RemoveEntityAsync(int reportId, int entityId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ReportEntities
            .FirstOrDefaultAsync(e => e.Id == entityId && e.ReportId == reportId, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }
        _db.ReportEntities.Remove(entity);
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken).ConfigureAwait(false);
        if (report is not null)
        {
            report.LastModifiedUtc = _time.GetUtcNow().UtcDateTime;
        }
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // -------------------- Lock / unlock / duplicate (RC3) --------------------

    public async Task<LockOutcome> LockReportAsync(
        int id,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return new LockOutcome(LockResult.PasswordEmpty, null);
        }
        var entity = await _db.Reports
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return new LockOutcome(LockResult.NotFound, null);
        }
        entity.LockPasswordHash = _lockHasher.HashPassword(entity, password);
        entity.IsLocked = true;
        entity.LastModifiedUtc = _time.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var row = await ProjectReportAsync(id, cancellationToken).ConfigureAwait(false);
        return new LockOutcome(LockResult.Success, row);
    }

    public async Task<UnlockOutcome> UnlockReportAsync(
        int id,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        var entity = await _db.Reports
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return new UnlockOutcome(UnlockResult.NotFound, null);
        }
        if (!entity.IsLocked || entity.LockPasswordHash is null)
        {
            return new UnlockOutcome(UnlockResult.NotLocked, null);
        }
        var verify = _lockHasher.VerifyHashedPassword(entity, entity.LockPasswordHash, password);
        if (verify == PasswordVerificationResult.Failed)
        {
            return new UnlockOutcome(UnlockResult.WrongPassword, null);
        }
        entity.IsLocked = false;
        entity.LockPasswordHash = null;
        entity.LastModifiedUtc = _time.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var row = await ProjectReportAsync(id, cancellationToken).ConfigureAwait(false);
        return new UnlockOutcome(UnlockResult.Success, row);
    }

    public async Task<ReportRow?> DuplicateReportAsync(
        int id,
        DuplicateReportInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.OwnerDisplayName);

        var source = await _db.Reports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (source is null)
        {
            return null;
        }
        var sourceEntities = await _db.ReportEntities
            .AsNoTracking()
            .Where(e => e.ReportId == id)
            .OrderBy(e => e.DisplayOrder)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = _time.GetUtcNow().UtcDateTime;
        var clone = new Report
        {
            Title = input.Title.Trim(),
            Description = source.Description,
            // Group affiliation carries over — the duplicate lives beside
            // the source in the same navigation section by default.
            ReportGroupId = source.ReportGroupId,
            OwnerUserId = input.OwnerUserId,
            OwnerDisplayName = input.OwnerDisplayName.Trim(),
            // Duplicates always start unlocked and un-pinned so the new
            // owner can freely edit before sharing.
            IsLocked = false,
            LockPasswordHash = null,
            IsPinnedHome = false,
            RefreshFrequencySeconds = source.RefreshFrequencySeconds,
            ChromeJson = source.ChromeJson,
            DisplayOrder = source.DisplayOrder,
            CreatedUtc = now,
            LastModifiedUtc = now,
        };
        _db.Reports.Add(clone);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var e in sourceEntities)
        {
            _db.ReportEntities.Add(new ReportEntity
            {
                ReportId = clone.Id,
                TileType = e.TileType,
                Title = e.Title,
                DisplayOrder = e.DisplayOrder,
                ConfigJson = e.ConfigJson,
                CreatedUtc = now,
                LastModifiedUtc = now,
            });
        }
        if (sourceEntities.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return await ProjectReportAsync(clone.Id, cancellationToken).ConfigureAwait(false);
    }

    // -------------------- Pin / unpin (F14) --------------------

    public async Task<ReportRow?> SetPinnedHomeAsync(
        int id,
        bool pinned,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.Reports
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }
        entity.IsPinnedHome = pinned;
        entity.LastModifiedUtc = _time.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ProjectReportAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateRefresh(int? refreshSeconds)
    {
        if (refreshSeconds is int s && s <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshSeconds),
                s,
                "Refresh frequency must be positive when supplied.");
        }
    }

    private async Task<ReportRow?> ProjectReportAsync(int id, CancellationToken cancellationToken)
    {
        return await _db.Reports
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new ReportRow(
                r.Id,
                r.Title,
                r.Description,
                r.ReportGroupId,
                r.Group == null ? null : r.Group.Name,
                r.OwnerUserId,
                r.OwnerDisplayName,
                r.IsLocked,
                r.IsPinnedHome,
                r.RefreshFrequencySeconds,
                r.ChromeJson,
                r.DisplayOrder,
                r.Entities.Count,
                r.CreatedUtc,
                r.LastModifiedUtc))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
