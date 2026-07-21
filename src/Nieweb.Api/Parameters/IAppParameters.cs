using System.Globalization;

using Nieweb.Data.Entities;

namespace Nieweb.Api.Parameters;

/// <summary>
/// Read/write access to the internal <c>AppParameters</c> table. Backs
/// the "Application parameters" admin page (Vieweb §2.4.2) and is the
/// canonical source for report-wide tuning knobs such as tolerance
/// intervals, the GR&amp;R constant, the confidence coefficient, and
/// the global batch-enabled master switch.
/// </summary>
/// <remarks>
/// Values are stored as invariant-culture strings; callers use
/// <see cref="AppParameterRow.AsDecimal"/> / <see cref="AppParameterRow.AsBool"/>
/// / <see cref="AppParameterRow.AsInt"/> to parse when they know the
/// expected type. Sole write API is <see cref="UpsertAsync"/> (create
/// or update) plus <see cref="DeleteAsync"/> (non-system rows only).
/// </remarks>
public interface IAppParameters
{
    /// <summary>Returns every parameter ordered by key ascending.</summary>
    Task<IReadOnlyList<AppParameterRow>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the row for <paramref name="key"/>, or <c>null</c> if none.</summary>
    Task<AppParameterRow?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the parameter if it is missing, or updates its value /
    /// type / description if it exists. Returns the resulting row plus
    /// a flag indicating whether the row was newly inserted (for
    /// audit-log routing).
    /// </summary>
    Task<AppParameterUpsertResult> UpsertAsync(
        string key,
        string valueType,
        string value,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the parameter and returns <c>true</c> if a row was
    /// removed. System-owned rows (<see cref="AppParameter.IsSystem"/>)
    /// cannot be deleted; the method throws
    /// <see cref="InvalidOperationException"/> in that case.
    /// </summary>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures every default in
    /// <see cref="AppParameterDefaults.All"/> is present in the table.
    /// Existing rows are left untouched (admins may have tuned them).
    /// Idempotent — safe to call on every host boot.
    /// </summary>
    Task EnsureSeededAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable snapshot of an <see cref="AppParameter"/> row plus typed
/// parsing helpers.
/// </summary>
public sealed record AppParameterRow(
    string Key,
    string ValueType,
    string Value,
    string? Description,
    bool IsSystem,
    DateTime CreatedUtc,
    DateTime LastModifiedUtc)
{
    /// <summary>
    /// Parses <see cref="Value"/> as <see cref="decimal"/> using
    /// <see cref="CultureInfo.InvariantCulture"/>. Throws
    /// <see cref="InvalidOperationException"/> if the type does not
    /// match or the value cannot be parsed.
    /// </summary>
    public decimal AsDecimal()
    {
        if (ValueType != AppParameterValueTypes.Decimal)
        {
            throw new InvalidOperationException(
                $"Parameter '{Key}' has type '{ValueType}', not '{AppParameterValueTypes.Decimal}'.");
        }
        if (!decimal.TryParse(Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
        {
            throw new InvalidOperationException(
                $"Parameter '{Key}' value '{Value}' is not a valid decimal.");
        }
        return d;
    }

    /// <summary>
    /// Parses <see cref="Value"/> as <see cref="int"/> using
    /// <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    public int AsInt()
    {
        if (ValueType != AppParameterValueTypes.Int)
        {
            throw new InvalidOperationException(
                $"Parameter '{Key}' has type '{ValueType}', not '{AppParameterValueTypes.Int}'.");
        }
        if (!int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
        {
            throw new InvalidOperationException(
                $"Parameter '{Key}' value '{Value}' is not a valid int.");
        }
        return i;
    }

    /// <summary>Parses <see cref="Value"/> as <see cref="bool"/> (case-insensitive).</summary>
    public bool AsBool()
    {
        if (ValueType != AppParameterValueTypes.Bool)
        {
            throw new InvalidOperationException(
                $"Parameter '{Key}' has type '{ValueType}', not '{AppParameterValueTypes.Bool}'.");
        }
        if (!bool.TryParse(Value, out var b))
        {
            throw new InvalidOperationException(
                $"Parameter '{Key}' value '{Value}' is not a valid bool.");
        }
        return b;
    }
}

/// <summary>
/// Result of <see cref="IAppParameters.UpsertAsync"/>. The
/// <see cref="Created"/> flag lets the caller emit the correct audit
/// event (<c>app.parameter.created</c> vs <c>app.parameter.updated</c>).
/// </summary>
public sealed record AppParameterUpsertResult(AppParameterRow Row, bool Created);
