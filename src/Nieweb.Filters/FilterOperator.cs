namespace Nieweb.Filters;

/// <summary>
/// Comparison operators available on report filters. Reproduces the
/// Vieweb 1.6.2 operator table from Vieweb §3.1.2 (Real time view →
/// Filters) verbatim so saved-view payloads round-trip 1:1 between
/// products.
/// </summary>
/// <remarks>
/// <para>
/// Enum name is serialised in saved-view JSON (via
/// <c>JsonStringEnumConverter</c>) so renames are breaking changes;
/// underlying int values are only consumed by internal switch
/// statements.
/// </para>
/// <para>
/// See <see cref="FilterOperatorMetadata"/> for arity (single value,
/// list, or [min,max] pair) and the value kinds each operator accepts.
/// </para>
/// </remarks>
public enum FilterOperator
{
    /// <summary>Vieweb "Equal" — <c>field = value</c>.</summary>
    Equal = 0,

    /// <summary>Vieweb "Different" — <c>field &lt;&gt; value</c>.</summary>
    Different = 1,

    /// <summary>Vieweb "In" — <c>field IN (value1, value2, …)</c>.</summary>
    In = 2,

    /// <summary>Vieweb "Not In" — <c>field NOT IN (value1, value2, …)</c>.</summary>
    NotIn = 3,

    /// <summary>Vieweb "Between" — <c>field BETWEEN min AND max</c> (inclusive).</summary>
    Between = 4,

    /// <summary>Vieweb "Not Between" — <c>NOT (field BETWEEN min AND max)</c>.</summary>
    NotBetween = 5,

    /// <summary>
    /// Vieweb "Like" — case-insensitive substring match. Vieweb
    /// §3.1.2: "Like operator means that the element to look for
    /// contains the criteria entered."
    /// </summary>
    Like = 6,

    /// <summary>Vieweb "Not Like" — logical negation of <see cref="Like"/>.</summary>
    NotLike = 7,

    /// <summary>Vieweb "&lt;=" — <c>field &lt;= value</c>.</summary>
    LessThanOrEqual = 8,

    /// <summary>Vieweb "&gt;=" — <c>field &gt;= value</c>.</summary>
    GreaterThanOrEqual = 9,
}
