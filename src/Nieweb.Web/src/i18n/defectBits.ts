/**
 * SPA-side mirror of `Nieweb.Reports.Common.Defects.DefectBitDecoder`
 * (bits 1..25 of `TESTED_OBJECT.Error_Table` / `Error_Table_AR`).
 *
 * The C# catalogue is the authority; this file exists so the failed-
 * objects table can render "Error type" cells offline without a
 * server round-trip per row. Keep it in sync when a new bit is added
 * on the .NET side.
 *
 * Legacy Vieweb bug #11211 ("wrong defect displayed") was rooted in
 * ad-hoc bit-to-name mappings scattered across the codebase — Nieweb
 * has ONE catalogue per platform (this file + `DefectBitDecoder.cs`)
 * to prevent recurrence.
 */

/**
 * Every bit position in `Error_Table` / `Error_Table_AR` documented
 * by VIT (Vision3D CR4). `key` is the i18n leaf key
 * (`defect.bits.<key>`); `defaultLabel` is the English fallback so
 * the decoder still works before i18n is loaded (used in unit tests
 * and by the format helper when i18n's `t` returns the key
 * unchanged).
 */
export type DefectBitDescriptor = {
    /** 1-based bit position (matches `DefectBit.<Name>` in C#). */
    bit: number;
    /** i18n leaf key: full key is `defect.bits.<key>`. */
    key: string;
    /** English fallback used when i18n has not resolved the key. */
    defaultLabel: string;
    /** Marked obsolete in modern schema (still surfaces in archives). */
    obsolete: boolean;
};

/**
 * Catalogue mirror of `DefectBitDecoder.All`. Order is 1..25 (bit
 * ascending) — matches the C# side so numeric parity holds when a
 * report renders a Pareto chart derived server-side and this
 * client-side decoder is used for a drill-down row.
 */
export const DEFECT_BITS: readonly DefectBitDescriptor[] = [
    { bit: 1, key: "objectMissing", defaultLabel: "Object missing", obsolete: false },
    { bit: 2, key: "polarityError", defaultLabel: "Polarity error", obsolete: false },
    { bit: 3, key: "solderJointDefect", defaultLabel: "Solder joint defect", obsolete: false },
    { bit: 4, key: "solderBridgeDefect", defaultLabel: "Solder bridge defect", obsolete: false },
    { bit: 5, key: "ocvError", defaultLabel: "OCV error", obsolete: false },
    { bit: 6, key: "modelNotFound", defaultLabel: "Model not found", obsolete: true },
    { bit: 7, key: "deltaXOutOfRange", defaultLabel: "Delta_X out of range", obsolete: false },
    { bit: 8, key: "deltaYOutOfRange", defaultLabel: "Delta_Y out of range", obsolete: false },
    { bit: 9, key: "deltaThetaOutOfRange", defaultLabel: "Delta_Theta out of range", obsolete: false },
    { bit: 10, key: "deltaThicknessOutOfRange", defaultLabel: "Delta_Thickness out of range", obsolete: false },
    { bit: 11, key: "pasteSurfaceAreaOutOfRange", defaultLabel: "Paste surface area out of range", obsolete: false },
    { bit: 12, key: "elementSkipped", defaultLabel: "Element skipped", obsolete: true },
    { bit: 13, key: "connectorBadPinColumnSpacing", defaultLabel: "Bad pin-column spacing", obsolete: true },
    { bit: 14, key: "connectorBadPinRowSpacing", defaultLabel: "Bad pin-row spacing", obsolete: true },
    { bit: 15, key: "connectorPinMissing", defaultLabel: "Connector pin missing", obsolete: true },
    { bit: 16, key: "connectorBadPinAlignment", defaultLabel: "Bad pin alignment", obsolete: true },
    { bit: 17, key: "volumeOutOfRange", defaultLabel: "Volume out of range", obsolete: true },
    { bit: 18, key: "badAppearance", defaultLabel: "Bad appearance", obsolete: true },
    { bit: 19, key: "potentialDefectImportedFromSpi", defaultLabel: "Potential defect (from SPI)", obsolete: false },
    { bit: 20, key: "tiltError", defaultLabel: "Tilt error", obsolete: false },
    { bit: 21, key: "sideOverhang", defaultLabel: "Side overhang (IPC 610)", obsolete: false },
    { bit: 22, key: "lengthOverhang", defaultLabel: "Length overhang (IPC 610)", obsolete: false },
    { bit: 23, key: "foreignMaterialDetected", defaultLabel: "Foreign material", obsolete: false },
    { bit: 24, key: "componentPresentButShouldNotBe", defaultLabel: "Component present (should not be)", obsolete: false },
    { bit: 25, key: "liftedLead", defaultLabel: "Lifted lead", obsolete: false },
];

/**
 * Mask covering bits 1..25 — matches
 * `DefectBitDecoder.Bits1To25Mask`. Bits above 25 are reserved by
 * VIT for classification metadata and are NOT defect bits; the
 * decoder strips them before enumerating.
 *
 * Using `Number.MAX_SAFE_INTEGER`-compatible math: `1 << 24` in
 * JavaScript is safe (it fits in 32-bit int space), and the mask is
 * `2^25 - 1 = 33_554_431`.
 */
export const DEFECT_BITS_1_TO_25_MASK = 0x01_FF_FF_FF; // 2^25 - 1

/**
 * Enumerate every set bit position (1..25) in
 * `errorTable`, in ascending bit order. Upper bits above 25 are
 * silently ignored (they carry VIT classification metadata, not
 * defects). Returns bit numbers so the caller can index
 * {@link DEFECT_BITS}.
 *
 * Fractional inputs are floored; negative inputs are treated as
 * their unsigned 32-bit reinterpretation — matches the C# side
 * where `Error_Table` is `int` / `bigint`.
 */
export function decodeDefectBits(errorTable: number): number[] {
    if (!Number.isFinite(errorTable) || errorTable === 0) return [];
    // Bit-and with the low-25 mask. JavaScript bitwise ops are 32-bit
    // signed — that's fine here because we only care about bits 0..24.
    const relevant = Math.floor(errorTable) & DEFECT_BITS_1_TO_25_MASK;
    const out: number[] = [];
    for (let bit = 1; bit <= 25; bit++) {
        const mask = 1 << (bit - 1);
        if ((relevant & mask) !== 0) {
            out.push(bit);
        }
    }
    return out;
}

/**
 * Count the number of set bits (1..25) in `errorTable`. Mirrors
 * `DefectBitDecoder.CountBits` — this is the Vieweb DPMO
 * numerator's per-object contribution ("one defect per set bit").
 */
export function countDefectBits(errorTable: number): number {
    if (!Number.isFinite(errorTable) || errorTable === 0) return 0;
    let relevant = Math.floor(errorTable) & DEFECT_BITS_1_TO_25_MASK;
    // Hamming weight over 32 bits — enough for the low 25.
    let count = 0;
    while (relevant !== 0) {
        count += relevant & 1;
        relevant >>>= 1;
    }
    return count;
}

/**
 * Format `errorTable` as a human-readable summary via the caller-
 * supplied `translate(key: string, fallback: string) => string`
 * function. Bits are joined with ` + ` in ascending order, matching
 * the TC5 spec ("SOLDER + Bridging + TEXT"). Returns an empty string
 * when no bits are set.
 *
 * We do NOT pull `i18next` in here so this module stays framework-
 * neutral (usable from vitest, react-i18next, or a headless test).
 * Callers wire the resolver like:
 * ```ts
 * formatDefectBits(row.errorTable, (k, fb) => t(k, { defaultValue: fb }))
 * ```
 */
export function formatDefectBits(
    errorTable: number,
    translate: (key: string, fallback: string) => string,
): string {
    const bits = decodeDefectBits(errorTable);
    if (bits.length === 0) return "";
    const parts: string[] = [];
    for (const bit of bits) {
        const info = DEFECT_BITS[bit - 1];
        // The catalogue is dense (1..25 present) so info is always
        // defined; the null-check is defensive against future edits.
        if (!info) continue;
        parts.push(translate(`defect.bits.${info.key}`, info.defaultLabel));
    }
    return parts.join(" + ");
}
