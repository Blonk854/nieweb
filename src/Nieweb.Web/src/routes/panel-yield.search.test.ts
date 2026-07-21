import { describe, expect, it } from "vitest";
import {
    pickDefaultSourceId,
    toApiQuery,
    validatePanelYieldSearch,
    type PanelYieldSearch,
} from "./panel-yield.search";
import type { SourceInfo } from "../api/sources";

describe("validatePanelYieldSearch", () => {
    it("returns an empty object for empty input", () => {
        expect(validatePanelYieldSearch({})).toEqual<PanelYieldSearch>({
            sourceId: undefined,
            startUtc: undefined,
            endUtc: undefined,
            machineIds: undefined,
            productIds: undefined,
            recipeIds: undefined,
            onlyLastInspection: undefined,
        });
    });

    it("parses comma-separated id lists", () => {
        const s = validatePanelYieldSearch({
            sourceId: "postreflow",
            startUtc: "2026-06-01T00:00:00Z",
            endUtc: "2026-07-01T00:00:00Z",
            machineIds: "1,2,3",
            productIds: "7",
            recipeIds: "42,7",
            onlyLastInspection: "true",
        });
        expect(s.sourceId).toBe("postreflow");
        expect(s.startUtc).toBe("2026-06-01T00:00:00Z");
        expect(s.endUtc).toBe("2026-07-01T00:00:00Z");
        expect(s.machineIds).toEqual([1, 2, 3]);
        expect(s.productIds).toEqual([7]);
        expect(s.recipeIds).toEqual([42, 7]);
        expect(s.onlyLastInspection).toBe(true);
    });

    it("parses array id lists (multi-value URL params)", () => {
        const s = validatePanelYieldSearch({
            machineIds: ["1", "2"],
            productIds: [3, 4],
        });
        expect(s.machineIds).toEqual([1, 2]);
        expect(s.productIds).toEqual([3, 4]);
    });

    it("drops non-integer id list entries", () => {
        const s = validatePanelYieldSearch({
            machineIds: "1,foo,2.5,3",
        });
        // "foo" and 2.5 dropped; 1 and 3 kept.
        expect(s.machineIds).toEqual([1, 3]);
    });

    it("drops empty strings", () => {
        const s = validatePanelYieldSearch({
            sourceId: "   ",
            machineIds: "",
        });
        expect(s.sourceId).toBeUndefined();
        expect(s.machineIds).toBeUndefined();
    });

    it("parses boolean onlyLastInspection from string form", () => {
        expect(validatePanelYieldSearch({ onlyLastInspection: "false" }).onlyLastInspection).toBe(false);
        expect(validatePanelYieldSearch({ onlyLastInspection: "true" }).onlyLastInspection).toBe(true);
        expect(validatePanelYieldSearch({ onlyLastInspection: "" }).onlyLastInspection).toBeUndefined();
        expect(validatePanelYieldSearch({ onlyLastInspection: true }).onlyLastInspection).toBe(true);
    });
});

describe("toApiQuery", () => {
    it("omits empty fields", () => {
        expect(toApiQuery({})).toEqual({});
    });

    it("joins id arrays with commas", () => {
        expect(toApiQuery({ machineIds: [1, 2, 3] })).toEqual({
            machineIds: "1,2,3",
        });
    });

    it("emits the full shape when populated", () => {
        expect(
            toApiQuery({
                sourceId: "postreflow",
                startUtc: "2026-06-01T00:00:00.000Z",
                endUtc: "2026-07-01T00:00:00.000Z",
                machineIds: [1, 2],
                productIds: [7],
                recipeIds: [42],
                onlyLastInspection: false,
            }),
        ).toEqual({
            sourceId: "postreflow",
            startUtc: "2026-06-01T00:00:00.000Z",
            endUtc: "2026-07-01T00:00:00.000Z",
            machineIds: "1,2",
            productIds: "7",
            recipeIds: "42",
            onlyLastInspection: "false",
        });
    });
});

describe("pickDefaultSourceId", () => {
    const src = (id: string, available: boolean): SourceInfo => ({
        id,
        displayName: id,
        schemaVersion: "5.0",
        capabilities: [],
        latestPanelUtc: null,
        available,
    });

    it("returns undefined for empty list", () => {
        expect(pickDefaultSourceId([])).toBeUndefined();
    });

    it("prefers the first available source", () => {
        expect(
            pickDefaultSourceId([src("a", false), src("b", true), src("c", true)]),
        ).toBe("b");
    });

    it("falls back to the first source when none are available", () => {
        expect(pickDefaultSourceId([src("a", false), src("b", false)])).toBe("a");
    });
});
