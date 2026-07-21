using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Nieweb.Filters;

/// <summary>
/// Static metadata for <see cref="FilterOperator"/> values (arity + value
/// kinds it accepts) and for <see cref="FilterField"/> values (which
/// operators are allowed per field — reproducing the Vieweb §3.1.2
/// operator table verbatim).
/// </summary>
public static class FilterOperatorMetadata
{
    /// <summary>
    /// Returns the fixed value arity for <paramref name="op"/>. Callers
    /// use this to validate incoming <see cref="FilterClause.Values"/>
    /// lengths.
    /// </summary>
    public static FilterOperatorArity GetArity(FilterOperator op) => op switch
    {
        FilterOperator.Equal or
        FilterOperator.Different or
        FilterOperator.Like or
        FilterOperator.NotLike or
        FilterOperator.LessThanOrEqual or
        FilterOperator.GreaterThanOrEqual => FilterOperatorArity.Single,

        FilterOperator.In or
        FilterOperator.NotIn => FilterOperatorArity.List,

        FilterOperator.Between or
        FilterOperator.NotBetween => FilterOperatorArity.Range,

        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown filter operator."),
    };

    /// <summary>
    /// Returns <c>true</c> if <paramref name="op"/> is compatible with
    /// values of kind <paramref name="valueKind"/>. Rules:
    /// <list type="bullet">
    ///   <item><description><see cref="FilterOperator.Like"/> / <see cref="FilterOperator.NotLike"/> require <see cref="FilterValueKind.String"/>.</description></item>
    ///   <item><description>Ordering operators (<see cref="FilterOperator.Between"/>, <see cref="FilterOperator.NotBetween"/>, <see cref="FilterOperator.LessThanOrEqual"/>, <see cref="FilterOperator.GreaterThanOrEqual"/>) require an orderable kind (integer / decimal / date-time / string).</description></item>
    ///   <item><description><see cref="FilterValueKind.Boolean"/> only accepts <see cref="FilterOperator.Equal"/> / <see cref="FilterOperator.Different"/>.</description></item>
    /// </list>
    /// </summary>
    public static bool SupportsValueKind(FilterOperator op, FilterValueKind valueKind)
    {
        if (op is FilterOperator.Like or FilterOperator.NotLike)
        {
            return valueKind == FilterValueKind.String;
        }
        if (valueKind == FilterValueKind.Boolean)
        {
            return op is FilterOperator.Equal or FilterOperator.Different;
        }
        return true;
    }
}

/// <summary>
/// Vieweb §3.1.2 operator table row → allowed <see cref="FilterOperator"/>
/// set. The lists are the exact ones printed in the Vieweb user guide
/// (Vieweb 1.0 rev 01, p. 3-3). Reports call
/// <see cref="GetAllowedOperators"/> when they need to render a
/// per-field operator picker.
/// </summary>
public static class FilterFieldMetadata
{
    // Materialised once at start-up; frozen for fast lookup and to
    // avoid accidental mutation from consumer code.
    private static readonly FrozenDictionary<FilterField, ImmutableHashSet<FilterOperator>> AllowedByField
        = BuildAllowed().ToFrozenDictionary();

    /// <summary>
    /// Value kind expected by <paramref name="field"/>. Used by the
    /// validator to reject clauses that mix e.g. a bool value against
    /// a string field.
    /// </summary>
    public static FilterValueKind GetValueKind(FilterField field) => field switch
    {
        FilterField.BoardNumber => FilterValueKind.Integer,
        // Bar codes and ID codes are stored as text on the AOI DB
        // (VARCHAR / NVARCHAR) even when they look numeric, so LIKE /
        // NOT LIKE remain useful search operators — matching Vieweb.
        FilterField.PanelBarcode => FilterValueKind.String,
        FilterField.BoardIdCode => FilterValueKind.String,
        FilterField.PanelStatus => FilterValueKind.Integer,
        FilterField.BoardStatus => FilterValueKind.Integer,
        _ => FilterValueKind.String,
    };

    /// <summary>
    /// Returns the set of operators Vieweb allowed on
    /// <paramref name="field"/>. Reports must intersect this with
    /// their own capability set — some sources cannot honour every
    /// operator (e.g. pre-reflow AOI has no <c>PIN_MEASURE</c>
    /// backing for reference-designator queries).
    /// </summary>
    public static ImmutableHashSet<FilterOperator> GetAllowedOperators(FilterField field)
    {
        return AllowedByField.TryGetValue(field, out var set)
            ? set
            : throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown filter field.");
    }

    /// <summary>
    /// Convenience predicate: is <paramref name="op"/> allowed on
    /// <paramref name="field"/>?
    /// </summary>
    public static bool IsAllowed(FilterField field, FilterOperator op)
        => GetAllowedOperators(field).Contains(op);

    private static Dictionary<FilterField, ImmutableHashSet<FilterOperator>> BuildAllowed()
    {
        // Verbatim from Vieweb 1.6.2 user guide §3.1.2 (p. 3-3).
        // A cell marked "X" in the printed table appears in the set.
        var stringSetOnly = ImmutableHashSet.Create(
            FilterOperator.Equal,
            FilterOperator.Different,
            FilterOperator.In,
            FilterOperator.NotIn,
            FilterOperator.Like,
            FilterOperator.NotLike);

        var orderedSet = ImmutableHashSet.Create(
            FilterOperator.Equal,
            FilterOperator.Different,
            FilterOperator.In,
            FilterOperator.NotIn,
            FilterOperator.Between,
            FilterOperator.NotBetween,
            FilterOperator.LessThanOrEqual,
            FilterOperator.GreaterThanOrEqual);

        var setMembership = ImmutableHashSet.Create(
            FilterOperator.Equal,
            FilterOperator.Different,
            FilterOperator.In,
            FilterOperator.NotIn);

        var fullTenColumn = ImmutableHashSet.Create(
            FilterOperator.Equal,
            FilterOperator.Different,
            FilterOperator.In,
            FilterOperator.NotIn,
            FilterOperator.Between,
            FilterOperator.NotBetween,
            FilterOperator.Like,
            FilterOperator.NotLike,
            FilterOperator.LessThanOrEqual,
            FilterOperator.GreaterThanOrEqual);

        var equalOnly = ImmutableHashSet.Create(FilterOperator.Equal);

        return new Dictionary<FilterField, ImmutableHashSet<FilterOperator>>
        {
            // 8 X's — ordered integer field.
            [FilterField.BoardNumber] = orderedSet,
            // 6 X's — enumerated string.
            [FilterField.PnpMachine] = stringSetOnly,
            [FilterField.PnpSubElement1] = stringSetOnly,
            [FilterField.PnpSubElement2] = stringSetOnly,
            [FilterField.PnpSubElement3] = stringSetOnly,
            [FilterField.PnpSubElement4] = stringSetOnly,
            [FilterField.PartNumber] = stringSetOnly,
            // 4 X's — set membership only (Vieweb never let users
            // Like-search these).
            [FilterField.InspectedObject] = setMembership,
            [FilterField.RepairStatus] = setMembership,
            [FilterField.Defect] = setMembership,
            // 6 X's each.
            [FilterField.Product] = stringSetOnly,
            [FilterField.Package] = stringSetOnly,
            [FilterField.RepairComment] = stringSetOnly,
            [FilterField.ReferenceDesignator] = stringSetOnly,
            [FilterField.AoiMachine] = stringSetOnly,
            // 10 X's — bar codes and ID codes admit every operator
            // including Between / <= / >= because operators sort them
            // alphanumerically.
            [FilterField.PanelBarcode] = fullTenColumn,
            [FilterField.BoardIdCode] = fullTenColumn,
            // 1 X — status filters are exact-match only.
            [FilterField.PanelStatus] = equalOnly,
            [FilterField.BoardStatus] = equalOnly,
        };
    }
}

/// <summary>How many values a <see cref="FilterOperator"/> takes.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "'Single' here means 'exactly one value', not the numeric type.")]
public enum FilterOperatorArity
{
    /// <summary>Exactly one value.</summary>
    Single = 0,

    /// <summary>One or more values.</summary>
    List = 1,

    /// <summary>Exactly two values (min, max).</summary>
    Range = 2,
}
