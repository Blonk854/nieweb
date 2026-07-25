using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Nieweb.DataSources.Sql;

/// <summary>
/// Post-reflow Mycronic AOI source — Vision20 / Vision3D CR5 Superviseur v5.0.
/// Backed by <c>HLYMSSQL2 / HLYAOI2024</c> (was <c>HLYAOI</c> until 2026-07;
/// same server + service account, DB renamed to <c>HLYAOI2024</c> when the
/// production line switched to the live catalogue).
/// </summary>
public sealed class HlyaoiSource : SqlServerAoiSourceBase, IPinLevelSource
{
    public HlyaoiSource(AoiSourceOptions options, ILogger<SqlServerAoiSourceBase>? logger = null)
        : base(options, logger)
    {
    }

    protected override string SourceTag => "postreflow";

    public override SourceDescriptor Descriptor { get; } = new(
        Id: "postreflow",
        DisplayName: "Post-reflow AOI (HLYAOI2024)",
        SchemaVersion: "5.0",
        Caps:
            Capabilities.PinLevel |
            Capabilities.ReviewAudit |
            Capabilities.IsLastInspectionFilter |
            Capabilities.MachineEfficiencyTiming |
            Capabilities.PrecomputedCardDpmo |
            Capabilities.BarcodeProductView |
            Capabilities.RecipeVariants);

    // ---- Reference data queries (fully working) ----------------------------

    public override Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct)
    {
        // Machine_Type is a canonical enum in the Superviseur schema
        // (see 'Database fields and constants (Vision3D CR4).pdf' §5.10):
        //   1 = Vision AOI  (Vision3D / Vision20 inspection machines)
        //   2 = Review station
        // We only ever want to expose Vision AOI machines: review
        // stations do not produce PANELS/CARDS rows, so surfacing them
        // in the filter dropdown, in the admin Production Lines picker,
        // or in the report display-name lookup is confusing and never
        // useful.
        const string Sql = """
            SELECT Machine_Id, Machine_Type, Machine_Name, Machine_Type_Name
            FROM   dbo.MACHINE WITH (NOLOCK)
            WHERE  Machine_Type = 1
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

    public override Task<IReadOnlyList<ReviewOperator>> ListOperatorsAsync(CancellationToken ct)
    {
        // OPERATOR is a small table (a few hundred rows at most on
        // this DB). Columns Operator_Id + Operator_Name are identical
        // across v4.3.1 and v5.0 schemas.
        const string Sql = """
            SELECT Operator_Id, Operator_Name
            FROM   dbo.OPERATOR WITH (NOLOCK)
            ORDER  BY Operator_Id;
            """;

        return ExecuteListAsync(
            Sql,
            bindParameters: null,
            map: static r => new ReviewOperator(
                OperatorId: r.GetInt32(0),
                // Operator_Name is nominally NOT NULL in the schema,
                // but historical rows on some sites have carried NULL
                // through legacy loaders. Guard defensively.
                OperatorName: r.IsDBNull(1) ? string.Empty : r.GetString(1)),
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
        // VARIANT_NAME available only on v5.0 (post-reflow).
        const string Sql = """
            SELECT Recipe_Id, File_Name, Product_Id, Author,
                   Inspected_Side_Nb, Inspected_Side_Name,
                   Customer, Production_Step, VARIANT_NAME
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
                VariantName: r.IsDBNull(8) ? null : r.GetString(8)),
            ct);
    }

    // ---- Fact-table queries -----------------------------------------------
    // QueryPanelsAsync, QueryCardsAsync, and QueryTestedObjectsAsync all use
    // the shared base-class implementations (BuildPanelsQuery / BuildCardsQuery
    // / BuildTestedObjectsQuery and their mappers). v5.0 exposes
    // Error_Table_AR and IS_LAST_INSPECTION, so no capability overrides are
    // needed here.

    // ---- IPinLevelSource (TC1) --------------------------------------------
    // Post-reflow only: v5.0 ships the PIN table (surrogate Pin_Id, FK
    // Tested_Object_Id). Pre-reflow v4.3.1 lacks PIN entirely, so MeaoiSource
    // does not implement this interface. Column names verified against
    // tools/db/out/postreflow/10_columns_PIN.csv.

    public async Task<IReadOnlyList<PinRow>> ListPinsForObjectAsync(
        long testedObjectId, CancellationToken ct)
    {
        const string Sql = """
            SELECT
              Pin_Id, Tested_Object_Id, Component_Side, Pin_Index_On_Side,
              IPC_Pin_Nb, Error_Table, Error_Table_AR, Review_Sanction
            FROM dbo.PIN WITH (NOLOCK)
            WHERE Tested_Object_Id = @testedObjectId
            ORDER BY Component_Side, Pin_Index_On_Side;
            """;

        return await ExecuteListAsync(
            Sql,
            bindParameters: p => p.Add(new SqlParameter("@testedObjectId", SqlDbType.BigInt) { Value = testedObjectId }),
            map: static r => new PinRow(
                PinId: r.GetInt64(0),
                TestedObjectId: r.GetInt64(1),
                // Component_Side is tinyint; GetByte returns byte.
                ComponentSide: r.GetByte(2),
                // Pin_Index_On_Side is smallint.
                PinIndexOnSide: r.GetInt16(3),
                // IPC_Pin_Nb is nullable smallint.
                IpcPinNb: r.IsDBNull(4) ? null : r.GetInt16(4),
                // Error_Table is int; widen to long to match DTO.
                ErrorTable: r.GetInt32(5),
                // Error_Table_AR is bigint.
                ErrorTableAr: r.GetInt64(6),
                // Review_Sanction is tinyint.
                ReviewSanction: r.GetByte(7)),
            ct).ConfigureAwait(false);
    }
}
