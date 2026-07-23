namespace Nieweb.Data.Entities;

/// <summary>
/// One tile (chart, table, MSA panel, comment, ...) inside a
/// <see cref="Report"/>. Ports Vieweb's <c>abstractentity</c> +
/// <c>templateEntity</c> chain (§3) with a deliberate simplification:
/// Vieweb modelled each concrete tile type as its own SQL table
/// (<c>templatetable</c>, <c>templategraph</c>, <c>templatemsa</c>,
/// <c>templateprocesscapability</c>, <c>templatecomment</c>, …).
/// Nieweb collapses that polymorphism into a single
/// <c>(TileType, ConfigJson)</c> pair: <see cref="TileType"/> names a
/// registry entry from <c>src/Nieweb.Web/canvas/tileCatalogue.ts</c>
/// and <see cref="ConfigJson"/> carries the tile-specific
/// configuration blob.
/// </summary>
/// <remarks>
/// <para>
/// This shape lets RC2's editor add a new tile type by extending the
/// TypeScript registry without a schema migration. The trade-off is
/// that server-side validation of <see cref="ConfigJson"/> is
/// deferred to whichever endpoint renders the tile (each report
/// endpoint already validates its own filter payload).
/// </para>
/// <para>
/// Cascade rule: an entity is bound to exactly one report — deleting
/// the parent report deletes the tile.
/// </para>
/// </remarks>
public sealed class ReportEntity
{
    /// <summary>Auto-generated surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>FK to the owning <see cref="Report"/>.</summary>
    public int ReportId { get; set; }

    /// <summary>Navigation to the owning report.</summary>
    public Report? Report { get; set; }

    /// <summary>
    /// Tile-catalogue key (e.g. <c>"panel-yield"</c>, <c>"pareto"</c>,
    /// <c>"deviation-chart"</c>, <c>"trend-chart"</c>,
    /// <c>"comment"</c>). Free-form string bounded at 100 chars;
    /// the SPA registry drives the render-time behaviour.
    /// </summary>
    public string TileType { get; set; } = string.Empty;

    /// <summary>
    /// Optional per-tile title (Vieweb <c>templateEntity.TITLE</c>).
    /// When <c>null</c> the SPA uses the tile-catalogue's default label.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Manual sort key within the parent report. Ports Vieweb
    /// <c>abstractentity.ENTITY_ORDER</c>.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Tile-specific configuration JSON (filter values, columns,
    /// numerator / opportunity choices, ...). Stored as opaque
    /// text; the rendering endpoint deserialises to its own DTO.
    /// </summary>
    public string ConfigJson { get; set; } = "{}";

    /// <summary>UTC timestamp of first insert.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of the last successful update.</summary>
    public DateTime LastModifiedUtc { get; set; }
}
