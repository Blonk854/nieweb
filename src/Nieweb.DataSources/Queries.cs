namespace Nieweb.DataSources;

/// <summary>
/// Base filter shared by every query type that hits large tables.
/// <see cref="Window"/> is mandatory (see <see cref="DateRange"/>);
/// machine / product filters are optional and additive.
/// </summary>
public abstract record BaseQuery
{
    public required DateRange Window { get; init; }

    /// <summary>Optional restriction to specific Machine_Id values.</summary>
    public IReadOnlyCollection<int>? MachineIds { get; init; }

    /// <summary>Optional restriction to specific Product_Id values.</summary>
    public IReadOnlyCollection<int>? ProductIds { get; init; }

    /// <summary>Optional restriction to specific Recipe_Id values.</summary>
    public IReadOnlyCollection<int>? RecipeIds { get; init; }
}

/// <summary>Query the PANELS table.</summary>
public sealed record PanelQuery : BaseQuery
{
    /// <summary>
    /// If <c>true</c> and the source supports <see cref="Capabilities.IsLastInspectionFilter"/>,
    /// only rows with IS_LAST_INSPECTION = 1 are returned (fixes Vieweb bug #12421).
    /// Sources that don't support the flag ignore this and return all rows.
    /// </summary>
    public bool OnlyLastInspection { get; init; } = true;

    /// <summary>Cursor for keyset paging. Null = first page.</summary>
    public PanelCursor? Cursor { get; init; }

    /// <summary>Rows to return in a page. Enforced upper bound at the adapter layer.</summary>
    public int PageSize { get; init; } = 1000;
}

/// <summary>Query the CARDS (subpanel) table.</summary>
public sealed record CardQuery : BaseQuery
{
    public CardCursor? Cursor { get; init; }
    public int PageSize { get; init; } = 1000;
}

/// <summary>Query the TESTED_OBJECT table.</summary>
public sealed record TestedObjectQuery : BaseQuery
{
    public TestedObjectCursor? Cursor { get; init; }
    public int PageSize { get; init; } = 1000;
}

/// <summary>Keyset paging cursor for PANELS (ordered by Panel_Numeric_Date, Panel_Id).</summary>
public readonly record struct PanelCursor(long LastPanelNumericDate, long LastPanelId);

/// <summary>Keyset paging cursor for CARDS (ordered by Panel_Id, Card_Id_On_Panel).</summary>
public readonly record struct CardCursor(long LastPanelId, int LastCardIdOnPanel);

/// <summary>Keyset paging cursor for TESTED_OBJECT (ordered by Panel_Id, Card_Id_On_Panel, Object_Id).</summary>
public readonly record struct TestedObjectCursor(long LastPanelId, int LastCardIdOnPanel, int LastObjectId);
