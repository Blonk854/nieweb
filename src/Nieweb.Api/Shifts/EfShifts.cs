using Microsoft.EntityFrameworkCore;

using Nieweb.Data;
using Nieweb.Data.Entities;
using Nieweb.Reports.Common;

namespace Nieweb.Api.Shifts;

/// <summary>
/// EF-backed <see cref="IShifts"/>. The shift cycle is treated as a
/// single, atomic unit: <see cref="ReplaceAsync"/> deletes every existing
/// breakpoint inside the same <c>SaveChangesAsync</c> as it inserts the
/// new one, matching Vieweb's "one shift definition per site" model.
/// </summary>
public sealed class EfShifts : IShifts
{
    private readonly NiewebDbContext _db;
    private readonly TimeProvider _time;

    public EfShifts(NiewebDbContext db, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(time);
        _db = db;
        _time = time;
    }

    public async Task<IReadOnlyList<ShiftBreakpointRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.ShiftBreakpoints
            .AsNoTracking()
            .OrderBy(s => s.Hour)
            .ThenBy(s => s.Minute)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToRow).ToList();
    }

    public async Task<IReadOnlyList<ShiftBreakpointRow>> ReplaceAsync(
        IEnumerable<ShiftBreakpointInput> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var normalised = Normalise(entries);
        var existing = await _db.ShiftBreakpoints
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _db.ShiftBreakpoints.RemoveRange(existing);

        var now = _time.GetUtcNow().UtcDateTime;
        var order = 0;
        var inserted = new List<ShiftBreakpoint>();
        foreach (var entry in normalised)
        {
            var entity = new ShiftBreakpoint
            {
                Hour = entry.Hour,
                Minute = entry.Minute,
                Label = string.IsNullOrWhiteSpace(entry.Label) ? null : entry.Label.Trim(),
                DisplayOrder = order++,
                CreatedUtc = now,
                LastModifiedUtc = now,
            };
            _db.ShiftBreakpoints.Add(entity);
            inserted.Add(entity);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return inserted.Select(ToRow).ToList();
    }

    public async Task<ShiftDefinition?> BuildShiftDefinitionAsync(CancellationToken cancellationToken = default)
    {
        var rows = await ListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return null;
        }
        var starts = rows.Select(r => new TimeOnly(r.Hour, r.Minute)).ToArray();
        var labels = rows
            .Select((r, i) => string.IsNullOrWhiteSpace(r.Label)
                ? $"Shift {(i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                : r.Label!)
            .ToArray();
        return ShiftDefinition.FromStarts(starts, labels);
    }

    /// <summary>
    /// Validates and sorts the caller's input into ascending
    /// <c>(Hour, Minute)</c> order. Public for endpoint use so the API
    /// layer can surface parse errors before they hit the DB.
    /// </summary>
    internal static List<ShiftBreakpointInput> Normalise(IEnumerable<ShiftBreakpointInput> entries)
    {
        var list = new List<ShiftBreakpointInput>();
        var seen = new HashSet<int>();
        foreach (var entry in entries)
        {
            if (entry.Hour is < 0 or > 23)
            {
                throw new ArgumentException(
                    $"Shift hour {entry.Hour} is out of range (0-23).",
                    nameof(entries));
            }
            if (entry.Minute is < 0 or > 59)
            {
                throw new ArgumentException(
                    $"Shift minute {entry.Minute} is out of range (0-59).",
                    nameof(entries));
            }
            var key = (entry.Hour * 60) + entry.Minute;
            if (!seen.Add(key))
            {
                throw new ArgumentException(
                    $"Duplicate shift breakpoint {entry.Hour:D2}:{entry.Minute:D2}.",
                    nameof(entries));
            }
            list.Add(entry);
        }
        list.Sort((a, b) =>
        {
            var byHour = a.Hour.CompareTo(b.Hour);
            return byHour != 0 ? byHour : a.Minute.CompareTo(b.Minute);
        });
        return list;
    }

    private static ShiftBreakpointRow ToRow(ShiftBreakpoint s) => new(
        Id: s.Id,
        Hour: s.Hour,
        Minute: s.Minute,
        Label: s.Label,
        DisplayOrder: s.DisplayOrder,
        CreatedUtc: s.CreatedUtc,
        LastModifiedUtc: s.LastModifiedUtc);
}
