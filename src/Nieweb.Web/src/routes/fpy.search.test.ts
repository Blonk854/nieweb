import { describe, expect, it } from "vitest";
import {
    validateFpySearch,
    toApiQuery,
    pickDefaultSourceId,
    type FpySearch,
} from "./fpy.search";
import type { SourceInfo } from "../api/sources";

describe("validateFpySearch", () => {
    it("returns an empty shape for empty input", () => {
        expect(validateFpySearch({})).toEqual<FpySearch>({
            sourceId: undefined,
            startUtc: undefined,
            endUtc: undefined,
            granularity: undefined,
            groupBy: undefined,
            machineIds: undefined,
            productIds: undefined,
            onlyLastInspection: undefined,
            skipExclusion: undefined,
            skipStatuses: undefined,
        });
    });

    it("parses and filters skipStatuses", () => {
        expect(validateFpySearch({ skipStatuses: "None,HeuristicMissing,bogus" }).skipStatuses).toEqual([
            "None",
            "HeuristicMissing",
        ]);
        expect(validateFpySearch({ skipStatuses: "nope" }).skipStatuses).toBeUndefined();
    });

    it("keeps valid enums and drops invalid ones", () => {
        const r = validateFpySearch({
            granularity: "Board",
            groupBy: "Product",
            skipExclusion: "Clean",
        });
        expect(r.granularity).toBe("Board");
        expect(r.groupBy).toBe("Product");
        expect(r.skipExclusion).toBe("Clean");
        expect(validateFpySearch({ granularity: "Nope", groupBy: "x" })).toMatchObject({
            granularity: undefined,
            groupBy: undefined,
        });
    });

    it("coerces onlyLastInspection", () => {
        expect(validateFpySearch({ onlyLastInspection: "false" }).onlyLastInspection).toBe(false);
        expect(validateFpySearch({ onlyLastInspection: true }).onlyLastInspection).toBe(true);
        expect(validateFpySearch({ onlyLastInspection: "nope" }).onlyLastInspection).toBeUndefined();
    });
});

describe("toApiQuery", () => {
    it("emits only set fields and omits default skip / lastInspection flags", () => {
        expect(
            toApiQuery({
                sourceId: "postreflow",
                startUtc: "2026-01-01T00:00:00Z",
                endUtc: "2026-01-02T00:00:00Z",
                granularity: "Board",
                groupBy: "Product",
            }),
        ).toEqual({
            sourceId: "postreflow",
            startUtc: "2026-01-01T00:00:00Z",
            endUtc: "2026-01-02T00:00:00Z",
            granularity: "Board",
            groupBy: "Product",
        });
        expect(toApiQuery({ skipExclusion: "Raw", onlyLastInspection: true })).toEqual({});
    });

    it("emits Clean skip, onlyLastInspection=false and joins id arrays", () => {
        expect(
            toApiQuery({
                machineIds: [1, 2],
                productIds: [3],
                onlyLastInspection: false,
                skipExclusion: "Clean",
                skipStatuses: ["ManualSkip"],
            }),
        ).toEqual({
            machineIds: "1,2",
            productIds: "3",
            onlyLastInspection: "false",
            skipExclusion: "Clean",
            skipStatuses: "ManualSkip",
        });
    });
});

describe("pickDefaultSourceId", () => {
    it("prefers the first available source", () => {
        const sources = [
            { id: "a", available: false },
            { id: "b", available: true },
        ] as SourceInfo[];
        expect(pickDefaultSourceId(sources)).toBe("b");
    });

    it("falls back to the first source when none are available", () => {
        expect(pickDefaultSourceId([{ id: "a", available: false }] as SourceInfo[])).toBe("a");
    });

    it("returns undefined for an empty list", () => {
        expect(pickDefaultSourceId([])).toBeUndefined();
    });
});
