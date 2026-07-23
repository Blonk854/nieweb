using Microsoft.EntityFrameworkCore;

using Nieweb.Data;
using Nieweb.Data.Entities;

namespace Nieweb.Api.ProductionLines;

/// <summary>
/// EF-backed <see cref="IProductionLines"/> that persists rows through
/// <see cref="NiewebDbContext"/>. Shares the caller's scoped context so
/// writes commit inside the same unit of work as the surrounding
/// endpoint. Read paths are <c>AsNoTracking</c> with deterministic
/// ordering.
/// </summary>
public sealed class EfProductionLines : IProductionLines
{
    private readonly NiewebDbContext _db;
    private readonly TimeProvider _time;

    public EfProductionLines(NiewebDbContext db, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(time);
        _db = db;
        _time = time;
    }

    public async Task<IReadOnlyList<ProductionLineRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.ProductionLines
            .AsNoTracking()
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.Name)
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.DisplayOrder,
                MachineCount = l.Machines.Count,
                l.CreatedUtc,
                l.LastModifiedUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(r => new ProductionLineRow(
                r.Id, r.Name, r.DisplayOrder, r.MachineCount, r.CreatedUtc, r.LastModifiedUtc))
            .ToList();
    }

    public async Task<ProductionLineDetail?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var line = await _db.ProductionLines
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (line is null)
        {
            return null;
        }

        var machines = await _db.ProductionLineMachines
            .AsNoTracking()
            .Where(m => m.ProductionLineId == id)
            .OrderBy(m => m.DisplayOrder)
            .ThenBy(m => m.MachineName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lineRow = new ProductionLineRow(
            line.Id, line.Name, line.DisplayOrder, machines.Count, line.CreatedUtc, line.LastModifiedUtc);
        return new ProductionLineDetail(lineRow, machines.Select(ToMachineRow).ToList());
    }

    public async Task<ProductionLineRow> CreateAsync(
        string name,
        int displayOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmed = name.Trim();
        var duplicate = await _db.ProductionLines
            .AnyAsync(l => l.Name == trimmed, cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
        {
            throw new ProductionLineConflictException(
                $"Production line '{trimmed}' already exists.");
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var entity = new ProductionLine
        {
            Name = trimmed,
            DisplayOrder = displayOrder,
            CreatedUtc = now,
            LastModifiedUtc = now,
        };
        _db.ProductionLines.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ProductionLineRow(
            entity.Id, entity.Name, entity.DisplayOrder, 0, entity.CreatedUtc, entity.LastModifiedUtc);
    }

    public async Task<ProductionLineRow?> UpdateAsync(
        int id,
        string name,
        int displayOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var entity = await _db.ProductionLines
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        var trimmed = name.Trim();
        if (!string.Equals(entity.Name, trimmed, StringComparison.Ordinal))
        {
            var duplicate = await _db.ProductionLines
                .AnyAsync(l => l.Id != id && l.Name == trimmed, cancellationToken)
                .ConfigureAwait(false);
            if (duplicate)
            {
                throw new ProductionLineConflictException(
                    $"Production line '{trimmed}' already exists.");
            }
        }

        entity.Name = trimmed;
        entity.DisplayOrder = displayOrder;
        entity.LastModifiedUtc = _time.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var machineCount = await _db.ProductionLineMachines
            .CountAsync(m => m.ProductionLineId == id, cancellationToken)
            .ConfigureAwait(false);
        return new ProductionLineRow(
            entity.Id, entity.Name, entity.DisplayOrder, machineCount, entity.CreatedUtc, entity.LastModifiedUtc);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ProductionLines
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }
        _db.ProductionLines.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<ProductionLineMachineRow?> AddMachineAsync(
        int lineId,
        string sourceId,
        int machineId,
        string machineName,
        string? category,
        int displayOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);

        var lineExists = await _db.ProductionLines
            .AnyAsync(l => l.Id == lineId, cancellationToken)
            .ConfigureAwait(false);
        if (!lineExists)
        {
            return null;
        }

        var trimmedSource = sourceId.Trim();
        var duplicate = await _db.ProductionLineMachines
            .AnyAsync(m => m.SourceId == trimmedSource && m.MachineId == machineId, cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
        {
            throw new ProductionLineConflictException(
                $"Machine ({trimmedSource}, {machineId}) is already assigned to a production line.");
        }

        var entity = new ProductionLineMachine
        {
            ProductionLineId = lineId,
            SourceId = trimmedSource,
            MachineId = machineId,
            MachineName = machineName.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            DisplayOrder = displayOrder,
            CreatedUtc = _time.GetUtcNow().UtcDateTime,
        };
        _db.ProductionLineMachines.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Bump the parent line's LastModifiedUtc so the admin UI's
        // "last edited" column reflects the assignment change.
        var line = await _db.ProductionLines
            .FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken)
            .ConfigureAwait(false);
        if (line is not null)
        {
            line.LastModifiedUtc = entity.CreatedUtc;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return ToMachineRow(entity);
    }

    public async Task<bool> RemoveMachineAsync(
        int lineId,
        int machineAssignmentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.ProductionLineMachines
            .FirstOrDefaultAsync(
                m => m.Id == machineAssignmentId && m.ProductionLineId == lineId,
                cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }
        _db.ProductionLineMachines.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var line = await _db.ProductionLines
            .FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken)
            .ConfigureAwait(false);
        if (line is not null)
        {
            line.LastModifiedUtc = _time.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private static ProductionLineMachineRow ToMachineRow(ProductionLineMachine m) => new(
        Id: m.Id,
        ProductionLineId: m.ProductionLineId,
        SourceId: m.SourceId,
        MachineId: m.MachineId,
        MachineName: m.MachineName,
        Category: m.Category,
        DisplayOrder: m.DisplayOrder,
        CreatedUtc: m.CreatedUtc);
}
