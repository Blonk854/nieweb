using System.Collections.Immutable;
using System.Globalization;

namespace Nieweb.Filters;

/// <summary>
/// Result of <see cref="FilterValidator.Validate(FilterRequest)"/> or
/// <see cref="FilterValidator.Validate(FilterClause)"/>. When
/// <see cref="IsValid"/> is <c>false</c>, <see cref="Errors"/> contains
/// one entry per problem — never empty. Errors are keyed by clause
/// index (<c>"[0].Operator"</c>, <c>"[2].Values"</c>) so an ASP.NET
/// Core <c>ValidationProblem</c> response can surface them directly.
/// </summary>
public sealed record FilterValidationResult(
    bool IsValid,
    ImmutableArray<FilterValidationError> Errors)
{
    /// <summary>The valid instance (no errors).</summary>
    public static FilterValidationResult Success { get; } =
        new(true, ImmutableArray<FilterValidationError>.Empty);
}

/// <summary>A single validation issue.</summary>
/// <param name="Key">Property path of the failing element (e.g. <c>"[0].Operator"</c>).</param>
/// <param name="Message">Human-readable explanation. Never a stack trace.</param>
public sealed record FilterValidationError(string Key, string Message);

/// <summary>
/// Structural validator for <see cref="FilterClause"/> and
/// <see cref="FilterRequest"/>. Enforces the three invariants Vieweb
/// implicitly relied upon:
/// <list type="number">
///   <item><description>The operator is allowed on the field (Vieweb §3.1.2 table).</description></item>
///   <item><description>The value count matches the operator arity.</description></item>
///   <item><description>Each value parses to the field's value kind (integer, decimal, date, string, bool).</description></item>
/// </list>
/// The validator is <em>structural</em> only: it does not check
/// existence of enum values in the underlying AOI DB (e.g. that
/// "MISSING" is a real defect name) — that belongs to the per-report
/// binder.
/// </summary>
public static class FilterValidator
{
    /// <summary>Validates a full request; aggregates errors per clause.</summary>
    public static FilterValidationResult Validate(FilterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Clauses.IsDefaultOrEmpty)
        {
            return FilterValidationResult.Success;
        }

        var errors = ImmutableArray.CreateBuilder<FilterValidationError>();
        for (var i = 0; i < request.Clauses.Length; i++)
        {
            var perClause = Validate(request.Clauses[i]);
            if (!perClause.IsValid)
            {
                var prefix = string.Create(CultureInfo.InvariantCulture, $"[{i}]");
                foreach (var e in perClause.Errors)
                {
                    errors.Add(new FilterValidationError(prefix + "." + e.Key, e.Message));
                }
            }
        }
        return errors.Count == 0
            ? FilterValidationResult.Success
            : new FilterValidationResult(false, errors.ToImmutable());
    }

    /// <summary>Validates a single clause.</summary>
    public static FilterValidationResult Validate(FilterClause clause)
    {
        ArgumentNullException.ThrowIfNull(clause);

        var errors = ImmutableArray.CreateBuilder<FilterValidationError>();

        // 1. Field must be an enum member. Wire deserialisation would
        //    normally reject unknown members, but a hand-crafted
        //    payload might still slip a raw int through.
        if (!Enum.IsDefined(clause.Field))
        {
            errors.Add(new FilterValidationError(
                nameof(FilterClause.Field),
                $"'{clause.Field}' is not a known filter field."));
            return new FilterValidationResult(false, errors.ToImmutable());
        }
        if (!Enum.IsDefined(clause.Operator))
        {
            errors.Add(new FilterValidationError(
                nameof(FilterClause.Operator),
                $"'{clause.Operator}' is not a known filter operator."));
            return new FilterValidationResult(false, errors.ToImmutable());
        }

        // 2. Operator ∈ allowed set for the field (Vieweb table).
        var allowed = FilterFieldMetadata.GetAllowedOperators(clause.Field);
        if (!allowed.Contains(clause.Operator))
        {
            errors.Add(new FilterValidationError(
                nameof(FilterClause.Operator),
                $"Operator '{clause.Operator}' is not allowed on field '{clause.Field}'."));
        }

        // 3. Operator + value-kind compatibility (e.g. Like on int fields).
        var valueKind = FilterFieldMetadata.GetValueKind(clause.Field);
        if (!FilterOperatorMetadata.SupportsValueKind(clause.Operator, valueKind))
        {
            errors.Add(new FilterValidationError(
                nameof(FilterClause.Operator),
                $"Operator '{clause.Operator}' cannot be applied to value kind '{valueKind}'."));
        }

        // 4. Arity + individual value parsing.
        var values = clause.Values.IsDefault ? ImmutableArray<string>.Empty : clause.Values;
        var arity = FilterOperatorMetadata.GetArity(clause.Operator);
        switch (arity)
        {
            case FilterOperatorArity.Single when values.Length != 1:
                errors.Add(new FilterValidationError(
                    nameof(FilterClause.Values),
                    $"Operator '{clause.Operator}' requires exactly 1 value; got {values.Length}."));
                break;
            case FilterOperatorArity.Range when values.Length != 2:
                errors.Add(new FilterValidationError(
                    nameof(FilterClause.Values),
                    $"Operator '{clause.Operator}' requires exactly 2 values (min, max); got {values.Length}."));
                break;
            case FilterOperatorArity.List when values.Length == 0:
                errors.Add(new FilterValidationError(
                    nameof(FilterClause.Values),
                    $"Operator '{clause.Operator}' requires at least 1 value."));
                break;
        }

        for (var i = 0; i < values.Length; i++)
        {
            if (!TryParseValue(values[i], valueKind, out var parseError))
            {
                errors.Add(new FilterValidationError(
                    string.Create(CultureInfo.InvariantCulture, $"{nameof(FilterClause.Values)}[{i}]"),
                    parseError!));
            }
        }

        return errors.Count == 0
            ? FilterValidationResult.Success
            : new FilterValidationResult(false, errors.ToImmutable());
    }

    private static bool TryParseValue(string raw, FilterValueKind kind, out string? error)
    {
        error = null;
        if (raw is null)
        {
            error = "Value must not be null.";
            return false;
        }
        switch (kind)
        {
            case FilterValueKind.String:
                if (raw.Length == 0)
                {
                    error = "String value must not be empty.";
                    return false;
                }
                return true;
            case FilterValueKind.Integer:
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    error = $"'{raw}' is not a valid integer.";
                    return false;
                }
                return true;
            case FilterValueKind.Decimal:
                if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                {
                    error = $"'{raw}' is not a valid decimal.";
                    return false;
                }
                return true;
            case FilterValueKind.DateTimeUtc:
                if (!DateTime.TryParse(
                        raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out _))
                {
                    error = $"'{raw}' is not a valid ISO-8601 date-time.";
                    return false;
                }
                return true;
            case FilterValueKind.Boolean:
                if (!bool.TryParse(raw, out _))
                {
                    error = $"'{raw}' is not a valid boolean.";
                    return false;
                }
                return true;
            default:
                error = $"Unknown value kind '{kind}'.";
                return false;
        }
    }
}
