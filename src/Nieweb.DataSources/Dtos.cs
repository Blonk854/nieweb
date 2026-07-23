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
/// <param name="DeltaXUm">
/// <c>TESTED_OBJECT.Delta_X</c> — measured X deviation in µm
/// (component placement offset for components / paste-print offset
/// for paste pads). <c>null</c> when the row carries no measurement
/// (e.g. not-inspected objects) or when the source's schema omits the
/// column. Used by the CR2 Deviation chart.
/// </param>
/// <param name="DeltaYUm">
/// <c>TESTED_OBJECT.Delta_Y</c> — measured Y deviation in µm.
/// Same nullability rules as <see cref="DeltaXUm"/>.
/// </param>
/// <param name="DeltaThetaDeg">
/// <c>TESTED_OBJECT.Delta_Theta</c> — measured rotation deviation in
/// degrees. Same nullability rules as <see cref="DeltaXUm"/>.
/// </param>
/// <param name="DeltaThicknessUm">
/// <c>TESTED_OBJECT.Delta_Thickness</c> — measured Z / height
/// deviation in µm (Vieweb calls this "Z"). Same nullability rules
/// as <see cref="DeltaXUm"/>.
/// </param>
/// <param name="DeltaSurface">
/// <c>TESTED_OBJECT.Delta_Surface</c> — measured surface deviation
/// (unitless ratio; adapter passes the raw value through). Same
/// nullability rules as <see cref="DeltaXUm"/>.
/// </param>
/// <param name="Face">
/// Human-readable side name from <c>PANELS.Face</c> (e.g. "Top",
/// "Bottom") — inherited from the parent panel because the AOI DB
/// stores the side at panel granularity, not per-component. Used by
/// the TC5 failed-objects table so operators can tell which side of
/// the PCB the component is on at a glance. <c>null</c> when the
/// source's schema omits the column.
/// </param>
/// <param name="FaceNumber">
/// Numeric side code from <c>PANELS.Face_Number</c> (0 / 1 for
/// top / bottom, or -1 for undefined). Same source semantics as
/// <see cref="Face"/>. <c>null</c> when the source's schema omits
/// the column.
/// </param>
/// <param name="FeederName">
/// Feeder identifier joined from <c>FEEDER.Feeder_Machine</c> via
/// <c>TESTED_OBJECT.Feeder_Id</c>. Surfaced verbatim — empty strings
/// stay empty (the default HLYAOI feeder is <c>""</c>), and
/// <c>null</c> only when the row's <c>Feeder_Id</c> has no matching
/// FEEDER row (never observed on the live DBs, kept for
/// defensiveness). The live post- and pre-reflow DBs both ship the
/// same three rows on FEEDER ("" / "DNP" / "NOGO"); TC5 renders
/// whatever is stored.
/// </param>
/// <param name="RepairState">
/// <c>TESTED_OBJECT.Repair_State_Result</c> — signed enum
/// (<c>-2</c>=not inspected, <c>-1</c>=not detected as faulty,
/// <c>0</c>=faulty not yet reviewed, <c>1</c>=repaired,
/// <c>2</c>=good, <c>3</c>=confirmed faulty). Both live schemas
/// ship <c>Repair_State_Result</c> as <c>NOT NULL</c>, so callers
/// will almost always see a value; <c>null</c> is reserved for
/// sources that omit the column entirely.
/// </param>
/// <param name="RepairUtc">
/// <c>TESTED_OBJECT.Repair_Numeric_Date_Hour</c> — ANSI <c>time_t</c>
/// (seconds since 1970-01-01 UTC) of the operator's repair
/// sanction, or <c>null</c> when the object was never reviewed
/// (column is nullable on both live DBs). Kept as <c>int?</c> to
/// match the pattern of <see cref="PanelNumericDate"/> — callers
/// should convert with
/// <see cref="DateTimeOffset.FromUnixTimeSeconds(long)"/>.
/// </param>
/// <param name="RepairButtonComment">
/// <c>TESTED_OBJECT.Repair_Button_Comment</c> — the label of the
/// review button the operator pressed (e.g. "Repaired", "False
/// call"). <c>null</c> when the object was never reviewed or the
/// source schema omits the column.
/// </param>
/// <param name="RepairErrorComment">
/// <c>TESTED_OBJECT.Repair_Error_Comment</c> — free text associated
/// with the pressed button (defect classification / cause). Same
/// nullability rules as <see cref="RepairButtonComment"/>.
/// </param>
/// <param name="RepairOperatorComment">
/// <c>TESTED_OBJECT.Repair_Operator_Comments</c> (plural in the AOI
/// schema) — the operator's free-form comment. Same nullability
/// rules as <see cref="RepairButtonComment"/>.
/// </param>
/// <param name="RepairOperatorId">
/// <c>TESTED_OBJECT.Operator_Id</c> — foreign key to
/// <c>OPERATOR.Operator_Id</c>. Surfaced verbatim (including the
/// sentinel value <c>0</c> which the AOI DB uses for "no operator");
/// TC5 renders <c>0</c> as an empty cell so the raw value can round-
/// trip through the API without losing information.
/// </param>
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
    string? JedecName,
    double? DeltaXUm = null,
    double? DeltaYUm = null,
    double? DeltaThetaDeg = null,
    double? DeltaThicknessUm = null,
    double? DeltaSurface = null,
    string? Face = null,
    int? FaceNumber = null,
    string? FeederName = null,
    int? RepairState = null,
    int? RepairUtc = null,
    string? RepairButtonComment = null,
    string? RepairErrorComment = null,
    string? RepairOperatorComment = null,
    int? RepairOperatorId = null);

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

/// <summary>
/// One row of the <c>PIN</c> table. Used by the TC1 traceability
/// drill-down (panel → subpanel → tested object → pin) on sources
/// that implement <see cref="IPinLevelSource"/> (v5.0 post-reflow
/// only; the pre-reflow v4.3.1 schema does not ship the <c>PIN</c>
/// table at all).
/// </summary>
/// <param name="PinId">
/// <c>PIN.Pin_Id</c> — surrogate <c>bigint</c> primary key.
/// </param>
/// <param name="TestedObjectId">
/// <c>PIN.Tested_Object_Id</c> — foreign key to
/// <c>TESTED_OBJECT.Tested_Object_Id</c> (the same value exposed as
/// <see cref="TestedObjectRow.ObjectId"/>).
/// </param>
/// <param name="ComponentSide">
/// <c>PIN.Component_Side</c> — clockwise pin side of the component
/// (0=N, 1=E, 2=S, 3=W) — see the <c>vit-aoi-database</c> skill.
/// </param>
/// <param name="PinIndexOnSide">
/// <c>PIN.Pin_Index_On_Side</c> — 0-based index within the side,
/// clockwise.
/// </param>
/// <param name="IpcPinNb">
/// <c>PIN.IPC_Pin_Nb</c> — global IPC-style pin index; <c>null</c>
/// or <c>0</c> when the AOI recipe did not resolve one.
/// </param>
/// <param name="ErrorTable">
/// Raw AOI defect bitfield (<c>PIN.Error_Table</c>). Uses the same
/// bit assignments as <c>TESTED_OBJECT.Error_Table</c> — see the
/// <c>Defects.DefectBit</c> table.
/// </param>
/// <param name="ErrorTableAr">
/// Post-review defect bitfield (<c>PIN.Error_Table_AR</c>).
/// </param>
/// <param name="ReviewSanction">
/// <c>PIN.Review_Sanction</c> — raw <c>tinyint</c> value. Documented
/// meanings mirror <c>Repair_State_Result</c>
/// (<c>-1</c>=not detected as faulty, <c>0</c>=faulty not yet
/// reviewed, <c>1</c>=repaired, <c>2</c>=good, <c>3</c>=confirmed
/// faulty); we surface the raw <c>int</c> so the UI can present the
/// exact stored value.
/// </param>
public sealed record PinRow(
    long PinId,
    long TestedObjectId,
    int ComponentSide,
    int PinIndexOnSide,
    int? IpcPinNb,
    long ErrorTable,
    long ErrorTableAr,
    int ReviewSanction);
