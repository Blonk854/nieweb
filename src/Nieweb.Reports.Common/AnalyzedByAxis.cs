namespace Nieweb.Reports.Common;

/// <summary>
/// Grouping-axis choices for report queries. Mirrors the Vieweb
/// "Analyzed by" list (Vieweb §3.1.4.4) so that error, DPMO and FPY
/// reports can share a single grouping enum. Each value describes a
/// column reports should <c>GROUP BY</c> before rolling up counts.
/// </summary>
/// <remarks>
/// Not every axis is implementable against every source: reference
/// designator and inspected object drill into <c>TESTED_OBJECT</c> /
/// <c>PIN</c>, which pre-reflow AOI DBs lack. Report authors must
/// consult <c>SourceCapabilities</c> before offering an axis in the
/// UI. Because the enum is persisted in saved-view JSON as its member
/// name, do not rename these values without a data migration.
/// </remarks>
public enum AnalyzedByAxis
{
    /// <summary>Board (sub-panel) number within the panel.</summary>
    BoardNumber = 0,

    /// <summary>Defect / error type (missing, polarity, solder joint, …).</summary>
    DefectType = 1,

    /// <summary>Pick-and-place sub-element (feeder / nozzle / head / spindle).</summary>
    PnpSubElement1 = 2,

    /// <summary>Second pick-and-place sub-element.</summary>
    PnpSubElement2 = 3,

    /// <summary>Third pick-and-place sub-element.</summary>
    PnpSubElement3 = 4,

    /// <summary>Fourth pick-and-place sub-element.</summary>
    PnpSubElement4 = 5,

    /// <summary>Placement machine name.</summary>
    PnpMachine = 6,

    /// <summary>Inspected object (component / pad / text / connector).</summary>
    InspectedObject = 7,

    /// <summary>Package (JEDEC) name.</summary>
    Package = 8,

    /// <summary>Part number.</summary>
    PartNumber = 9,

    /// <summary>Inspection program (product).</summary>
    Product = 10,

    /// <summary>Repair-action free-form comment.</summary>
    RepairComment = 11,

    /// <summary>Repair-action status (repaired / scrap / false-call / …).</summary>
    RepairStatus = 12,

    /// <summary>Board topology reference designator (R23, U7, …).</summary>
    ReferenceDesignator = 13,

    /// <summary>AOI machine name.</summary>
    AoiMachine = 14,
}
