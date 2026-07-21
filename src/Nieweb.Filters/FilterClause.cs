using System.Collections.Immutable;

namespace Nieweb.Filters;

/// <summary>
/// A single predicate to apply against a report source. The value of
/// <see cref="Values"/> depends on <see cref="Operator"/>:
/// <list type="bullet">
///   <item><description><see cref="FilterOperatorArity.Single"/>: exactly one entry.</description></item>
///   <item><description><see cref="FilterOperatorArity.List"/>: one or more entries.</description></item>
///   <item><description><see cref="FilterOperatorArity.Range"/>: exactly two entries — <c>[min, max]</c>.</description></item>
/// </list>
/// The typed value string is parsed against the field's
/// <see cref="FilterValueKind"/> by <see cref="FilterValidator"/>.
/// </summary>
/// <param name="Field">The report field being filtered.</param>
/// <param name="Operator">Comparison operator (Vieweb §3.1.2).</param>
/// <param name="Values">
/// Value strings encoded in invariant culture. Kept as strings on the
/// wire so the DTO shape is stable regardless of the backing SQL type.
/// </param>
public sealed record FilterClause(
    FilterField Field,
    FilterOperator Operator,
    ImmutableArray<string> Values);

/// <summary>
/// A full filter payload sent from the SPA to a report endpoint. All
/// clauses are AND-joined — matching Vieweb, which never allowed
/// arbitrary boolean composition ("Combining multiple filters can
/// alter the report response time").
/// </summary>
/// <param name="Clauses">Ordered list of predicates, all AND-joined.</param>
public sealed record FilterRequest(ImmutableArray<FilterClause> Clauses)
{
    /// <summary>An empty filter request (matches every row).</summary>
    public static FilterRequest Empty { get; } = new(ImmutableArray<FilterClause>.Empty);
}
