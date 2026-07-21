/**
 * FPY (First-Pass Yield) threshold model used by the panel-yield bar
 * chart and any future FPY visualisation.
 *
 * A band answers the question "how good is this machine right now?":
 *
 *  - green  : fpyPercent >= green
 *  - amber  : amber <= fpyPercent < green
 *  - red    : fpyPercent < amber
 *
 * The default thresholds match the F5 acceptance criteria in
 * docs/phase-1-mvp.md (green >= 99.5, amber 98.0-99.5, red < 98.0).
 * These are per-site tunable and should eventually come from a user or
 * customer preference; keeping them as a single typed value now means
 * F8 (saved views) can override them without touching the chart.
 */

/** A colour band assigned to a single FPY value. */
export type FpyBand = "green" | "amber" | "red";

/** Threshold configuration in FPY percent (0-100). */
export type FpyThresholds = {
    /** FPY >= this value is coloured green. */
    green: number;
    /** FPY >= this (and < green) is coloured amber. Everything below is red. */
    amber: number;
};

/** Default site thresholds per docs/phase-1-mvp.md §7.5 F5. */
export const DEFAULT_FPY_THRESHOLDS: FpyThresholds = Object.freeze({
    green: 99.5,
    amber: 98.0,
});

/** Palette applied to each band. Mantine-neutral hexes so the chart works in both light/dark. */
export const FPY_BAND_COLORS: Readonly<Record<FpyBand, string>> = Object.freeze({
    green: "#2f9e44",
    amber: "#f08c00",
    red: "#e03131",
});

/**
 * Classify a single FPY percent into a band. `NaN` and non-finite
 * values fall through to `red` on purpose - a broken measurement is
 * never "good".
 */
export function bandFor(fpyPercent: number, thresholds: FpyThresholds = DEFAULT_FPY_THRESHOLDS): FpyBand {
    if (!Number.isFinite(fpyPercent)) {
        return "red";
    }
    if (fpyPercent >= thresholds.green) {
        return "green";
    }
    if (fpyPercent >= thresholds.amber) {
        return "amber";
    }
    return "red";
}

/**
 * Convenience: colour hex for a value. Equivalent to
 * `FPY_BAND_COLORS[bandFor(v, t)]` but written to keep call sites terse.
 */
export function colorForFpy(fpyPercent: number, thresholds: FpyThresholds = DEFAULT_FPY_THRESHOLDS): string {
    return FPY_BAND_COLORS[bandFor(fpyPercent, thresholds)];
}
