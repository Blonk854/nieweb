/**
 * Colour palette for the Pareto chart. Split out from
 * `ParetoChart.tsx` so the component file stays "components-only"
 * (which is what React Fast Refresh needs to hot-reload the chart
 * during `vite dev`).
 *
 *  - `vitalFew`   — bars whose cumulative-% is at or below the vital-few
 *                   threshold (the "vital few" from Juran's 80/20).
 *  - `trivialMany` — bars past the threshold.
 *  - `others`     — the collapsed Others bucket that only appears when
 *                   `TopN` overflowed.
 *  - `cumulative` — the running cumulative-percent line.
 */
export const PARETO_COLORS = {
    vitalFew: "#c92a2a",
    trivialMany: "#868e96",
    others: "#495057",
    cumulative: "#1c7ed6",
} as const;
