namespace Nieweb.DataSources;

/// <summary>
/// One page of rows plus paging metadata.
/// </summary>
/// <typeparam name="TRow">Row DTO type.</typeparam>
/// <typeparam name="TCursor">Keyset cursor type for the next page (null when exhausted).</typeparam>
public sealed record Page<TRow, TCursor>(
    IReadOnlyList<TRow> Rows,
    TCursor? NextCursor,
    bool HasMore)
    where TCursor : struct;

public sealed record PanelRow(
    int PanelId,
    int MachineId,
    int LaneNumber,
    string PanelBarCode,
    int PanelNumericDate,
    int NbOfValidCards,
    double TestTime,
    int PanelStatus,
    int AnomalyBr,
    int AnomalyAr,
    bool HasBeenReviewed,
    int NbOfTestedObject,
    int NbOfErrorObject,
    int? OperatorId,
    int ProductId,
    int RecipeId);

/// <summary>
/// One row of the <c>CARDS</c> (sub-panel) table. <see cref="MachineId"/>
/// and <see cref="ProductId"/> are copied from the parent <c>PANELS</c>
/// row so board-level reports (FPY table, DPMO table) can group and
/// filter without a second query — the SQL adapters join to
/// <c>dbo.PANELS</c> when materialising this shape.
/// </summary>
public sealed record CardRow(
    long PanelId,
    int CardIdOnPanel,
    int CardStatus,
    long AnomalyBr,
    long AnomalyAr,
    int NbOfTestedObject,
    int NbOfErrorObject,
    int MachineId,
    int ProductId,
    int PanelNumericDate);

/// <summary>
/// One row of the <c>TESTED_OBJECT</c> table. Fields <see cref="MachineId"/>,
/// <see cref="ProductId"/>, and <see cref="PanelNumericDate"/> are copied
/// from the parent <c>PANELS</c> row so component-level reports (DPMO
/// table, Pareto chart) can group and window-filter without a second
/// query — SQL adapters materialise this shape via a join to
/// <c>dbo.PANELS</c>. The reference-data columns
/// (<see cref="Topology"/>, <see cref="PartNumberName"/>,
/// <see cref="JedecName"/>) are <c>null</c> on sources whose schema
/// omits them (e.g. pre-reflow <c>MEAOI</c> for pin-derived fields);
/// grouping ignores <c>null</c>-keyed rows for those axes.
/// </summary>
/// <param name="PanelId">Parent <c>PANELS.Panel_Id</c>.</param>
/// <param name="CardIdOnPanel">Sub-panel identifier within the parent panel.</param>
/// <param name="ObjectId">Tested-object identifier within the sub-panel.</param>
/// <param name="ObjectTypeId">
/// <c>OBJECT_TYPE.Object_Type_Id</c> bit-code
/// (1=Component, 8=Text, 16=Paste pad, 4096=Macro, 33554432=Foreign
/// material — see the <c>vit-aoi-database</c> skill).
/// </param>
/// <param name="ErrorTable">Raw AOI defect bitfield (<c>TESTED_OBJECT.Error_Table</c>).</param>
/// <param name="ErrorTableAr">
/// Post-review defect bitfield (<c>TESTED_OBJECT.Error_Table_AR</c>).
/// On sources where the AR column is not present, the adapter mirrors
/// <see cref="ErrorTable"/> — Nieweb.Reports treats missing-AR as
/// "no review has occurred yet".
/// </param>
/// <param name="Status">Object-level status (0=OK / n&gt;0=faulty bit).</param>
/// <param name="MachineId">Parent panel's <c>Machine_Id</c>.</param>
/// <param name="ProductId">Parent panel's <c>Product_Id</c>.</param>
/// <param name="PanelNumericDate">Parent panel's timestamp (ANSI <c>time_t</c>).</param>
/// <param name="Topology">Reference designator (aka <c>TESTED_OBJECT.Topology</c>).</param>
/// <param name="PartNumberName">Human-readable part number, joined from <c>PART_NUMBER</c>.</param>
/// <param name="JedecName">JEDEC / package name, joined from <c>JEDEC</c>.</param>
public sealed record TestedObjectRow(
    long PanelId,
    int CardIdOnPanel,
    int ObjectId,
    int ObjectTypeId,
    long ErrorTable,
    long ErrorTableAr,
    int Status,
    int MachineId,
    int ProductId,
    int PanelNumericDate,
    string? Topology,
    string? PartNumberName,
    string? JedecName);

public sealed record Machine(int MachineId, int MachineType, string MachineName, string? MachineTypeName);

public sealed record Product(int ProductId, string? ProductName, string? Revision, string? Description);

/// <summary>
/// A recipe (inspection program). <see cref="FileName"/> is the human-readable
/// name used throughout the UI - the AOI Superviseur schema calls this column
/// <c>File_Name</c> even though it's really the recipe name.
/// </summary>
public sealed record Recipe(
    int RecipeId,
    string? FileName,
    int ProductId,
    string? Author,
    int InspectedSideNb,
    string? InspectedSideName,
    string? Customer,
    string? ProductionStep,
    string? VariantName);
