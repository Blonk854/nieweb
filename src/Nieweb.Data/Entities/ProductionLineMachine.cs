namespace Nieweb.Data.Entities;

/// <summary>
/// One AOI / SPI machine assigned to a <see cref="ProductionLine"/>.
/// Mirrors how the legacy Vieweb <c>machine.PRODUCTION_LINE_ID</c>
/// nullable FK worked: a physical machine may belong to at most one
/// production line at a time (enforced by a unique index on
/// <c>(SourceId, MachineId)</c>).
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="SourceId"/> + <see cref="MachineId"/> pair is the
/// stable identity: it locates the machine in one of Nieweb's read-only
/// Superviseur databases (<c>"postreflow"</c> / <c>"prereflow"</c> /
/// <c>"fake"</c>). <see cref="MachineName"/> is stored as a snapshot
/// taken when the machine was added so the admin UI can render a
/// meaningful label even when the source is offline.
/// </para>
/// <para>
/// <see cref="Category"/> is a free-form label copied from the Vieweb
/// design (e.g. <c>"AOI"</c>, <c>"SPI"</c>, <c>"Placement"</c>). It is
/// not enum-typed on purpose — sites use their own vocabulary.
/// </para>
/// </remarks>
public sealed class ProductionLineMachine
{
    /// <summary>Auto-generated surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>FK to the owning <see cref="ProductionLine"/>.</summary>
    public int ProductionLineId { get; set; }

    /// <summary>Navigation to the owning line.</summary>
    public ProductionLine ProductionLine { get; set; } = null!;

    /// <summary>
    /// Stable id of the Nieweb AOI data source that hosts the machine
    /// (e.g. <c>"postreflow"</c>, <c>"prereflow"</c>, <c>"fake"</c>).
    /// Matches <c>SourceDescriptor.SourceId</c>.
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// Superviseur <c>MACHINE.MACHINE_ID</c> from the source database.
    /// Combined with <see cref="SourceId"/> this is the stable identity
    /// of the physical machine.
    /// </summary>
    public int MachineId { get; set; }

    /// <summary>
    /// Snapshot of <c>MACHINE.NAME</c> at the time the machine was
    /// added, so the admin UI can render a label even when the source
    /// is offline. Not authoritative — the live source wins if the
    /// admin re-syncs.
    /// </summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>
    /// Free-form category matching Vieweb's <c>machine.CATEGORY</c>
    /// column (e.g. <c>"AOI"</c>, <c>"SPI"</c>, <c>"Placement"</c>).
    /// May be <c>null</c>.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Manual sort key for machines within a line (Vieweb
    /// <c>machine.MACHINE_ORDER</c>). Ties break on
    /// <see cref="MachineName"/> ascending.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>UTC timestamp of the assignment insert.</summary>
    public DateTime CreatedUtc { get; set; }
}
