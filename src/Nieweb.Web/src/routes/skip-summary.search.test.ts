import { describe, expect, it } from "vitest";
import {
    validateSkipSummarySearch,
    toApiQuery,
    pickDefaultSourceId,
    type SkipSummarySearch,
} from "./skip-summary.search";
import type { SourceInfo } from "../api/sources";

describe("validateSkipSummarySearch", () => {
    it("returns an empty shape for empty input", () => {
        expect(validateSkipSummarySearch({})).toEqual<SkipSummarySearch>({
            sourceId: undefined,
            startUtc: undefined,
            endUtc: undefined,
            machineIds: undefined,
            productIds: undefined,
            onlyLastInspection: undefined,
        });
    });

    it("parses comma-separated machineIds and productIds", () => {
        const r = validateSkipSummarySearch({ machineIds: "1,2,3", productIds: "10" });
        expect(r.machineIds).toEqual([1, 2, 3]);
        expect(r.productIds).toEqual([10]);
    });

    it("coerces onlyLastInspection from string / boolean", () => {
        expect(validateSkipSummarySearch({ onlyLastInspection: "false" }).onlyLastInspection).toBe(false);
        expect(validateSkipSummarySearch({ onlyLastInspection: true }).onlyLastInspection).toBe(true);
        expect(validateSkipSummarySearch({ onlyLastInspection: "nope" }).onlyLastInspection).toBeUndefined();
    });
});

describe("toApiQuery", () => {
    it("emits only the set fields", () => {
        expect(
            toApiQuery({
                sourceId: "postreflow",
                startUtc: "2026-01-01T00:00:00Z",
                endUtc: "2026-01-02T00:00:00Z",
            }),
        ).toEqual({
            sourceId: "postreflow",
            startUtc: "2026-01-01T00:00:00Z",
            endUtc: "2026-01-02T00:00:00Z",
        });
    });

    it("joins id arrays and only emits onlyLastInspection when false", () => {
        expect(toApiQuery({ machineIds: [1, 2], productIds: [3] })).toEqual({
            machineIds: "1,2",
            productIds: "3",
        });
        expect(toApiQuery({ onlyLastInspection: true })).toEqual({});
        expect(toApiQuery({ onlyLastInspection: false })).toEqual({ onlyLastInspection: "false" });
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
        const sources = [{ id: "a", available: false }] as SourceInfo[];
        expect(pickDefaultSourceId(sources)).toBe("a");
    });

    it("returns undefined for an empty list", () => {
        expect(pickDefaultSourceId([])).toBeUndefined();
    });
});
