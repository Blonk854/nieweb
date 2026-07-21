using Microsoft.Extensions.Logging;

namespace Nieweb.DataSources.Sql;

/// <summary>
/// Pre-reflow Mycronic AOI source — Vision3D CR4 Superviseur v4.3.1.
/// Backed by <c>HLYMSSQL1 / MEAOI</c>. Missing PIN, PIN_MEASURE, all *_HISTO
/// tables, and Barcode_Product; adds paste-print / stencil metrics on PANELS
/// and CARDS, plus real per-machine FEEDER data.
/// </summary>
public sealed class MeaoiSource : SqlServerAoiSourceBase
{
    public MeaoiSource(AoiSourceOptions options, ILogger<SqlServerAoiSourceBase>? logger = null)
        : base(options, logger)
    {
    }

    protected override string SourceTag => "prereflow";

    public override SourceDescriptor Descriptor { get; } = new(
        Id: "prereflow",
        DisplayName: "Pre-reflow AOI (MEAOI)",
        SchemaVersion: "4.3.1",
        Caps:
            Capabilities.PastePrintMetrics |
            Capabilities.FeederAnalytics);

    // ---- Reference data queries (fully working) ----------------------------

    public override Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct)
    {
        // MACHINE columns are identical between v4.3.1 and v5.0.
        const string Sql = """
            SELECT Machine_Id, Machine_Type, Machine_Name, Machine_Type_Name
            FROM   dbo.MACHINE WITH (NOLOCK)
            ORDER  BY Machine_Id;
            """;

        return ExecuteListAsync(
            Sql,
            bindParameters: null,
            map: static r => new Machine(
                MachineId: r.GetInt32(0),
                MachineType: r.GetInt32(1),
                MachineName: r.GetString(2),
                // Machine_Type_Name is declared nullable in the schema (verified
                // against both live DBs). Never call GetString on a nullable
                // column without an IsDBNull guard - it throws.
                MachineTypeName: r.IsDBNull(3) ? null : r.GetString(3)),
            ct);
    }

    public override Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct)
    {
        const string Sql = """
            SELECT Product_Id, Product_Name, Revision, Description
            FROM   dbo.PRODUCT WITH (NOLOCK)
            ORDER  BY Product_Id;
            """;

        return ExecuteListAsync(
            Sql,
            bindParameters: null,
            map: static r => new Product(
                ProductId: r.GetInt32(0),
                ProductName: r.IsDBNull(1) ? null : r.GetString(1),
                Revision: r.IsDBNull(2) ? null : r.GetString(2),
                Description: r.IsDBNull(3) ? null : r.GetString(3)),
            ct);
    }

    public override Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct)
    {
        // v4.3.1 lacks VARIANT_NAME; the DTO field is populated with null.
        const string Sql = """
            SELECT Recipe_Id, File_Name, Product_Id, Author,
                   Inspected_Side_Nb, Inspected_Side_Name,
                   Customer, Production_Step
            FROM   dbo.RECIPE WITH (NOLOCK)
            ORDER  BY Recipe_Id;
            """;

        return ExecuteListAsync(
            Sql,
            bindParameters: null,
            map: static r => new Recipe(
                RecipeId: r.GetInt32(0),
                FileName: r.IsDBNull(1) ? null : r.GetString(1),
                ProductId: r.GetInt32(2),
                Author: r.IsDBNull(3) ? null : r.GetString(3),
                InspectedSideNb: r.GetInt32(4),
                InspectedSideName: r.IsDBNull(5) ? null : r.GetString(5),
                Customer: r.IsDBNull(6) ? null : r.GetString(6),
                ProductionStep: r.IsDBNull(7) ? null : r.GetString(7),
                VariantName: null),
            ct);
    }

    // ---- Fact-table queries (skeleton - to be implemented per-report) ------

    public override Task<Page<CardRow, CardCursor>> QueryCardsAsync(CardQuery query, CancellationToken ct)
    {
        ValidateWindow(query);
        throw new NotImplementedException("QueryCardsAsync will be implemented alongside the first CARDS-consuming report.");
    }

    /// <summary>
    /// v4.3.1 TESTED_OBJECT lacks <c>Error_Table_AR</c> — the shared
    /// query builder falls back to <c>Error_Table</c> so
    /// <see cref="TestedObjectRow.ErrorTableAr"/> mirrors
    /// <see cref="TestedObjectRow.ErrorTable"/> (contract:
    /// "missing-AR means no review has occurred yet").
    /// </summary>
    protected override bool HasTestedObjectErrorTableAr => false;

    // QueryTestedObjectsAsync uses the shared base-class implementation
    // (BuildTestedObjectsQuery + MapTestedObjectRow) — see the
    // HasTestedObjectErrorTableAr override above for the v4.3.1 quirk.
}
