import { describe, expect, it } from "vitest";
import {
    validateDpmoSearch,
    toApiQuery,
    pickDefaultSourceId,
    type DpmoSearch,
} from "./dpmo.search";
import type { SourceInfo } from "../api/sources";

describe("validateDpmoSearch", () => {
    it("returns an empty shape for empty input", () => {
        expect(validateDpmoSearch({})).toEqual<DpmoSearch>({
            sourceId: undefined,
            startUtc: undefined,
            endUtc: undefined,
            groupBy: undefined,
            numerator: undefined,
            opportunity: undefined,
            machineIds: undefined,
            productIds: undefined,
            includeObsoleteBits: undefined,
            skipExclusion: undefined,
            skipStatuses: undefined,
        });
    });

    it("parses and filters skipStatuses", () => {
        expect(validateDpmoSearch({ skipStatuses: "ManualSkip,None,bogus" }).skipStatuses).toEqual([
            "ManualSkip",
            "None",
        ]);
        expect(validateDpmoSearch({ skipStatuses: "nope" }).skipStatuses).toBeUndefined();
    });

    it("keeps valid enums and drops invalid ones", () => {
        const r = validateDpmoSearch({
            groupBy: "Defect",
            numerator: "Real",
            opportunity: "Components",
            skipExclusion: "Clean",
        });
        expect(r.groupBy).toBe("Defect");
        expect(r.numerator).toBe("Real");
        expect(r.opportunity).toBe("Components");
        expect(r.skipExclusion).toBe("Clean");
        expect(validateDpmoSearch({ groupBy: "Nope", skipExclusion: "x" })).toMatchObject({
            groupBy: undefined,
            skipExclusion: undefined,
        });
    });

    it("parses comma-separated ids and booleans", () => {
        const r = validateDpmoSearch({
            machineIds: "1,2",
            productIds: "9",
            includeObsoleteBits: "true",
        });
        expect(r.machineIds).toEqual([1, 2]);
        expect(r.productIds).toEqual([9]);
        expect(r.includeObsoleteBits).toBe(true);
    });
});

describe("toApiQuery", () => {
    it("emits only set fields and omits default skip / obsolete flags", () => {
        expect(
            toApiQuery({
                sourceId: "postreflow",
                startUtc: "2026-01-01T00:00:00Z",
                endUtc: "2026-01-02T00:00:00Z",
                groupBy: "AoiMachine",
            }),
        ).toEqual({
            sourceId: "postreflow",
            startUtc: "2026-01-01T00:00:00Z",
            endUtc: "2026-01-02T00:00:00Z",
            groupBy: "AoiMachine",
        });
        expect(toApiQuery({ skipExclusion: "Raw", includeObsoleteBits: false })).toEqual({});
    });

    it("emits Clean skip and obsolete flag and joins id arrays", () => {
        expect(
            toApiQuery({
                machineIds: [1, 2],
                productIds: [3],
                includeObsoleteBits: true,
                skipExclusion: "Clean",
                skipStatuses: ["ManualSkip", "MachineFlagged"],
            }),
        ).toEqual({
            machineIds: "1,2",
            productIds: "3",
            includeObsoleteBits: "true",
            skipExclusion: "Clean",
            skipStatuses: "ManualSkip,MachineFlagged",
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
