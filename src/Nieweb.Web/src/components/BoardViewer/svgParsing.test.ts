import { describe, expect, it } from "vitest";
import {
    parseComponentCentroids,
    parseSubpanelOutlines,
} from "./svgParsing";

/**
 * Pure-function tests for the SVG parsing helpers used by
 * &lt;BoardViewer&gt;. DOM-agnostic (DOMParser only) so they run
 * fast under jsdom.
 */

const SAMPLE_COMPONENTS = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 213360 124460">
  <g id="components">
    <g class="component tested" sub-panel-index="1" reference="U1" transform="rotate(270 28435 97498)"/>
    <g class="component tested" sub-panel-index="3" reference="U1" transform="rotate(270 78473 47460)"/>
    <g class="component tested" sub-panel-index="1" reference="R7" transform="rotate(0 100 200)"/>
    <g class="component tested" sub-panel-index="2" reference="MISSING_TRANSFORM"/>
    <g class="component tested" reference="NO_INDEX" transform="rotate(0 1 2)"/>
  </g>
</svg>`;

const SAMPLE_SUBPANELS = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200000 200000">
  <g id="sub-panels">
    <g class="sub-panel" index="1">
      <path class="background" d="M0 0 L100 0 L100 100 L0 100 Z"/>
      <path class="border" d="M0 0 L100 0 L100 100 L0 100 Z"/>
    </g>
    <g class="sub-panel" index="2">
      <path class="border" d="M200 200 L300 200 L300 300 L200 300 Z"/>
    </g>
    <g class="sub-panel" index="3">
      <!-- No .border and no .background — must be dropped. -->
    </g>
    <g class="sub-panel">
      <!-- No index attribute — must be dropped. -->
      <path class="border" d="M0 0 L1 1"/>
    </g>
  </g>
</svg>`;

describe("parseComponentCentroids", () => {
    it("returns a Map keyed by (sub-panel-index, reference) with cx/cy from rotate()", () => {
        const map = parseComponentCentroids(SAMPLE_COMPONENTS);
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
        const map = parseComponentCentroids(SAMPLE_COMPONENTS);
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

describe("parseSubpanelOutlines", () => {
    it("returns a Map keyed by index carrying the border 'd' attribute", () => {
        const map = parseSubpanelOutlines(SAMPLE_SUBPANELS);
        expect(map.size).toBe(2);
        expect(map.get(1)).toEqual({
            index: 1,
            pathD: "M0 0 L100 0 L100 100 L0 100 Z",
        });
        expect(map.get(2)).toEqual({
            index: 2,
            pathD: "M200 200 L300 200 L300 300 L200 300 Z",
        });
    });

    it("drops sub-panels without a usable path and those missing an index", () => {
        const map = parseSubpanelOutlines(SAMPLE_SUBPANELS);
        expect(map.has(3)).toBe(false);
    });

    it("returns an empty map for empty / malformed input", () => {
        expect(parseSubpanelOutlines("").size).toBe(0);
        expect(parseSubpanelOutlines("<not-svg>").size).toBe(0);
    });
});
