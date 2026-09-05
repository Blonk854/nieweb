import { describe, expect, it } from "vitest";
import { paretoTileQueryKey } from "./ParetoTile";
import type { CanvasFilters } from "../FilterContext";

const base: CanvasFilters = {
    sourceId: "postreflow",
    startUtc: "2026-01-01T00:00:00Z",
    endUtc: "2026-01-02T00:00:00Z",
    machineIds: [1],
    productIds: [2],
};

describe("paretoTileQueryKey", () => {
    it("includes the raw configJson so filter and threshold changes bust the cache", () => {
        const a = paretoTileQueryKey(base, '{"axis":"Defect","vitalFewThreshold":80}');
        const b = paretoTileQueryKey(base, '{"axis":"Defect","vitalFewThreshold":60}');
        const c = paretoTileQueryKey(
            base,
            '{"axis":"Defect","filters":[{"field":"PartNumber","operator":"NotLike","values":["x"]}]}',
        );
        expect(a).not.toEqual(b);
        expect(a).not.toEqual(c);
        expect(a[0]).toBe("canvas");
        expect(a[1]).toBe("pareto");
        expect(a[7]).toBe('{"axis":"Defect","vitalFewThreshold":80}');
    });
});
