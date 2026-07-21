namespace Nieweb.Filters;

/// <summary>
/// Scalar value kind a filter clause can carry. Determines which
/// operators are applicable and how the value is parsed from JSON.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Discriminator names intentionally mirror their runtime type.")]
public enum FilterValueKind
{
    /// <summary>Free-form UTF-16 string. Accepts <see cref="FilterOperator.Like"/>.</summary>
    String = 0,

    /// <summary>Signed 64-bit integer.</summary>
    Integer = 1,

    /// <summary>Invariant-culture decimal.</summary>
    Decimal = 2,

    /// <summary>UTC date-time (ISO-8601 string on the wire).</summary>
    DateTimeUtc = 3,

    /// <summary>Boolean (<c>true</c> / <c>false</c>). Only supports <see cref="FilterOperator.Equal"/>.</summary>
    Boolean = 4,
}
