namespace Nieweb.Data.Entities;

/// <summary>
/// A single typed key/value tuning knob for Nieweb. Backs the "Application
/// parameters" admin page (Vieweb §2.4.2) plus system-wide switches like
/// the global batch-enabled flag (parity with Vieweb <c>batchIsOn</c>).
/// </summary>
/// <remarks>
/// <para>
/// Values are always stored as invariant-culture strings; consumers parse
/// them against <see cref="ValueType"/>. This keeps the schema stable
/// (one table, one row per knob) at the cost of a tiny amount of parsing
/// on read. That trade-off matches the legacy Vieweb design and keeps
/// admin CRUD trivially uniform.
/// </para>
/// <para>
/// Rows with <see cref="IsSystem"/><c> = true</c> are seeded on first
/// boot and cannot be deleted (they may still be updated). Rows with
/// <see cref="IsSystem"/><c> = false</c> are user-defined and may be
/// deleted freely.
/// </para>
/// </remarks>
public sealed class AppParameter
{
    /// <summary>
    /// Stable dot-separated key (e.g. <c>msa.gr_r</c>,
    /// <c>tolerance.paste.itx</c>, <c>batch.enabled</c>). Case-sensitive.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Discriminator that tells consumers how to parse
    /// <see cref="Value"/>. One of the constants declared on
    /// <see cref="AppParameterValueTypes"/>.
    /// </summary>
    public string ValueType { get; set; } = AppParameterValueTypes.String;

    /// <summary>
    /// The value in invariant-culture text form (e.g. <c>"4.33"</c>,
    /// <c>"true"</c>, <c>"1440"</c>).
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Optional human-readable description shown in the admin UI. Not
    /// translated - the plan is for the admin catalogue to key on
    /// <see cref="Key"/> for localisation lookups.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// <c>true</c> for seeded rows that Nieweb depends on and refuses to
    /// let admins delete (they may still be updated). <c>false</c> for
    /// ad-hoc rows added later.
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>UTC timestamp of first insert.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of the last successful update.</summary>
    public DateTime LastModifiedUtc { get; set; }
}

/// <summary>
/// String constants used in <see cref="AppParameter.ValueType"/>. Callers
/// should compare against these rather than typing the literals.
/// </summary>
/// <remarks>
/// The names <c>Decimal</c> / <c>Int</c> / <c>String</c> deliberately
/// mirror their runtime discriminator values; the CA1720 warning is
/// suppressed because these are dictionary keys, not type wrappers.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Discriminator constants intentionally mirror their string values.")]
public static class AppParameterValueTypes
{
    /// <summary>Decimal / floating-point value (invariant-culture text).</summary>
    public const string Decimal = "decimal";

    /// <summary>Signed integer value.</summary>
    public const string Int = "int";

    /// <summary>Boolean value (<c>"true"</c> / <c>"false"</c>, case-insensitive).</summary>
    public const string Bool = "bool";

    /// <summary>Free-form string value.</summary>
    public const string String = "string";
}
