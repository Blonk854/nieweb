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

    /// <summary>
    /// When <c>true</c>, restricts the stream to rows that can influence
    /// skip classification — those with the <c>Error_Table</c> "object
    /// missing" bit set or a non-empty <c>Repair_Button_Comment</c>.
    /// Rows excluded by this predicate contribute nothing to
    /// <c>SkipInputsIndex</c> (they are neither "missing" nor a manual
    /// skip), so the resulting index is byte-identical while the wire
    /// volume collapses on schemas whose <c>TESTED_OBJECT</c> is not
    /// physically defect-only (the pre-reflow v4.3.1 DB). Defaults to
    /// <c>false</c> so every other caller keeps the full projection.
    /// </summary>
    public bool SkipInputsOnly { get; init; }

    /// <summary>
    /// When <c>true</c>, restricts the stream to rows carrying at least one
    /// defect bit (<c>Error_Table &lt;&gt; 0 OR Error_Table_AR &lt;&gt; 0</c>).
    /// <para>
    /// This is <b>exact-parity</b> for defect-counting callers: a row with no
    /// bits set popcounts to zero in every numerator flavour (AOI, Real,
    /// Dummy), so pruning it cannot change a defect total. What it does change
    /// is wire volume — on schemas whose <c>TESTED_OBJECT</c> is not physically
    /// defect-only (the pre-reflow v4.3.1 DB emits one row per tested object,
    /// not one per defect) the pruned stream is orders of magnitude smaller.
    /// </para>
    /// <para>
    /// Do <b>not</b> set this when the caller needs a count of tested objects —
    /// the pruned stream can no longer answer "how many objects were
    /// inspected". Opportunity denominators must come from
    /// <c>CARDS.Nb_Of_Tests_On_Comp</c> / <c>Nb_Of_Tests_On_Pads</c>
    /// regardless, never from a <c>TESTED_OBJECT</c> row count.
    /// </para>
    /// Defaults to <c>false</c> so every existing caller keeps the full stream.
    /// </summary>
    public bool DefectsOnly { get; init; }
}

/// <summary>Keyset paging cursor for PANELS (ordered by Panel_Numeric_Date, Panel_Id).</summary>
public readonly record struct PanelCursor(int LastPanelNumericDate, int LastPanelId);

/// <summary>Keyset paging cursor for CARDS (ordered by Panel_Id, Card_Id_On_Panel).</summary>
public readonly record struct CardCursor(int LastPanelId, int LastCardIdOnPanel);

/// <summary>Keyset paging cursor for TESTED_OBJECT (ordered by Panel_Id, Card_Id_On_Panel, Object_Id).</summary>
public readonly record struct TestedObjectCursor(int LastPanelId, int LastCardIdOnPanel, int LastObjectId);
