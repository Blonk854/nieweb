namespace Nieweb.Reports.Common.Defects;

/// <summary>
/// One bit position in <c>TESTED_OBJECT.Error_Table</c> /
/// <c>Error_Table_AR</c> (and, for a subset of positions, in
/// <c>PIN.Error_Table</c> / <c>PIN.Error_Table_AR</c>). The bit number
/// is the enum value (bit 1 → value 1, bit 25 → value 25); the
/// bitmask value is <c>1 &lt;&lt; (bit - 1)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Sourced verbatim from the <c>vit-aoi-database</c> skill / VIT
/// Vision3D CR4 reference. <see cref="DefectBitDecoder"/> is the
/// central authority for translating raw bitfields back into these
/// enum values; do not decode by hand. Legacy Vieweb bug <b>#11211</b>
/// (wrong defect displayed) originated in ad-hoc bit-to-name mappings
/// scattered across the code base — Nieweb keeps a single decoder to
/// stop the bug recurring.
/// </para>
/// <para>
/// Bits 6, 12, 13, 14, 15, 16, 17, and 18 are marked obsolete in
/// modern schema (see <see cref="DefectBitInfo.IsObsolete"/>) but
/// still surface in archived data — the decoder therefore still
/// reports them; UI layers may hide them behind a "show obsolete
/// defects" toggle.
/// </para>
/// </remarks>
public enum DefectBit
{
    /// <summary>Bit 1 — object missing.</summary>
    ObjectMissing = 1,

    /// <summary>Bit 2 — polarity error.</summary>
    PolarityError = 2,

    /// <summary>Bit 3 — solder joint defect (refer to <c>PIN</c>).</summary>
    SolderJointDefect = 3,

    /// <summary>Bit 4 — solder bridge defect (refer to <c>PIN</c>).</summary>
    SolderBridgeDefect = 4,

    /// <summary>Bit 5 — OCV (Optical Character Verification) error.</summary>
    OcvError = 5,

    /// <summary>Bit 6 — model not found in library. Obsolete: use <c>Not_Inspected_Cause = 2</c>.</summary>
    ModelNotFound = 6,

    /// <summary>Bit 7 — <c>Delta_X</c> out of range.</summary>
    DeltaXOutOfRange = 7,

    /// <summary>Bit 8 — <c>Delta_Y</c> out of range.</summary>
    DeltaYOutOfRange = 8,

    /// <summary>Bit 9 — <c>Delta_Theta</c> out of range.</summary>
    DeltaThetaOutOfRange = 9,

    /// <summary>Bit 10 — <c>Delta_Thickness</c> out of range.</summary>
    DeltaThicknessOutOfRange = 10,

    /// <summary>Bit 11 — paste surface area out of range.</summary>
    PasteSurfaceAreaOutOfRange = 11,

    /// <summary>Bit 12 — element skipped. Obsolete: use <c>Not_Inspected_Cause = 1</c>.</summary>
    ElementSkipped = 12,

    /// <summary>Bit 13 — connector: bad pin-column spacing. Obsolete.</summary>
    ConnectorBadPinColumnSpacing = 13,

    /// <summary>Bit 14 — connector: bad pin-row spacing. Obsolete.</summary>
    ConnectorBadPinRowSpacing = 14,

    /// <summary>Bit 15 — connector: pin missing. Obsolete.</summary>
    ConnectorPinMissing = 15,

    /// <summary>Bit 16 — connector: bad pin alignment. Obsolete.</summary>
    ConnectorBadPinAlignment = 16,

    /// <summary>Bit 17 — volume out of range. Obsolete.</summary>
    VolumeOutOfRange = 17,

    /// <summary>Bit 18 — bad appearance. Obsolete.</summary>
    BadAppearance = 18,

    /// <summary>Bit 19 — potential defect imported from SPI.</summary>
    PotentialDefectImportedFromSpi = 19,

    /// <summary>Bit 20 — tilt error (bad coplanarity).</summary>
    TiltError = 20,

    /// <summary>Bit 21 — side overhang (IPC 610).</summary>
    SideOverhang = 21,

    /// <summary>Bit 22 — length overhang (IPC 610).</summary>
    LengthOverhang = 22,

    /// <summary>Bit 23 — foreign material detected.</summary>
    ForeignMaterialDetected = 23,

    /// <summary>Bit 24 — component present (should not be).</summary>
    ComponentPresentButShouldNotBe = 24,

    /// <summary>Bit 25 — lifted lead (refer to <c>PIN</c>).</summary>
    LiftedLead = 25,
}
