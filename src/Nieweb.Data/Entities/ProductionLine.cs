namespace Nieweb.Data.Entities;

/// <summary>
/// A named group of AOI / SPI machines that make up a physical SMT
/// production line. Ports the Vieweb <c>productionline</c> table
/// (Vieweb §2.4.3) and is the anchor for the Process Capability
/// dashboard (docs/phase-2.md §7.4 <c>PC1</c>) and any future
/// line-scoped filter on the reporting SPA.
/// </summary>
/// <remarks>
/// <para>
/// A line has a stable <see cref="Name"/>; machines are attached via
/// <see cref="Machines"/> (a one-to-many <see cref="ProductionLineMachine"/>
/// collection with cascade delete). Deleting a line therefore removes
/// its machine assignments — the AOI Superviseur DBs are never
/// touched, and every machine that used to be on the line simply
/// becomes "unassigned" from Nieweb's point of view.
/// </para>
/// <para>
/// <see cref="DisplayOrder"/> lets an admin control how lines appear
/// in the UI (top-to-bottom in the admin table, left-to-right in the
/// PC1 grid). Ties are broken by <see cref="Name"/> ascending.
/// </para>
/// </remarks>
public sealed class ProductionLine
{
    /// <summary>Auto-generated surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Human-readable line name (e.g. <c>"Line 1"</c>). Unique across
    /// the tenant so admins cannot accidentally create two "Line 1"
    /// entries.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Manual sort key used by the admin table and the PC1 dashboard.
    /// Ties break on <see cref="Name"/> ascending.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>UTC timestamp of first insert.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of the last successful update.</summary>
    public DateTime LastModifiedUtc { get; set; }

    /// <summary>
    /// Machines assigned to the line. Cascade-deleted with the line
    /// itself.
    /// </summary>
    public ICollection<ProductionLineMachine> Machines { get; set; }
        = new List<ProductionLineMachine>();
}
