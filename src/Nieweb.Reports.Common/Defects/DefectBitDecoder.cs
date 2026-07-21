using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Numerics;

namespace Nieweb.Reports.Common.Defects;

/// <summary>
/// Central authority for decoding <c>TESTED_OBJECT.Error_Table</c> and
/// <c>TESTED_OBJECT.Error_Table_AR</c> (and the pin-level counterparts)
/// into <see cref="DefectBit"/> values. Fixes legacy Vieweb bug
/// <b>#11211</b> ("wrong defect displayed") by concentrating every
/// bit-to-name mapping into one static catalogue.
/// </summary>
/// <remarks>
/// <para>
/// Sourced verbatim from the <c>vit-aoi-database</c> skill (VIT
/// Vision3D CR4 documentation). Only bits 1..25 are populated;
/// <c>Error_Table_AR</c> is <c>BIGINT</c> but its upper 32 bits are
/// reserved by VIT for classification metadata and are ignored by the
/// decoder — a stray upper bit must not spuriously appear as an
/// unknown defect.
/// </para>
/// <para>
/// The service is stateless and safe to call from any thread.
/// Consumers typically want:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="Decode(long)"/> to enumerate every set bit as <see cref="DefectBitInfo"/>.</description></item>
///   <item><description><see cref="HasBit(long, DefectBit)"/> for quick membership tests.</description></item>
///   <item><description><see cref="CountBits(long)"/> for DPMO numerators (Vieweb "one defect per set bit" definition).</description></item>
///   <item><description><see cref="IsComponentGood(long, int)"/> to apply the VIT rule "<c>Error_Table_AR = 0</c> is <em>not</em> enough — also require <c>Not_Inspected_Cause = 0</c>".</description></item>
/// </list>
/// </remarks>
public static class DefectBitDecoder
{
    /// <summary>
    /// Every <see cref="DefectBit"/> in bit-number order (1..25).
    /// Suitable for building Pareto columns / DPMO detail rows in a
    /// stable order.
    /// </summary>
    public static ImmutableArray<DefectBitInfo> All { get; } = BuildCatalogue();

    /// <summary>Fast lookup by enum member.</summary>
    public static FrozenDictionary<DefectBit, DefectBitInfo> ByBit { get; }
        = All.ToFrozenDictionary(info => info.Bit);

    /// <summary>Fast lookup by bit-mask value (e.g. <c>4</c> → <see cref="DefectBit.SolderJointDefect"/>).</summary>
    public static FrozenDictionary<long, DefectBitInfo> ByMask { get; }
        = All.ToFrozenDictionary(info => info.Mask);

    /// <summary>
    /// Every bit in the low 25 positions. <c>Error_Table_AR</c>
    /// values whose upper bits (positions 26..64) are set have those
    /// bits stripped before decoding.
    /// </summary>
    public const long Bits1To25Mask = (1L << 25) - 1L;

    /// <summary>
    /// Yield one <see cref="DefectBitInfo"/> per bit set in
    /// <paramref name="errorTable"/> in ascending bit order.
    /// Upper (classification) bits above bit 25 are ignored so
    /// <c>Error_Table_AR</c> values carrying classification metadata
    /// decode cleanly.
    /// </summary>
    public static IEnumerable<DefectBitInfo> Decode(long errorTable)
    {
        var relevant = errorTable & Bits1To25Mask;
        for (var bit = 1; bit <= 25 && relevant != 0; bit++)
        {
            var mask = 1L << (bit - 1);
            if ((relevant & mask) != 0)
            {
                // The catalogue is dense (bits 1..25 all present) so
                // this indexer never throws.
                yield return All[bit - 1];
                relevant &= ~mask;
            }
        }
    }

    /// <summary>
    /// <c>true</c> when <paramref name="bit"/> is set in
    /// <paramref name="errorTable"/> (upper bits above 25 are ignored).
    /// </summary>
    public static bool HasBit(long errorTable, DefectBit bit)
    {
        if (!ByBit.TryGetValue(bit, out var info))
        {
            throw new ArgumentOutOfRangeException(nameof(bit), bit, "Unknown defect bit.");
        }
        return (errorTable & info.Mask) != 0;
    }

    /// <summary>
    /// Population count of bits 1..25 in <paramref name="errorTable"/>.
    /// This is the count Vieweb uses as the DPMO numerator: "one
    /// defect per set bit" — a component can accumulate multiple bits
    /// (e.g. missing + polarity + tilt) and each counts.
    /// </summary>
    public static int CountBits(long errorTable)
    {
        var relevant = errorTable & Bits1To25Mask;
        return BitOperations.PopCount((ulong)relevant);
    }

    /// <summary>
    /// Apply the VIT rule from the <c>vit-aoi-database</c> skill:
    /// "<c>Error_Table_AR = 0</c> alone is <em>not</em> enough to
    /// declare a component good. You must also check
    /// <c>Not_Inspected_Cause = 0</c>". Returns <c>true</c> when both
    /// conditions hold (low 25 bits of <paramref name="errorTableAr"/>
    /// clear AND <paramref name="notInspectedCause"/> == 0).
    /// </summary>
    public static bool IsComponentGood(long errorTableAr, int notInspectedCause)
    {
        return (errorTableAr & Bits1To25Mask) == 0L && notInspectedCause == 0;
    }

    /// <summary>
    /// Subset of <see cref="All"/> whose <see cref="DefectBitInfo.AppearsOnPin"/>
    /// is <c>true</c> — the bits VIT documents as populated on the
    /// <c>PIN</c> table (joint, bridge, side overhang, length overhang,
    /// lifted lead).
    /// </summary>
    public static ImmutableArray<DefectBitInfo> PinLevelBits { get; }
        = [.. All.Where(info => info.AppearsOnPin)];

    private static ImmutableArray<DefectBitInfo> BuildCatalogue()
    {
        // (Bit, DisplayName, Description, IsObsolete, ObsolescenceNote, AppearsOnPin)
        // Verbatim from vit-aoi-database SKILL.md (Vision3D CR4).
        return
        [
            Info(DefectBit.ObjectMissing, "Object missing",
                "Component or pad missing from its expected footprint.",
                false, null, false),
            Info(DefectBit.PolarityError, "Polarity error",
                "Polarised component placed in the wrong orientation.",
                false, null, false),
            Info(DefectBit.SolderJointDefect, "Solder joint defect",
                "Defective solder joint. Refer to the PIN table for the offending pin(s).",
                false, null, true),
            Info(DefectBit.SolderBridgeDefect, "Solder bridge defect",
                "Solder bridging between adjacent pins. Refer to the PIN table.",
                false, null, true),
            Info(DefectBit.OcvError, "OCV error",
                "Optical Character Verification failed (text on component does not match expected).",
                false, null, false),
            Info(DefectBit.ModelNotFound, "Model not found",
                "Component model not found in the AOI library.",
                true, "Use Not_Inspected_Cause = 2 (ModelNotFound) instead.", false),
            Info(DefectBit.DeltaXOutOfRange, "Delta_X out of range",
                "Component or pad X-position deviation exceeds tolerance.",
                false, null, false),
            Info(DefectBit.DeltaYOutOfRange, "Delta_Y out of range",
                "Component or pad Y-position deviation exceeds tolerance.",
                false, null, false),
            Info(DefectBit.DeltaThetaOutOfRange, "Delta_Theta out of range",
                "Component rotation deviation exceeds tolerance.",
                false, null, false),
            Info(DefectBit.DeltaThicknessOutOfRange, "Delta_Thickness out of range",
                "Paste or component height deviation exceeds tolerance.",
                false, null, false),
            Info(DefectBit.PasteSurfaceAreaOutOfRange, "Paste surface area out of range",
                "Paste-print surface area outside expected range.",
                false, null, false),
            Info(DefectBit.ElementSkipped, "Element skipped",
                "Inspection skipped for this element.",
                true, "Use Not_Inspected_Cause = 1 (ManuallySkipped) instead.", false),
            Info(DefectBit.ConnectorBadPinColumnSpacing, "Bad pin-column spacing",
                "Connector: pin column spacing out of tolerance.",
                true, "Connector-specific bits are obsolete; use generic pin geometry checks.", false),
            Info(DefectBit.ConnectorBadPinRowSpacing, "Bad pin-row spacing",
                "Connector: pin row spacing out of tolerance.",
                true, "Connector-specific bits are obsolete; use generic pin geometry checks.", false),
            Info(DefectBit.ConnectorPinMissing, "Connector pin missing",
                "Connector: one or more pins missing.",
                true, "Connector-specific bits are obsolete; use ObjectMissing for the whole connector.", false),
            Info(DefectBit.ConnectorBadPinAlignment, "Bad pin alignment",
                "Connector: pin alignment out of tolerance.",
                true, "Connector-specific bits are obsolete; use generic pin geometry checks.", false),
            Info(DefectBit.VolumeOutOfRange, "Volume out of range",
                "Paste-deposit volume outside expected range.",
                true, "Obsolete: replaced by per-pin volume measurements in PIN_MEASURE.", false),
            Info(DefectBit.BadAppearance, "Bad appearance",
                "Visual appearance out of tolerance.",
                true, "Obsolete.", false),
            Info(DefectBit.PotentialDefectImportedFromSpi, "Potential defect (from SPI)",
                "Defect imported from an upstream SPI inspection (feed-forward).",
                false, null, false),
            Info(DefectBit.TiltError, "Tilt error",
                "Component tilt / coplanarity error.",
                false, null, false),
            Info(DefectBit.SideOverhang, "Side overhang (IPC 610)",
                "Side-overhang measurement out of tolerance per IPC 610.",
                false, null, true),
            Info(DefectBit.LengthOverhang, "Length overhang (IPC 610)",
                "Length-overhang measurement out of tolerance per IPC 610.",
                false, null, true),
            Info(DefectBit.ForeignMaterialDetected, "Foreign material",
                "Foreign object detected on board. See OBJECT_TYPE.Object_Type_Id = 33554432.",
                false, null, false),
            Info(DefectBit.ComponentPresentButShouldNotBe, "Component present (should not be)",
                "Component detected in a location where none was expected.",
                false, null, false),
            Info(DefectBit.LiftedLead, "Lifted lead",
                "Lead lifted off the pad. Refer to the PIN table.",
                false, null, true),
        ];
    }

    private static DefectBitInfo Info(
        DefectBit bit,
        string displayName,
        string description,
        bool isObsolete,
        string? obsolescenceNote,
        bool appearsOnPin)
    {
        var number = (int)bit;
        return new DefectBitInfo(
            Bit: bit,
            BitNumber: number,
            Mask: 1L << (number - 1),
            Name: bit.ToString(),
            DisplayName: displayName,
            Description: description,
            IsObsolete: isObsolete,
            ObsolescenceNote: obsolescenceNote,
            AppearsOnPin: appearsOnPin);
    }
}
