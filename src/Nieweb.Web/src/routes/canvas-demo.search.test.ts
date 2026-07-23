import { describe, expect, it } from "vitest";
import {
    toApiQuery,
    validateCanvasDemoSearch,
} from "./canvas-demo.search";

describe("validateCanvasDemoSearch", () => {
    it("returns an empty search when the URL has no known keys", () => {
        expect(validateCanvasDemoSearch({})).toEqual({
            sourceId: undefined,
            startUtc: undefined,
            endUtc: undefined,
            machineIds: undefined,
            productIds: undefined,
            tiles: undefined,
        });
    });

    it("parses source, window and narrowing filters from string inputs", () => {
        expect(
            validateCanvasDemoSearch({
                sourceId: "postreflow",
                startUtc: "2026-07-01T00:00:00Z",
                endUtc: "2026-07-08T00:00:00Z",
                machineIds: "1,2,3",
                productIds: [10, 20],
            }),
        ).toEqual({
            sourceId: "postreflow",
            startUtc: "2026-07-01T00:00:00Z",
            endUtc: "2026-07-08T00:00:00Z",
            machineIds: [1, 2, 3],
            productIds: [10, 20],
            tiles: undefined,
        });
    });

    it("parses the tile list from a comma-separated string and drops unknown types", () => {
        const search = validateCanvasDemoSearch({
            tiles: "panelYield,unknown,pareto,pareto",
        });
        expect(search.tiles).toEqual(["panelYield", "pareto", "pareto"]);
    });

    it("parses the tile list from an array of strings", () => {
        const search = validateCanvasDemoSearch({
            tiles: ["pareto", "panelYield"],
        });
        expect(search.tiles).toEqual(["pareto", "panelYield"]);
    });

    it("returns undefined tiles when the input contains only unknown types", () => {
        const search = validateCanvasDemoSearch({ tiles: "foo,bar" });
        expect(search.tiles).toBeUndefined();
    });

    it("drops empty strings and out-of-range numbers instead of throwing", () => {
        expect(
            validateCanvasDemoSearch({
                sourceId: "   ",
                machineIds: "1, ,not-a-number,3",
            }),
        ).toEqual({
            sourceId: undefined,
            startUtc: undefined,
            endUtc: undefined,
            machineIds: [1, 3],
            productIds: undefined,
            tiles: undefined,
        });
    });
});

describe("toApiQuery", () => {
    it("emits only the populated keys", () => {
        expect(toApiQuery({})).toEqual({});
        expect(toApiQuery({ sourceId: "s", startUtc: "2026-07-01T00:00:00Z" }))
            .toEqual({
                sourceId: "s",
                startUtc: "2026-07-01T00:00:00Z",
            });
    });

    it("joins array fields with commas", () => {
        expect(
            toApiQuery({
                machineIds: [1, 2],
                productIds: [10],
                tiles: ["panelYield", "pareto"],
            }),
        ).toEqual({
            machineIds: "1,2",
            productIds: "10",
            tiles: "panelYield,pareto",
        });
    });

    it("round-trips through validateCanvasDemoSearch preserving all fields", () => {
        const source = {
            sourceId: "prereflow",
            startUtc: "2026-07-01T00:00:00Z",
            endUtc: "2026-07-08T00:00:00Z",
            machineIds: [1, 2, 3],
            productIds: [4],
            tiles: ["panelYield" as const, "pareto" as const, "panelYield" as const],
        };
        const q = toApiQuery(source);
        expect(validateCanvasDemoSearch(q)).toEqual(source);
    });
});
