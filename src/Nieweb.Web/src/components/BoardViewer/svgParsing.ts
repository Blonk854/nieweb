/**
 * Pure helpers for parsing Sigmalink-style panel SVGs into
 * component centroids and for computing per-highlight geometry.
 * Kept side-effect-free so we can unit-test them independently of
 * the DOM.
 *
 * <h3>SVG structure we rely on</h3>
 * <pre>
 *   &lt;g id="components"&gt;
 *     &lt;g class="component tested …"
 *        sub-panel-index="1"
 *        reference="U1"
 *        topo="U1"
 *        transform="rotate(270 28435 97498)"&gt;
 *       …
 *     &lt;/g&gt;
 *   &lt;/g&gt;
 * </pre>
 *
 * The centroid is the last two arguments of the
 * <code>rotate(θ cx cy)</code> transform on each
 * <code>&lt;g class="component …"&gt;</code>.
 */

export type ComponentCentroid = {
    subpanelIndex: number;
    reference: string;
    cx: number;
    cy: number;
};

export type HighlightGeometry = {
    highlight: { subpanelIndex: number; reference: string };
    cx: number;
    cy: number;
    radius: number;
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
    // parseerror shows up as <parsererror> inside the doc
    const err = doc.querySelector("parsererror");
    if (err) return out;
    const nodes = doc.querySelectorAll("g#components > g.component, g.component");
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

/** Default fallback radius (SVG user units) if getBBox() is unusable. */
export const DEFAULT_HIGHLIGHT_RADIUS = 1500;

/** Radius multiplier applied to <code>max(bbox.width, bbox.height)</code>. */
export const RADIUS_FACTOR = 0.6;

/**
 * For each requested highlight, resolve the matching centroid and
 * compute a circle radius from the live <code>getBBox()</code> of
 * the corresponding component node. Highlights that don't map to a
 * known component are dropped from the result.
 *
 * The <code>svgEl</code> parameter is required because
 * <code>getBBox()</code> only works on elements attached to a
 * rendered SVG document — the parsed <code>DOMParser</code> tree
 * used in {@link parseComponentCentroids} won't have layout.
 */
export function computeHighlightGeometry(
    svgEl: SVGSVGElement,
    centroids: ReadonlyMap<string, ComponentCentroid>,
    highlights: readonly { subpanelIndex: number; reference: string }[],
): HighlightGeometry[] {
    const out: HighlightGeometry[] = [];
    for (const h of highlights) {
        const key = `${h.subpanelIndex}:${h.reference}`;
        const centroid = centroids.get(key);
        if (!centroid) continue;
        // getBBox is expensive-ish so scope the querySelector strictly
        // to the same (sub-panel-index, reference) pair.
        const node = svgEl.querySelector<SVGGraphicsElement>(
            `g.component[sub-panel-index="${h.subpanelIndex}"][reference="${cssEscape(h.reference)}"]`,
        );
        let radius = DEFAULT_HIGHLIGHT_RADIUS;
        if (node && typeof node.getBBox === "function") {
            try {
                const box = node.getBBox();
                const size = Math.max(box.width, box.height);
                if (Number.isFinite(size) && size > 0) {
                    radius = size * RADIUS_FACTOR;
                }
            } catch {
                // jsdom throws NotSupportedError — fall back to default.
            }
        }
        out.push({
            highlight: { subpanelIndex: h.subpanelIndex, reference: h.reference },
            cx: centroid.cx,
            cy: centroid.cy,
            radius,
        });
    }
    return out;
}

/**
 * Minimal CSS.escape polyfill for jsdom test envs that lack it.
 * Only escapes characters that would break an attribute selector.
 */
function cssEscape(value: string): string {
    if (typeof (globalThis as { CSS?: { escape?: (v: string) => string } }).CSS?.escape === "function") {
        return (globalThis as { CSS: { escape: (v: string) => string } }).CSS.escape(value);
    }
    return value.replace(/["\\]/g, "\\$&");
}
