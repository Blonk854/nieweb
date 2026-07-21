import { describe, expect, it } from "vitest";
import {
    bandFor,
    colorForFpy,
    DEFAULT_FPY_THRESHOLDS,
    FPY_BAND_COLORS,
} from "./fpyThresholds";

describe("bandFor", () => {
    it("classifies well above green as green", () => {
        expect(bandFor(100)).toBe("green");
        expect(bandFor(99.99)).toBe("green");
    });

    it("treats the green threshold itself as green (inclusive lower bound)", () => {
        expect(bandFor(DEFAULT_FPY_THRESHOLDS.green)).toBe("green");
    });

    it("classifies the amber band (>= amber, < green)", () => {
        expect(bandFor(99.4999)).toBe("amber");
        expect(bandFor(98.5)).toBe("amber");
        expect(bandFor(DEFAULT_FPY_THRESHOLDS.amber)).toBe("amber");
    });

    it("classifies below the amber threshold as red", () => {
        expect(bandFor(97.9999)).toBe("red");
        expect(bandFor(0)).toBe("red");
    });

    it("treats non-finite input as red (defensive, never colours a broken measurement green)", () => {
        expect(bandFor(Number.NaN)).toBe("red");
        expect(bandFor(Number.POSITIVE_INFINITY)).toBe("red");
    });

    it("respects custom thresholds", () => {
        const custom = { green: 95, amber: 90 };
        expect(bandFor(95, custom)).toBe("green");
        expect(bandFor(94.99, custom)).toBe("amber");
        expect(bandFor(89.99, custom)).toBe("red");
    });
});

describe("colorForFpy", () => {
    it("returns the palette hex for the corresponding band", () => {
        expect(colorForFpy(99.9)).toBe(FPY_BAND_COLORS.green);
        expect(colorForFpy(99.0)).toBe(FPY_BAND_COLORS.amber);
        expect(colorForFpy(50)).toBe(FPY_BAND_COLORS.red);
    });
});
