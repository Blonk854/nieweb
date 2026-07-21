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
    public void MapTestedObjectRow_NullTopologyAndPartAndJedec_MapsToNull()
    {
        var dt = NewTestedObjectTable(typeof(int), typeof(int));
        dt.Rows.Add(1, 0, 42, 100, 0, 0, /* Topology */ DBNull.Value, 5, 6, 1700000000,
                    /* Part_Number */ DBNull.Value, /* Jedec_Name */ DBNull.Value);
        using var reader = dt.CreateDataReader();
        Assert.True(reader.Read());

        var row = SqlServerAoiSourceBase.MapTestedObjectRow(reader);

        Assert.Null(row.Topology);
        Assert.Null(row.PartNumberName);
        Assert.Null(row.JedecName);
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
            /* Panel_Id           */ 1,
            /* Card_Number        */ 0,
            /* Tested_Object_Id   */ testedObjectId,
            /* Object_Type_Id     */ 100,
            /* Error_Table        */ errorTable,
            /* Error_Table_AR     */ errorTableAr,
            /* Topology           */ "R1",
            /* Machine_Id         */ 10,
            /* Product_Id         */ 20,
            /* Panel_Numeric_Date */ 1_700_000_000,
            /* Part_Number        */ "PN-1",
            /* Jedec_Name         */ "0402");
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
}
