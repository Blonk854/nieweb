/**
 * Pure helpers for parsing Sigmalink-style panel SVGs.
 *
 * Two artefacts are needed by the {@link BoardViewer} runtime:
 *
 * <ol>
 *   <li><strong>Component centroids</strong> — extracted from the
 *       <code>transform="rotate(θ cx cy)"</code> attribute on each
 *       <code>&lt;g class="component" sub-panel-index="…"
 *       reference="…"&gt;</code>. Used to place the crosshair at the
 *       primary highlight and (as a fallback) to nudge users toward
 *       the failing part on inspected-but-empty component groups.</li>
 *   <li><strong>Sub-panel outlines</strong> — extracted from the
 *       <code>&lt;path class="border" d="…"&gt;</code> child of each
 *       <code>&lt;g class="sub-panel" index="N"&gt;</code>. Cloned
 *       into the overlay with a pulsing red stroke so the user sees
 *       exactly which sub-panels failed even at low zoom.</li>
 * </ol>
 *
 * Kept side-effect-free so we can unit-test them independently of
 * the DOM.
 */

export type ComponentCentroid = {
    subpanelIndex: number;
    reference: string;
    cx: number;
    cy: number;
};

/**
 * One sub-panel outline parsed from the panel SVG. The
 * <code>pathD</code> is the exact <code>d</code> attribute of the
 * <code>&lt;path class="border"&gt;</code> child of
 * <code>&lt;g class="sub-panel" index="N"&gt;</code> — kept as an
 * opaque string because we just clone it into the overlay layer.
 */
export type SubpanelOutline = {
    index: number;
    pathD: string;
};

// `rotate(θ cx cy)` — θ can be signed float; commas or whitespace
// allowed between args (SVG allows either).
const ROTATE_RE = /rotate\(\s*(-?\d+(?:\.\d+)?)[\s,]+(-?\d+(?:\.\d+)?)[\s,]+(-?\d+(?:\.\d+)?)\s*\)/;

/**
 * Parse the injected SVG text and return every
 * <code>&lt;g class="component …"&gt;</code> keyed by
 * <code>"subpanelIndex:reference"</code>. Uses the browser's
 * <code>DOMParser</code> so it works in vitest (jsdom) too.
 *
 * Nodes that lack any of the required attributes or a matching
 * rotate() transform are silently skipped — we prefer partial
 * highlights over an exception because the SVG comes from an
 * external tool chain and evolves over time.
 */
export function parseComponentCentroids(
    svgText: string,
): Map<string, ComponentCentroid> {
    const out = new Map<string, ComponentCentroid>();
    if (!svgText || typeof DOMParser === "undefined") return out;
    const doc = new DOMParser().parseFromString(svgText, "image/svg+xml");
    if (doc.querySelector("parsererror")) return out;
    const nodes = doc.querySelectorAll("g#components g.component, g.component");
    nodes.forEach((n) => {
        const subpanelAttr = n.getAttribute("sub-panel-index");
        const reference = n.getAttribute("reference");
        const transform = n.getAttribute("transform");
        if (!subpanelAttr || !reference || !transform) return;
        const m = ROTATE_RE.exec(transform);
        if (!m) return;
        const subpanelIndex = Number.parseInt(subpanelAttr, 10);
        const cx = Number.parseFloat(m[2]);
        const cy = Number.parseFloat(m[3]);
        if (
            !Number.isFinite(subpanelIndex) ||
            !Number.isFinite(cx) ||
            !Number.isFinite(cy)
        ) {
            return;
        }
        out.set(`${subpanelIndex}:${reference}`, {
            subpanelIndex,
            reference,
            cx,
            cy,
        });
    });
    return out;
}

/**
 * Parse the <code>&lt;g id="sub-panels"&gt;</code> block into a
 * <code>Map&lt;index, SubpanelOutline&gt;</code>. Each outline
 * carries the raw <code>d</code> attribute of the sub-panel's
 * border path so callers can clone it into an overlay layer without
 * re-computing geometry.
 *
 * If the SVG has no sub-panels group, or a specific sub-panel lacks
 * a <code>&lt;path class="border"&gt;</code>, that entry is dropped
 * — the BoardViewer will simply not render a pulse ring for it.
 */
export function parseSubpanelOutlines(
    svgText: string,
): Map<number, SubpanelOutline> {
    const out = new Map<number, SubpanelOutline>();
    if (!svgText || typeof DOMParser === "undefined") return out;
    const doc = new DOMParser().parseFromString(svgText, "image/svg+xml");
    if (doc.querySelector("parsererror")) return out;
    const groups = doc.querySelectorAll("g#sub-panels g.sub-panel, g.sub-panel");
    groups.forEach((g) => {
        const idxAttr = g.getAttribute("index");
        if (!idxAttr) return;
        const index = Number.parseInt(idxAttr, 10);
        if (!Number.isFinite(index)) return;
        // Prefer the .border path so we get the outline (no fill).
        // Fall back to .background so the pulse still shows even on
        // sub-panels that only carry a background path.
        const border = g.querySelector("path.border") ?? g.querySelector("path.background");
        const pathD = border?.getAttribute("d");
        if (!pathD) return;
        out.set(index, { index, pathD });
    });
    return out;
}

/**
 * Minimal CSS.escape shim for jsdom test envs that lack it. Only
 * escapes characters that would break an attribute selector.
 */
export function cssEscape(value: string): string {
    if (typeof (globalThis as { CSS?: { escape?: (v: string) => string } }).CSS?.escape === "function") {
        return (globalThis as { CSS: { escape: (v: string) => string } }).CSS.escape(value);
    }
    return value.replace(/["\\]/g, "\\$&");
}
