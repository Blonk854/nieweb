import { describe, expect, it } from "vitest";
import {
    countDefectBits,
    decodeDefectBits,
    DEFECT_BITS,
    DEFECT_BITS_1_TO_25_MASK,
    formatDefectBits,
} from "./defectBits";

describe("defectBits catalogue", () => {
    it("has 25 entries covering bits 1..25 in order", () => {
        expect(DEFECT_BITS.length).toBe(25);
        DEFECT_BITS.forEach((info, idx) => {
            expect(info.bit).toBe(idx + 1);
            expect(info.key).toMatch(/^[a-z][A-Za-z0-9]+$/);
            expect(info.defaultLabel.length).toBeGreaterThan(0);
        });
    });

    it("marks the historically-obsolete bits (6, 12..18) as obsolete", () => {
        const obsoleteBits = DEFECT_BITS.filter((b) => b.obsolete).map((b) => b.bit);
        expect(obsoleteBits.sort((a, b) => a - b)).toEqual([6, 12, 13, 14, 15, 16, 17, 18]);
    });

    it("Bits1To25 mask equals 2^25 - 1", () => {
        expect(DEFECT_BITS_1_TO_25_MASK).toBe(0x01_FF_FF_FF);
        expect(DEFECT_BITS_1_TO_25_MASK).toBe((1 << 25) - 1);
    });
});

describe("decodeDefectBits", () => {
    it("returns [] for 0", () => {
        expect(decodeDefectBits(0)).toEqual([]);
    });

    it("returns [] for non-finite input", () => {
        expect(decodeDefectBits(NaN)).toEqual([]);
        expect(decodeDefectBits(Infinity)).toEqual([]);
    });

    it("decodes single-bit values", () => {
        expect(decodeDefectBits(1)).toEqual([1]); // bit 1
        expect(decodeDefectBits(8)).toEqual([4]); // bit 4 (1 << 3)
        expect(decodeDefectBits(1 << 24)).toEqual([25]); // bit 25
    });

    it("decodes multi-bit values in ascending bit order", () => {
        // bit 1 + bit 4 + bit 20 = 1 + 8 + 524288 = 524297
        const composite = (1 << 0) | (1 << 3) | (1 << 19);
        expect(decodeDefectBits(composite)).toEqual([1, 4, 20]);
    });

    it("ignores upper-bit classification metadata above bit 25", () => {
        // bit 26 is (1 << 25) — should NOT be reported.
        expect(decodeDefectBits(1 << 25)).toEqual([]);
        // bit 3 set alongside bit 26 classification bit → only bit 3.
        expect(decodeDefectBits((1 << 25) | 4)).toEqual([3]);
    });
});

describe("countDefectBits", () => {
    it("returns 0 for 0", () => {
        expect(countDefectBits(0)).toBe(0);
    });

    it("counts each set bit in 1..25", () => {
        expect(countDefectBits(1)).toBe(1);
        expect(countDefectBits((1 << 0) | (1 << 3) | (1 << 19))).toBe(3);
        expect(countDefectBits(DEFECT_BITS_1_TO_25_MASK)).toBe(25);
    });

    it("ignores upper-bit classification metadata", () => {
        expect(countDefectBits((1 << 25) | 1)).toBe(1);
    });
});

describe("formatDefectBits", () => {
    // Sanity-check the join order + fallback pathway. Uses the
    // English defaultLabel via a fallthrough translate function so
    // the test is independent of i18n runtime state.
    const useFallback = (_key: string, fallback: string) => fallback;

    it("returns empty string for 0", () => {
        expect(formatDefectBits(0, useFallback)).toBe("");
    });

    it("joins multiple defects with ' + ' in bit-ascending order", () => {
        // ObjectMissing (bit 1) + SolderBridgeDefect (bit 4) + TiltError (bit 20)
        const composite = (1 << 0) | (1 << 3) | (1 << 19);
        const result = formatDefectBits(composite, useFallback);
        expect(result).toBe(
            "Object missing + Solder bridge defect + Tilt error",
        );
    });

    it("uses the translate function when it provides a localised string", () => {
        const translate = (key: string, fallback: string) =>
            key === "defect.bits.objectMissing" ? "COMPOSANT MANQUANT" : fallback;
        expect(formatDefectBits(1, translate)).toBe("COMPOSANT MANQUANT");
    });

    it("ignores upper-bit classification metadata", () => {
        expect(formatDefectBits(1 << 25, useFallback)).toBe("");
    });
});
