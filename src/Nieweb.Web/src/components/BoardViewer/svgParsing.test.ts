import { describe, expect, it } from "vitest";
import {
    computeHighlightGeometry,
    parseComponentCentroids,
    DEFAULT_HIGHLIGHT_RADIUS,
    RADIUS_FACTOR,
} from "./svgParsing";

/**
 * Pure-function tests for the SVG parsing helpers used by
 * &lt;BoardViewer&gt;. These are DOM-agnostic (DOMParser only) so they
 * run fast under jsdom and pin the "reference" attribute as the
 * authoritative join key.
 */

const SAMPLE = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 213360 124460">
  <g id="components">
    <g class="component tested" sub-panel-index="1" reference="U1" transform="rotate(270 28435 97498)"/>
    <g class="component tested" sub-panel-index="3" reference="U1" transform="rotate(270 78473 47460)"/>
    <g class="component tested" sub-panel-index="1" reference="R7" transform="rotate(0 100 200)"/>
    <g class="component tested" sub-panel-index="2" reference="MISSING_TRANSFORM"/>
    <g class="component tested" reference="NO_INDEX" transform="rotate(0 1 2)"/>
  </g>
</svg>`;

describe("parseComponentCentroids", () => {
    it("returns a Map keyed by (sub-panel-index, reference) with cx/cy from rotate()", () => {
        const map = parseComponentCentroids(SAMPLE);
        expect(map.size).toBe(3);
        expect(map.get("1:U1")).toEqual({
            subpanelIndex: 1,
            reference: "U1",
            cx: 28435,
            cy: 97498,
        });
        expect(map.get("3:U1")).toEqual({
            subpanelIndex: 3,
            reference: "U1",
            cx: 78473,
            cy: 47460,
        });
        expect(map.get("1:R7")).toEqual({
            subpanelIndex: 1,
            reference: "R7",
            cx: 100,
            cy: 200,
        });
    });

    it("silently drops nodes that lack required attributes", () => {
        const map = parseComponentCentroids(SAMPLE);
        expect(map.has("2:MISSING_TRANSFORM")).toBe(false);
        expect([...map.values()].some((v) => v.reference === "NO_INDEX")).toBe(
            false,
        );
    });

    it("returns an empty map for empty / malformed input", () => {
        expect(parseComponentCentroids("").size).toBe(0);
        expect(parseComponentCentroids("<not-svg>").size).toBe(0);
    });

    it("accepts comma-separated rotate arguments too (SVG grammar)", () => {
        const svg = `<svg xmlns="http://www.w3.org/2000/svg"><g id="components"><g class="component" sub-panel-index="4" reference="C9" transform="rotate(90,1234.5,678.9)"/></g></svg>`;
        const map = parseComponentCentroids(svg);
        expect(map.get("4:C9")).toEqual({
            subpanelIndex: 4,
            reference: "C9",
            cx: 1234.5,
            cy: 678.9,
        });
    });
});

describe("computeHighlightGeometry", () => {
    /**
     * jsdom does not implement getBBox — the helper must fall back
     * to DEFAULT_HIGHLIGHT_RADIUS and drop highlights whose
     * (subpanel, reference) pair does not appear in the centroid map.
     */
    it("returns geometry with the fallback radius when getBBox is unavailable and drops unknown highlights", () => {
        const doc = new DOMParser().parseFromString(SAMPLE, "image/svg+xml");
        const svgEl = doc.documentElement as unknown as SVGSVGElement;
        const centroids = parseComponentCentroids(SAMPLE);
        const geom = computeHighlightGeometry(svgEl, centroids, [
            { subpanelIndex: 1, reference: "U1" },
            { subpanelIndex: 99, reference: "NOT_THERE" },
        ]);
        expect(geom).toHaveLength(1);
        expect(geom[0].cx).toBe(28435);
        expect(geom[0].cy).toBe(97498);
        expect(geom[0].radius).toBe(DEFAULT_HIGHLIGHT_RADIUS);
    });

    it("uses RADIUS_FACTOR × max(bbox.w, bbox.h) when getBBox is available", () => {
        const doc = new DOMParser().parseFromString(SAMPLE, "image/svg+xml");
        const svgEl = doc.documentElement as unknown as SVGSVGElement;
        // Monkey-patch getBBox on the specific component node so the
        // measurement path is exercised.
        const node = svgEl.querySelector<SVGGraphicsElement>(
            "g.component[sub-panel-index='1'][reference='U1']",
        )!;
        (node as unknown as { getBBox: () => DOMRect }).getBBox = () => ({
            x: 0,
            y: 0,
            width: 4000,
            height: 2500,
            top: 0,
            left: 0,
            right: 4000,
            bottom: 2500,
            toJSON: () => ({}),
        }) as DOMRect;
        const centroids = parseComponentCentroids(SAMPLE);
        const geom = computeHighlightGeometry(svgEl, centroids, [
            { subpanelIndex: 1, reference: "U1" },
        ]);
        expect(geom).toHaveLength(1);
        expect(geom[0].radius).toBeCloseTo(4000 * RADIUS_FACTOR, 5);
    });
});
