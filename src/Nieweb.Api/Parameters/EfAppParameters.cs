using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Nieweb.Data;
using Nieweb.Data.Entities;

namespace Nieweb.Api.Parameters;

/// <summary>
/// EF-backed <see cref="IAppParameters"/> that persists rows through
/// <see cref="NiewebDbContext"/>. Shares the caller's scoped context so
/// writes commit inside the same unit of work as the surrounding
/// endpoint. Read paths are AsNoTracking + ordered by key for stable
/// listing output.
/// </summary>
public sealed class EfAppParameters : IAppParameters
{
    private readonly NiewebDbContext _db;
    private readonly TimeProvider _time;

    public EfAppParameters(NiewebDbContext db, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(time);
        _db = db;
        _time = time;
    }

    public async Task<IReadOnlyList<AppParameterRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.AppParameters
            .AsNoTracking()
            .OrderBy(p => p.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToRow).ToList();
    }

    public async Task<AppParameterRow?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var entity = await _db.AppParameters
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == key, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : ToRow(entity);
    }

    public async Task<AppParameterUpsertResult> UpsertAsync(
        string key,
        string valueType,
        string value,
        string? description,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueType);
        ArgumentNullException.ThrowIfNull(value);

        ValidateValueType(valueType);
        ValidateValueParses(valueType, value);

        var now = _time.GetUtcNow().UtcDateTime;
        var entity = await _db.AppParameters
            .FirstOrDefaultAsync(p => p.Key == key, cancellationToken)
            .ConfigureAwait(false);

        var created = false;
        if (entity is null)
        {
            entity = new AppParameter
            {
                Key = key,
                ValueType = valueType,
                Value = value,
                Description = description,
                IsSystem = false,
                CreatedUtc = now,
                LastModifiedUtc = now,
            };
            _db.AppParameters.Add(entity);
            created = true;
        }
        else
        {
            entity.ValueType = valueType;
            entity.Value = value;
            entity.Description = description;
            entity.LastModifiedUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new AppParameterUpsertResult(ToRow(entity), created);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var entity = await _db.AppParameters
            .FirstOrDefaultAsync(p => p.Key == key, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }
        if (entity.IsSystem)
        {
            throw new InvalidOperationException(
                $"Parameter '{key}' is system-owned and cannot be deleted.");
        }
        _db.AppParameters.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var existingKeys = await _db.AppParameters
            .Select(p => p.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingSet = new HashSet<string>(existingKeys, StringComparer.Ordinal);

        var now = _time.GetUtcNow().UtcDateTime;
        var toInsert = AppParameterDefaults.All
            .Where(d => !existingSet.Contains(d.Key))
            .Select(d => new AppParameter
            {
                Key = d.Key,
                ValueType = d.ValueType,
                Value = d.Value,
                Description = d.Description,
                IsSystem = true,
                CreatedUtc = now,
                LastModifiedUtc = now,
            })
            .ToList();

        if (toInsert.Count == 0)
        {
            return;
        }

        _db.AppParameters.AddRange(toInsert);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rejects a value type that is not one of the four canonical
    /// discriminators. Kept internal to the service so both the
    /// endpoint layer and the seeder converge on the same rules.
    /// </summary>
    internal static void ValidateValueType(string valueType)
    {
        if (valueType is
            AppParameterValueTypes.Decimal
            or AppParameterValueTypes.Int
            or AppParameterValueTypes.Bool
            or AppParameterValueTypes.String)
        {
            return;
        }
        throw new ArgumentException(
            $"Unsupported valueType '{valueType}'. Expected one of: "
            + $"{AppParameterValueTypes.Decimal}, {AppParameterValueTypes.Int}, "
            + $"{AppParameterValueTypes.Bool}, {AppParameterValueTypes.String}.",
            nameof(valueType));
    }

    /// <summary>
    /// Rejects a value that fails to parse against its declared type.
    /// Bool/int/decimal are all invariant-culture; string is accepted
    /// verbatim.
    /// </summary>
    internal static void ValidateValueParses(string valueType, string value)
    {
        switch (valueType)
        {
            case AppParameterValueTypes.Decimal:
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                {
                    throw new ArgumentException(
                        $"Value '{value}' is not a valid decimal.", nameof(value));
                }
                break;
            case AppParameterValueTypes.Int:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    throw new ArgumentException(
                        $"Value '{value}' is not a valid int.", nameof(value));
                }
                break;
            case AppParameterValueTypes.Bool:
                if (!bool.TryParse(value, out _))
                {
                    throw new ArgumentException(
                        $"Value '{value}' is not a valid bool.", nameof(value));
                }
                break;
            case AppParameterValueTypes.String:
                // No parsing constraint.
                break;
        }
    }

    private static AppParameterRow ToRow(AppParameter e) => new(
        Key: e.Key,
        ValueType: e.ValueType,
        Value: e.Value,
        Description: e.Description,
        IsSystem: e.IsSystem,
        CreatedUtc: e.CreatedUtc,
        LastModifiedUtc: e.LastModifiedUtc);
}
