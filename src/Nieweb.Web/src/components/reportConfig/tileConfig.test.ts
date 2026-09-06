import { describe, expect, it } from "vitest";
import {
    PANEL_YIELD_TILE_DEFAULT,
    PARETO_TILE_DEFAULT,
    parseCommentTileConfig,
    parsePanelYieldTileConfig,
    parseParetoTileConfig,
    serializeCommentTileConfig,
    serializePanelYieldTileConfig,
    serializeParetoTileConfig,
} from "./tileConfig";
import { TILE_CONFIG_SCHEMAS } from "./tileConfigSchema";

describe("parseParetoTileConfig", () => {
    it("returns the canonical default for empty / null / undefined", () => {
        expect(parseParetoTileConfig(undefined)).toEqual(PARETO_TILE_DEFAULT);
        expect(parseParetoTileConfig(null)).toEqual(PARETO_TILE_DEFAULT);
        expect(parseParetoTileConfig("")).toEqual(PARETO_TILE_DEFAULT);
        expect(parseParetoTileConfig("   ")).toEqual(PARETO_TILE_DEFAULT);
    });

    it("falls back to defaults on malformed JSON or non-object JSON", () => {
        expect(parseParetoTileConfig("{ not json")).toEqual(PARETO_TILE_DEFAULT);
        expect(parseParetoTileConfig("[1,2,3]")).toEqual(PARETO_TILE_DEFAULT);
        expect(parseParetoTileConfig("42")).toEqual(PARETO_TILE_DEFAULT);
        expect(parseParetoTileConfig("null")).toEqual(PARETO_TILE_DEFAULT);
    });

    it("reads valid analytic knobs", () => {
        const cfg = parseParetoTileConfig(
            JSON.stringify({
                axis: "Product",
                numerator: "Aoi",
                opportunity: "Paste",
                weight: "Dpmo",
                topN: 5,
                vitalFewThreshold: 90,
            }),
        );
        expect(cfg).toEqual({
            axis: "Product",
            numerator: "Aoi",
            opportunity: "Paste",
            weight: "Dpmo",
            topN: 5,
            vitalFewThreshold: 90,
            filters: [],
        });
    });

    it("matches enum values case-insensitively and drops unknown values", () => {
        const cfg = parseParetoTileConfig(
            JSON.stringify({ axis: "product", numerator: "BOGUS" }),
        );
        expect(cfg.axis).toBe("Product");
        expect(cfg.numerator).toBe(PARETO_TILE_DEFAULT.numerator);
    });

    it("treats topN of 0 / negative / null as no cap", () => {
        expect(parseParetoTileConfig(JSON.stringify({ topN: 0 })).topN).toBeUndefined();
        expect(parseParetoTileConfig(JSON.stringify({ topN: -3 })).topN).toBeUndefined();
        expect(parseParetoTileConfig(JSON.stringify({ topN: null })).topN).toBeUndefined();
    });

    it("preserves an unknown extra key by ignoring it (render is total)", () => {
        const cfg = parseParetoTileConfig(
            JSON.stringify({ axis: "Jedec", somethingElse: true }),
        );
        expect(cfg.axis).toBe("Jedec");
        expect(cfg).not.toHaveProperty("somethingElse");
    });

    it("retains Subpanel as a selectable Pareto tile axis", () => {
        const cfg = parseParetoTileConfig(JSON.stringify({ axis: "Subpanel" }));
        expect(cfg.axis).toBe("Subpanel");
    });
});

describe("TILE_CONFIG_SCHEMAS pareto axis options", () => {
    it("includes Subpanel and excludes Day and Shift", () => {
        const axisField = TILE_CONFIG_SCHEMAS.pareto?.find((f) => f.key === "axis");
        expect(axisField?.kind).toBe("select");
        if (axisField?.kind !== "select") {
            throw new Error("expected pareto axis field to be a select");
        }
        const values = axisField.options.map((o) => o.value);
        expect(values).toContain("Subpanel");
        expect(values).toContain("Defect");
        expect(values).not.toContain("Day");
        expect(values).not.toContain("Shift");
    });
});

describe("parsePanelYieldTileConfig", () => {
    it("defaults onlyLastInspection to true", () => {
        expect(parsePanelYieldTileConfig(undefined)).toEqual(PANEL_YIELD_TILE_DEFAULT);
        expect(parsePanelYieldTileConfig("{}").onlyLastInspection).toBe(true);
    });

    it("reads a boolean or stringy boolean", () => {
        expect(
            parsePanelYieldTileConfig(JSON.stringify({ onlyLastInspection: false }))
                .onlyLastInspection,
        ).toBe(false);
        expect(
            parsePanelYieldTileConfig('{"onlyLastInspection":"false"}').onlyLastInspection,
        ).toBe(false);
    });
});

describe("parseCommentTileConfig", () => {
    it("reads the markdown body and defaults to empty", () => {
        expect(parseCommentTileConfig(undefined).markdown).toBe("");
        expect(parseCommentTileConfig('{"markdown":"# Hi"}').markdown).toBe("# Hi");
    });
});

describe("round-trip serialise <-> parse", () => {
    it("pareto", () => {
        const cfg = {
            axis: "AoiMachine" as const,
            numerator: "Dummy" as const,
            opportunity: "Components" as const,
            weight: "Ppm" as const,
            topN: 15,
            vitalFewThreshold: 75,
            filters: [],
        };
        expect(parseParetoTileConfig(serializeParetoTileConfig(cfg))).toEqual(cfg);
    });

    it("pareto with per-entity filters round-trips", () => {
        const cfg = {
            ...PARETO_TILE_DEFAULT,
            filters: [
                { field: "PartNumber" as const, operator: "NotLike" as const, values: ["PN-B"] },
                { field: "Package" as const, operator: "In" as const, values: ["BGA256", "QFN44"] },
            ],
        };
        expect(parseParetoTileConfig(serializeParetoTileConfig(cfg))).toEqual(cfg);
    });

    it("pareto without a topN cap round-trips to undefined", () => {
        const cfg = { ...PARETO_TILE_DEFAULT, topN: undefined };
        const round = parseParetoTileConfig(serializeParetoTileConfig(cfg));
        expect(round.topN).toBeUndefined();
    });

    it("panelYield", () => {
        const cfg = { onlyLastInspection: false, filters: [] };
        expect(parsePanelYieldTileConfig(serializePanelYieldTileConfig(cfg))).toEqual(cfg);
    });

    it("comment", () => {
        const cfg = { markdown: "line 1\n\nline 2" };
        expect(parseCommentTileConfig(serializeCommentTileConfig(cfg))).toEqual(cfg);
    });
});
