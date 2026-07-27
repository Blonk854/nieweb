using System.Data;
using Xunit;

namespace Nieweb.DataSources.Sql.Tests;

/// <summary>
/// Regression tests for the polymorphic <c>MapPanelRow</c> and
/// <c>MapTestedObjectRow</c> readers in <see cref="SqlServerAoiSourceBase"/>.
///
/// Motivation: the same logical column is not always the same SQL type
/// across the two live Superviseur DBs — for example
/// <c>TESTED_OBJECT.Tested_Object_Id</c> is <c>bigint</c> on the v5.0
/// post-reflow HLYAOI2024 schema but <c>int</c> on the v4.3.1 pre-reflow
/// MEAOI schema, and <c>Error_Table_AR</c> is <c>bigint</c> on v5.0 and
/// absent on v4.3.1 (the query builder repeats <c>Error_Table</c> in slot 5
/// as int). SqlDataReader's typed getters throw <see cref="System.InvalidCastException"/>
/// on any mismatch — one such bug hid in production until the live-DB
/// smoke test caught it. These tests drive the mappers with a
/// <see cref="DataTableReader"/> holding every column-type variant that
/// exists on either DB, so any future regression is caught before it
/// ships.
/// </summary>
public sealed class MapperColumnTypeTests
{
    // ---------- MapTestedObjectRow ----------

    [Fact]
    public void MapTestedObjectRow_PostReflowShape_ReadsBigIntColumns()
    {
        // v5.0: Tested_Object_Id=bigint, Error_Table=int, Error_Table_AR=bigint
        using var reader = BuildTestedObjectReader(
            testedObjectIdType: typeof(long),
            errorTableArType: typeof(long),
            // Value fits in int32 (matches current DTO/cursor shape).
            testedObjectId: 1_234_567L,
            errorTable: 0,
            errorTableAr: 0x1_0000_0002L);

        Assert.True(reader.Read());
        var row = SqlServerAoiSourceBase.MapTestedObjectRow(reader);

        Assert.Equal(1_234_567, row.ObjectId);
        Assert.Equal(4_294_967_298L, row.ErrorTableAr);
        Assert.Equal(0L, row.ErrorTable);
        Assert.Equal(1, row.Status);              // errorTableAr != 0
    }

    [Fact]
    public void MapTestedObjectRow_PreReflowShape_ReadsAllIntColumns()
    {
        // v4.3.1: Tested_Object_Id=int, Error_Table=int, Error_Table_AR
        // (column absent — builder repeats Error_Table in slot 5, still int).
        using var reader = BuildTestedObjectReader(
            testedObjectIdType: typeof(int),
            errorTableArType: typeof(int),
            testedObjectId: 12345,
            errorTable: 512,
            errorTableAr: 512);

        Assert.True(reader.Read());
        var row = SqlServerAoiSourceBase.MapTestedObjectRow(reader);

        Assert.Equal(12345, row.ObjectId);
        Assert.Equal(512L, row.ErrorTable);
        Assert.Equal(512L, row.ErrorTableAr);
        Assert.Equal(1, row.Status);
    }

    [Fact]
    public void MapTestedObjectRow_ZeroErrorTableAr_YieldsStatusZero()
    {
        using var reader = BuildTestedObjectReader(
            testedObjectIdType: typeof(long),
            errorTableArType: typeof(long),
            testedObjectId: 1L,
            errorTable: 0,
            errorTableAr: 0L);

        Assert.True(reader.Read());
        var row = SqlServerAoiSourceBase.MapTestedObjectRow(reader);

        Assert.Equal(0, row.Status);
    }

    [Fact]
    public void MapTestedObjectRow_DeltaColumns_RoundTripAsDoubles()
    {
        // CR2 Deviation chart depends on Delta_X..Delta_Surface being
        // materialised as System.Double from SQL Server FLOAT
        // (same on v5.0 and v4.3.1). Guards against a future
        // regression where a Delta_* column is dropped from the SELECT
        // list or the mapper indexes the wrong slot.
        using var reader = BuildTestedObjectReader(
            testedObjectIdType: typeof(long),
            errorTableArType: typeof(long),
            testedObjectId: 1L,
            errorTable: 0,
            errorTableAr: 0L);

        Assert.True(reader.Read());
        var row = SqlServerAoiSourceBase.MapTestedObjectRow(reader);

        Assert.Equal(12.5, row.DeltaXUm);
        Assert.Equal(-7.25, row.DeltaYUm);
        Assert.Equal(0.5, row.DeltaThetaDeg);
        Assert.Equal(3.75, row.DeltaThicknessUm);
        Assert.Equal(0.98, row.DeltaSurface);
    }

    [Fact]
    public void MapTestedObjectRow_NullTopologyAndPartAndJedec_MapsToNull()
    {
        var dt = NewTestedObjectTable(typeof(int), typeof(int));
        dt.Rows.Add(1, 0, 42, 100, 0, 0, /* Topology */ DBNull.Value, 5, 6, 1700000000,
                    /* Part_Number */ DBNull.Value, /* Jedec_Name */ DBNull.Value,
                    /* Delta_X */ DBNull.Value, /* Delta_Y */ DBNull.Value,
                    /* Delta_Theta */ DBNull.Value, /* Delta_Thickness */ DBNull.Value,
                    /* Delta_Surface */ DBNull.Value,
                    // TC5 Phase B — new nullable slots. Face / Face_Number /
                    // Repair_State_Result / Operator_Id are NOT NULL on the
                    // live DBs, but the mapper still IsDBNull-guards them
                    // for defensiveness against future schema drift.
                    /* Face */ DBNull.Value, /* Face_Number */ DBNull.Value,
                    /* Feeder_Machine */ DBNull.Value,
                    /* Repair_State_Result */ DBNull.Value,
                    /* Repair_Numeric_Date_Hour */ DBNull.Value,
                    /* Repair_Button_Comment */ DBNull.Value,
                    /* Repair_Error_Comment */ DBNull.Value,
                    /* Repair_Operator_Comments */ DBNull.Value,
                    /* Operator_Id */ DBNull.Value);
        using var reader = dt.CreateDataReader();
        Assert.True(reader.Read());

        var row = SqlServerAoiSourceBase.MapTestedObjectRow(reader);

        Assert.Null(row.Topology);
        Assert.Null(row.PartNumberName);
        Assert.Null(row.JedecName);
        Assert.Null(row.DeltaXUm);
        Assert.Null(row.DeltaYUm);
        Assert.Null(row.DeltaThetaDeg);
        Assert.Null(row.DeltaThicknessUm);
        Assert.Null(row.DeltaSurface);
        Assert.Null(row.Face);
        Assert.Null(row.FaceNumber);
        Assert.Null(row.FeederName);
        Assert.Null(row.RepairState);
        Assert.Null(row.RepairUtc);
        Assert.Null(row.RepairButtonComment);
        Assert.Null(row.RepairErrorComment);
        Assert.Null(row.RepairOperatorComment);
        Assert.Null(row.RepairOperatorId);
    }

    [Fact]
    public void MapTestedObjectRow_TC5Phase_RepairAndFeederAndFace_RoundTrip()
    {
        // TC5 Phase B — verify the new columns round-trip through the
        // mapper. The default helper populates every slot with a
        // plausible non-null value; this test asserts the mapper reads
        // the correct slot and preserves the value verbatim.
        using var reader = BuildTestedObjectReader(
            testedObjectIdType: typeof(long),
            errorTableArType: typeof(long),
            testedObjectId: 1L,
            errorTable: 0,
            errorTableAr: 0L);

        Assert.True(reader.Read());
        var row = SqlServerAoiSourceBase.MapTestedObjectRow(reader);

        Assert.Equal("Top", row.Face);
        Assert.Equal(0, row.FaceNumber);
        Assert.Equal("DNP", row.FeederName);
        Assert.Equal(1, row.RepairState);
        Assert.Equal(1_700_000_500, row.RepairUtc);
        Assert.Equal("Repaired", row.RepairButtonComment);
        Assert.Equal("Solder", row.RepairErrorComment);
        Assert.Equal("reflowed", row.RepairOperatorComment);
        Assert.Equal(42, row.RepairOperatorId);
    }

    [Fact]
    public void MapTestedObjectRow_TestedObjectIdOverflow_Throws()
    {
        using var reader = BuildTestedObjectReader(
            testedObjectIdType: typeof(long),
            errorTableArType: typeof(long),
            testedObjectId: (long)int.MaxValue + 1,
            errorTable: 0,
            errorTableAr: 0);
        Assert.True(reader.Read());

        Assert.Throws<OverflowException>(
            () => SqlServerAoiSourceBase.MapTestedObjectRow(reader));
    }

    // ---------- MapPanelRow ----------

    [Fact]
    public void MapPanelRow_ReadsAllTypedColumnsCorrectly()
    {
        var dt = NewPanelTable();
        dt.Rows.Add(
            /* Panel_Id            */ 1234,
            /* Machine_Id          */ 10,
            /* Lane_Number         */ 1,
            /* Panel_Bar_Code      */ "BC-001",
            /* Panel_Numeric_Date  */ 1_700_000_000,
            /* Nb_Of_Valid_Cards   */ 4,
            /* Test_Time           */ 12.5,
            /* Panel_Status        */ 1,
            /* Anomaly_BR          */ 0,
            /* Anomaly_AR          */ 0,
            /* Has_Been_Reviewed   */ (byte)1,
            /* Nb_Of_Tested_Object */ 16,
            /* Nb_Of_Error_Object  */ 0,
            /* Operator_Id         */ 42,
            /* Product_Id          */ 100,
            /* Recipe_Id           */ 200);
        using var reader = dt.CreateDataReader();
        Assert.True(reader.Read());

        var row = SqlServerAoiSourceBase.MapPanelRow(reader);

        Assert.Equal(1234, row.PanelId);
        Assert.Equal(10, row.MachineId);
        Assert.Equal(1, row.LaneNumber);
        Assert.Equal("BC-001", row.PanelBarCode);
        Assert.Equal(1_700_000_000, row.PanelNumericDate);
        Assert.Equal(4, row.NbOfValidCards);
        Assert.Equal(12.5, row.TestTime);
        Assert.Equal(1, row.PanelStatus);
        Assert.Equal(0, row.AnomalyBr);
        Assert.Equal(0, row.AnomalyAr);
        Assert.True(row.HasBeenReviewed);
        Assert.Equal(16, row.NbOfTestedObject);
        Assert.Equal(0, row.NbOfErrorObject);
        Assert.Equal(42, row.OperatorId);
        Assert.Equal(100, row.ProductId);
        Assert.Equal(200, row.RecipeId);
    }

    [Fact]
    public void MapPanelRow_OperatorIdNull_MapsToNull()
    {
        // Operator_Id is the only nullable column verified on both DBs
        // (schema `int NULL`). MapPanelRow must not throw and must not
        // silently coerce to 0.
        var dt = NewPanelTable();
        dt.Rows.Add(
            1, 1, 1, "BC", 1_700_000_000, 1, 1.0, 1, 0, 0, (byte)0, 1, 0,
            DBNull.Value,       // Operator_Id
            1, 1);
        using var reader = dt.CreateDataReader();
        Assert.True(reader.Read());

        var row = SqlServerAoiSourceBase.MapPanelRow(reader);

        Assert.Null(row.OperatorId);
    }

    // ---------- MapCardRow ----------

    [Fact]
    public void MapCardRow_ReadsAllTypedColumnsCorrectly()
    {
        // Every column projected by BuildCardsQuery is int NOT NULL on
        // both live DBs (Card_Id, the only polymorphic CARDS column, is
        // only used in the JOIN and never projected). Verify each slot
        // ends up in the right DTO field.
        var dt = NewCardTable();
        dt.Rows.Add(
            /* c.Panel_Id             */ 1234,
            /* c.Card_Number          */ 3,
            /* c.Card_Status          */ 2,
            /* c.Anomaly_BR           */ 5,
            /* c.Anomaly_AR           */ 1,
            /* c.Number_Of_Component  */ 42,
            /* c.Number_Of_Anomaly    */ 3,
            /* p.Machine_Id           */ 10,
            /* p.Product_Id           */ 100,
            /* p.Panel_Numeric_Date   */ 1_700_000_000,
            /* c.Nb_Of_Tests_On_Comp  */ 500,
            /* c.Nb_Of_Tests_On_Pads  */ 250);
        using var reader = dt.CreateDataReader();
        Assert.True(reader.Read());

        var row = SqlServerAoiSourceBase.MapCardRow(reader);

        Assert.Equal(1234L, row.PanelId);
        Assert.Equal(3, row.CardIdOnPanel);
        Assert.Equal(2, row.CardStatus);
        Assert.Equal(5L, row.AnomalyBr);
        Assert.Equal(1L, row.AnomalyAr);
        Assert.Equal(42, row.NbOfTestedObject);
        Assert.Equal(3, row.NbOfErrorObject);
        Assert.Equal(10, row.MachineId);
        Assert.Equal(100, row.ProductId);
        Assert.Equal(1_700_000_000, row.PanelNumericDate);
        Assert.Equal(500, row.NbOfTestsOnComp);
        Assert.Equal(250, row.NbOfTestsOnPads);
    }

    [Fact]
    public void MapCardRow_NoDefects_LeavesLongCountersAtZero()
    {
        // A clean board (status 1, zero anomaly bit-fields) is by far
        // the most common CARDS row on a healthy line; make sure the
        // int-to-long widening path doesn't sign-extend or otherwise
        // mangle a zero.
        var dt = NewCardTable();
        // Slot 10 = Nb_Of_Tests_On_Comp (8), slot 11 = Nb_Of_Tests_On_Pads
        // as DBNull to exercise the post-reflow "paste column absent" path.
        dt.Rows.Add(1, 1, 1, 0, 0, 10, 0, 1, 1, 1_700_000_000, 8, DBNull.Value);
        using var reader = dt.CreateDataReader();
        Assert.True(reader.Read());

        var row = SqlServerAoiSourceBase.MapCardRow(reader);

        Assert.Equal(0L, row.AnomalyBr);
        Assert.Equal(0L, row.AnomalyAr);
        Assert.Equal(0, row.NbOfErrorObject);
        Assert.Equal(8, row.NbOfTestsOnComp);
        Assert.Null(row.NbOfTestsOnPads);
    }

    // ---------- Helpers ----------

    private static DataTableReader BuildTestedObjectReader(
        Type testedObjectIdType,
        Type errorTableArType,
        object testedObjectId,
        object errorTable,
        object errorTableAr)
    {
        var dt = NewTestedObjectTable(testedObjectIdType, errorTableArType);
        dt.Rows.Add(
            /* Panel_Id                 */ 1,
            /* Card_Number              */ 0,
            /* Tested_Object_Id         */ testedObjectId,
            /* Object_Type_Id           */ 100,
            /* Error_Table              */ errorTable,
            /* Error_Table_AR           */ errorTableAr,
            /* Topology                 */ "R1",
            /* Machine_Id               */ 10,
            /* Product_Id               */ 20,
            /* Panel_Numeric_Date       */ 1_700_000_000,
            /* Part_Number              */ "PN-1",
            /* Jedec_Name               */ "0402",
            /* Delta_X                  */ 12.5,
            /* Delta_Y                  */ -7.25,
            /* Delta_Theta              */ 0.5,
            /* Delta_Thickness          */ 3.75,
            /* Delta_Surface            */ 0.98,
            /* Face                     */ "Top",
            /* Face_Number              */ 0,
            /* Feeder_Machine           */ "DNP",
            /* Repair_State_Result      */ 1,
            /* Repair_Numeric_Date_Hour */ 1_700_000_500,
            /* Repair_Button_Comment    */ "Repaired",
            /* Repair_Error_Comment     */ "Solder",
            /* Repair_Operator_Comments */ "reflowed",
            /* Operator_Id              */ 42);
        return dt.CreateDataReader();
    }

    private static DataTable NewTestedObjectTable(Type testedObjectIdType, Type errorTableArType)
    {
        var dt = new DataTable();
        dt.Columns.Add("Panel_Id", typeof(int));
        dt.Columns.Add("Card_Number", typeof(int));
        dt.Columns.Add("Tested_Object_Id", testedObjectIdType);
        dt.Columns.Add("Object_Type_Id", typeof(int));
        dt.Columns.Add("Error_Table", typeof(int));
        dt.Columns.Add("Error_Table_AR", errorTableArType);
        dt.Columns.Add("Topology", typeof(string));
        dt.Columns.Add("Machine_Id", typeof(int));
        dt.Columns.Add("Product_Id", typeof(int));
        dt.Columns.Add("Panel_Numeric_Date", typeof(int));
        dt.Columns.Add("Part_Number", typeof(string));
        dt.Columns.Add("Jedec_Name", typeof(string));
        // Delta_* columns — SQL Server FLOAT projects as double,
        // nullable in both schemas (macros / not-inspected rows
        // legitimately carry NULL).
        dt.Columns.Add("Delta_X", typeof(double));
        dt.Columns.Add("Delta_Y", typeof(double));
        dt.Columns.Add("Delta_Theta", typeof(double));
        dt.Columns.Add("Delta_Thickness", typeof(double));
        dt.Columns.Add("Delta_Surface", typeof(double));
        // TC5 Phase B — panel face, feeder (LEFT JOIN → nullable),
        // and repair fields. Face / Face_Number / Repair_State_Result
        // / Operator_Id are NOT NULL on both live DBs; the others
        // are nullable.
        dt.Columns.Add("Face", typeof(string));
        dt.Columns.Add("Face_Number", typeof(int));
        dt.Columns.Add("Feeder_Machine", typeof(string));
        dt.Columns.Add("Repair_State_Result", typeof(int));
        dt.Columns.Add("Repair_Numeric_Date_Hour", typeof(int));
        dt.Columns.Add("Repair_Button_Comment", typeof(string));
        dt.Columns.Add("Repair_Error_Comment", typeof(string));
        dt.Columns.Add("Repair_Operator_Comments", typeof(string));
        dt.Columns.Add("Operator_Id", typeof(int));
        return dt;
    }

    private static DataTable NewPanelTable()
    {
        var dt = new DataTable();
        dt.Columns.Add("Panel_Id", typeof(int));
        dt.Columns.Add("Machine_Id", typeof(int));
        dt.Columns.Add("Lane_Number", typeof(int));
        dt.Columns.Add("Panel_Bar_Code", typeof(string));
        dt.Columns.Add("Panel_Numeric_Date", typeof(int));
        dt.Columns.Add("Nb_Of_Valid_Cards", typeof(int));
        dt.Columns.Add("Test_Time", typeof(double));   // SQL Server FLOAT
        dt.Columns.Add("Panel_Status", typeof(int));
        dt.Columns.Add("Anomaly_BR", typeof(int));
        dt.Columns.Add("Anomaly_AR", typeof(int));
        dt.Columns.Add("Has_Been_Reviewed", typeof(byte));  // tinyint
        dt.Columns.Add("Nb_Of_Tested_Object", typeof(int));
        dt.Columns.Add("Nb_Of_Error_Object", typeof(int));
        dt.Columns.Add("Operator_Id", typeof(int));
        dt.Columns.Add("Product_Id", typeof(int));
        dt.Columns.Add("Recipe_Id", typeof(int));
        return dt;
    }

    private static DataTable NewCardTable()
    {
        // Column order matches BuildCardsQuery's SELECT list. Every
        // column is int NOT NULL on both live DBs (see
        // tools/db/out/{postreflow,prereflow}/04_cards_columns.csv);
        // Card_Id — the polymorphic bigint/int column — is not
        // projected, so no polymorphic type param is needed here.
        var dt = new DataTable();
        dt.Columns.Add("Panel_Id", typeof(int));
        dt.Columns.Add("Card_Number", typeof(int));
        dt.Columns.Add("Card_Status", typeof(int));
        dt.Columns.Add("Anomaly_BR", typeof(int));
        dt.Columns.Add("Anomaly_AR", typeof(int));
        dt.Columns.Add("Number_Of_Component", typeof(int));
        dt.Columns.Add("Number_Of_Anomaly", typeof(int));
        dt.Columns.Add("Machine_Id", typeof(int));
        dt.Columns.Add("Product_Id", typeof(int));
        dt.Columns.Add("Panel_Numeric_Date", typeof(int));
        // Nb_Of_Tests_On_Comp — int NOT NULL on both DBs (the DPMO/PPM
        // component-test denominator). Nb_Of_Tests_On_Pads — paste
        // denominator, projected as a typed NULL on post-reflow (paste
        // is a pre-reflow stage), so it is nullable here.
        dt.Columns.Add("Nb_Of_Tests_On_Comp", typeof(int));
        dt.Columns.Add("Nb_Of_Tests_On_Pads", typeof(int)).AllowDBNull = true;
        return dt;
    }
}
