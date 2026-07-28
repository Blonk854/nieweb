using System.Globalization;

namespace Nieweb.Filters;

/// <summary>
/// Supplies a report row's value(s) for a <see cref="FilterField"/> so
/// <see cref="FilterEvaluator"/> can test a <see cref="FilterClause"/>
/// against it in memory. The report layer implements this over its
/// concrete row type (e.g. <c>TESTED_OBJECT</c> or <c>PANELS</c>/<c>CARDS</c>
/// rows) and resolves id → name lookups (product / machine / defect) before
/// handing values back as invariant-culture strings.
/// </summary>
/// <remarks>
/// A single row can carry <em>zero, one, or many</em> tokens for one field:
/// <list type="bullet">
///   <item><description>Zero — the row has no value (e.g. a null part number). Positive operators never match; negative operators (Different / NotIn / NotLike / NotBetween) match, so "field is not X" includes rows with no value.</description></item>
///   <item><description>One — a scalar field (bar code, board number, status).</description></item>
///   <item><description>Many — a set-membership field such as <see cref="FilterField.Defect"/> where several defect bits are set on the same object.</description></item>
/// </list>
/// Values are compared using the field's <see cref="FilterValueKind"/>
/// (integer fields numerically, everything else ordinal case-insensitive),
/// which keeps parity with the Vieweb §3.1.2 operator table.
/// </remarks>
public interface IFilterRowValues
{
    /// <summary>
    /// Returns the row's token(s) for <paramref name="field"/> as
    /// invariant-culture strings, or an empty collection when the row
    /// has no value for that field. Implementations should return a
    /// stable, allocation-light collection (an empty array or a cached
    /// single-element array for scalar fields).
    /// </summary>
    IReadOnlyCollection<string> GetValues(FilterField field);
}

/// <summary>
/// Evaluates <see cref="FilterRequest"/> / <see cref="FilterClause"/>
/// predicates against a materialised row in memory. Nieweb applies the
/// generic operator filters <em>after</em> streaming rows from the AOI
/// Superviseur DB (which already applies the time window plus the
/// machine / product first-class filters in SQL), so this evaluator
/// never touches the live line and carries no SQL-injection surface.
/// </summary>
/// <remarks>
/// All clauses in a request are AND-joined, matching Vieweb (which never
/// allowed arbitrary boolean composition). Operator semantics reproduce
/// the Vieweb 1.6.2 user guide §3.1.2 table:
/// <list type="bullet">
///   <item><description><see cref="FilterOperator.Equal"/> / <see cref="FilterOperator.Different"/> — exact match (ordinal-ignore-case for strings).</description></item>
///   <item><description><see cref="FilterOperator.In"/> / <see cref="FilterOperator.NotIn"/> — set membership.</description></item>
///   <item><description><see cref="FilterOperator.Like"/> / <see cref="FilterOperator.NotLike"/> — case-insensitive <em>substring</em> ("contains") match, per Vieweb ("the element to look for contains the criteria entered").</description></item>
///   <item><description><see cref="FilterOperator.Between"/> / <see cref="FilterOperator.NotBetween"/> — inclusive range.</description></item>
///   <item><description><see cref="FilterOperator.LessThanOrEqual"/> / <see cref="FilterOperator.GreaterThanOrEqual"/> — ordered comparison.</description></item>
/// </list>
/// Negative operators are the strict logical negation of their positive
/// counterpart, so a row with no value for the field <em>satisfies</em>
/// the negative predicate (e.g. "part number NotLike 'ABC'" keeps rows
/// with no part number). Callers are expected to have validated the
/// request with <see cref="FilterValidator"/> first; an invalid clause
/// (bad arity, disallowed operator) is treated as non-matching rather
/// than throwing, so a single malformed clause never crashes a report.
/// </remarks>
public static class FilterEvaluator
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="row"/> satisfies every
    /// clause in <paramref name="request"/> (AND). An empty / default
    /// request matches every row.
    /// </summary>
    public static bool Matches(FilterRequest request, IFilterRowValues row)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(row);

        if (request.Clauses.IsDefaultOrEmpty)
        {
            return true;
        }

        foreach (var clause in request.Clauses)
        {
            if (!Matches(clause, row))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="row"/> satisfies the
    /// single <paramref name="clause"/>.
    /// </summary>
    public static bool Matches(FilterClause clause, IFilterRowValues row)
    {
        ArgumentNullException.ThrowIfNull(clause);
        ArgumentNullException.ThrowIfNull(row);

        var values = clause.Values.IsDefault ? [] : clause.Values;
        if (!HasCorrectArity(clause.Operator, values.Length))
        {
            // Structurally invalid clause — never matches (fail closed).
            return false;
        }

        var kind = FilterFieldMetadata.GetValueKind(clause.Field);
        var tokens = row.GetValues(clause.Field);

        return clause.Operator switch
        {
            FilterOperator.Equal => AnyToken(tokens, t => ScalarEquals(t, values[0], kind)),
            FilterOperator.Different => !AnyToken(tokens, t => ScalarEquals(t, values[0], kind)),
            FilterOperator.In => AnyToken(tokens, t => ContainsValue(values, t, kind)),
            FilterOperator.NotIn => !AnyToken(tokens, t => ContainsValue(values, t, kind)),
            FilterOperator.Like => AnyToken(tokens, t => LikeMatch(t, values[0])),
            FilterOperator.NotLike => !AnyToken(tokens, t => LikeMatch(t, values[0])),
            FilterOperator.Between => AnyToken(tokens, t => InRange(t, values[0], values[1], kind)),
            FilterOperator.NotBetween => !AnyToken(tokens, t => InRange(t, values[0], values[1], kind)),
            FilterOperator.LessThanOrEqual => AnyToken(tokens, t => Compare(t, values[0], kind) is int c && c <= 0),
            FilterOperator.GreaterThanOrEqual => AnyToken(tokens, t => Compare(t, values[0], kind) is int c && c >= 0),
            _ => false,
        };
    }

    private static bool HasCorrectArity(FilterOperator op, int count)
    {
        if (!Enum.IsDefined(op))
        {
            return false;
        }
        return FilterOperatorMetadata.GetArity(op) switch
        {
            FilterOperatorArity.Single => count == 1,
            FilterOperatorArity.Range => count == 2,
            FilterOperatorArity.List => count >= 1,
            _ => false,
        };
    }

    private static bool AnyToken(IReadOnlyCollection<string> tokens, Func<string, bool> predicate)
    {
        if (tokens.Count == 0)
        {
            return false;
        }
        foreach (var token in tokens)
        {
            if (token is not null && predicate(token))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsValue(
        System.Collections.Immutable.ImmutableArray<string> values,
        string token,
        FilterValueKind kind)
    {
        foreach (var v in values)
        {
            if (ScalarEquals(token, v, kind))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ScalarEquals(string token, string value, FilterValueKind kind)
    {
        if (kind == FilterValueKind.Integer)
        {
            return TryLong(token, out var a) && TryLong(value, out var b) && a == b;
        }
        return string.Equals(token, value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LikeMatch(string token, string value)
        => token.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static bool InRange(string token, string min, string max, FilterValueKind kind)
        => Compare(token, min, kind) is int lo && lo >= 0
           && Compare(token, max, kind) is int hi && hi <= 0;

    /// <summary>
    /// Orders <paramref name="token"/> against <paramref name="value"/>
    /// using the field kind: integer fields compare numerically, all
    /// others compare ordinal-ignore-case (alphanumeric — matching how
    /// Vieweb sorts bar codes / id codes). Returns <c>null</c> when the
    /// token is not comparable (e.g. a non-numeric token on an integer
    /// field), and callers fail the ordered predicate closed.
    /// </summary>
    private static int? Compare(string token, string value, FilterValueKind kind)
    {
        if (kind == FilterValueKind.Integer)
        {
            if (TryLong(token, out var a) && TryLong(value, out var b))
            {
                return a.CompareTo(b);
            }
            // Non-numeric token can never satisfy an ordered comparison.
            return null;
        }
        return string.Compare(token, value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryLong(string raw, out long value)
        => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
