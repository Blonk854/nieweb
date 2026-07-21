namespace Nieweb.Filters;

/// <summary>
/// Vieweb 1.6.2 filter fields (Vieweb §3.1.2 — "The available operators
/// by filter are described in the above table"). Enumerating them here
/// so that (a) the filter validator can enforce the per-field operator
/// matrix at request time and (b) future reports can call
/// <see cref="FilterFieldMetadata.GetAllowedOperators(FilterField)"/>
/// when building a UI operator drop-down.
/// </summary>
/// <remarks>
/// The enum value names are persisted verbatim in saved-view JSON —
/// never rename them without a migration. Additional fields (shift,
/// production line, IPC class, tolerance interval, …) will be added
/// alongside their reports; keep this list restricted to the operator
/// table Vieweb ships with today so parity stays visible.
/// </remarks>
public enum FilterField
{
    /// <summary>Board (sub-panel) number within the panel.</summary>
    BoardNumber = 0,

    /// <summary>Placement / P&amp;P machine name.</summary>
    PnpMachine = 1,

    /// <summary>Pick-and-place sub-element 1 (feeder / nozzle / head / spindle).</summary>
    PnpSubElement1 = 2,

    /// <summary>Pick-and-place sub-element 2.</summary>
    PnpSubElement2 = 3,

    /// <summary>Pick-and-place sub-element 3.</summary>
    PnpSubElement3 = 4,

    /// <summary>Pick-and-place sub-element 4.</summary>
    PnpSubElement4 = 5,

    /// <summary>Part number.</summary>
    PartNumber = 6,

    /// <summary>Inspected object (component / pad / text / connector).</summary>
    InspectedObject = 7,

    /// <summary>Product (inspection program).</summary>
    Product = 8,

    /// <summary>JEDEC / package name.</summary>
    Package = 9,

    /// <summary>Repair-action status.</summary>
    RepairStatus = 10,

    /// <summary>Repair-action free-form comment.</summary>
    RepairComment = 11,

    /// <summary>Board topology reference designator (R23, U7, …).</summary>
    ReferenceDesignator = 12,

    /// <summary>Defect / error type.</summary>
    Defect = 13,

    /// <summary>Panel bar code (numeric or alphanumeric).</summary>
    PanelBarcode = 14,

    /// <summary>Board / sub-panel ID code.</summary>
    BoardIdCode = 15,

    /// <summary>AOI machine name.</summary>
    AoiMachine = 16,

    /// <summary>
    /// Panel-level status (OK / KO_OPERATOR / OK_OPERATOR / …).
    /// Vieweb only allows <see cref="FilterOperator.Equal"/> here.
    /// </summary>
    PanelStatus = 17,

    /// <summary>
    /// Sub-panel status. Vieweb only allows
    /// <see cref="FilterOperator.Equal"/>.
    /// </summary>
    BoardStatus = 18,
}
