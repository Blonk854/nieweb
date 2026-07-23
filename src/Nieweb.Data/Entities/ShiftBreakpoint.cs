namespace Nieweb.Data.Entities;

/// <summary>
/// One entry in the site-wide shift cycle. Ports Vieweb's
/// <c>shiftunit</c> table (Vieweb §2.4.4): a list of start times that
/// partition a 24-hour day into consecutive shifts. Consumed by
/// <c>Nieweb.Reports.Common.ShiftDefinition</c> when building
/// per-shift buckets for charts and dashboards.
/// </summary>
/// <remarks>
/// <para>
/// Each row is one <em>breakpoint</em> — the start of a shift. The
/// site-wide cycle is the ordered set of all rows sorted by
/// <c>(Hour, Minute)</c> ascending. Vieweb allowed the admin to
/// tweak the cycle by adding / removing breakpoints one at a time;
/// Nieweb exposes a single "replace-all" endpoint because a shift
/// cycle only makes sense as an atomic unit.
/// </para>
/// <para>
/// <see cref="Label"/> is optional; when it is null the report layer
/// defaults to <c>"Shift 1"</c>, <c>"Shift 2"</c>, … in start-time
/// order.
/// </para>
/// </remarks>
public sealed class ShiftBreakpoint
{
    /// <summary>Auto-generated surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>Hour of day, 0–23.</summary>
    public int Hour { get; set; }

    /// <summary>Minute of hour, 0–59.</summary>
    public int Minute { get; set; }

    /// <summary>
    /// Optional shift label (e.g. <c>"Morning"</c>). Null means "use
    /// the default <c>Shift N</c> label".
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Manual sort key. Not authoritative for ordering (the report
    /// layer sorts by <c>(Hour, Minute)</c>); kept for parity with
    /// Vieweb's <c>SHIFT_UNIT_ORDER</c> column and to give admins a
    /// stable UI order even before they save.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>UTC timestamp of first insert.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of the last successful update.</summary>
    public DateTime LastModifiedUtc { get; set; }
}
