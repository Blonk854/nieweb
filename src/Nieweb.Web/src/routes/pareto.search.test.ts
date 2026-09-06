import { describe, expect, it } from "vitest";
import {
    pickDefaultSourceId,
    paretoDrillInto,
    toApiQuery,
    validateParetoSearch,
    withDefectBit,
    withoutDefectBit,
    withNumericFilter,
    withoutNumericFilter,
    type ParetoSearch,
} from "./pareto.search";
import type { SourceInfo } from "../api/sources";

describe("validateParetoSearch", () => {
    it("returns an empty shape for empty input", () => {
        expect(validateParetoSearch({})).toEqual<ParetoSearch>({
            sourceId: undefined,
            startUtc: undefined,
            endUtc: undefined,
            axis: undefined,
            numerator: undefined,
            opportunity: undefined,
            topN: undefined,
            vitalFewThreshold: undefined,
            machineIds: undefined,
            productIds: undefined,
            defectBits: undefined,
            topologies: undefined,
            partNumbers: undefined,
            jedecNames: undefined,
            cardNumbers: undefined,
        });
    });

    it("snaps DPMO/PPM to Count on object-level axes", () => {
        expect(validateParetoSearch({ axis: "PartNumber", weight: "Dpmo" }).weight).toBe(
            "Count",
        );
        expect(validateParetoSearch({ axis: "Jedec", weight: "Ppm" }).weight).toBe("Count");
        expect(validateParetoSearch({ axis: "Product", weight: "Dpmo" }).weight).toBe("Dpmo");
        expect(validateParetoSearch({ axis: "Subpanel", weight: "Dpmo" }).weight).toBe("Dpmo");
        expect(validateParetoSearch({ axis: "Subpanel", weight: "Ppm" }).weight).toBe("Ppm");
    });

    it("parses canonical enum names case-insensitively", () => {
        const s = validateParetoSearch({
            axis: "product",
            numerator: "REAL",
            opportunity: "Components",
        });
        expect(s.axis).toBe("Product");
        expect(s.numerator).toBe("Real");
        expect(s.opportunity).toBe("Components");
    });

    it("drops unknown enum values", () => {
        const s = validateParetoSearch({
            axis: "bogus",
            numerator: "",
            opportunity: 42,
        });
        expect(s.axis).toBeUndefined();
        expect(s.numerator).toBeUndefined();
        expect(s.opportunity).toBeUndefined();
    });

    it("parses comma-separated defectBits", () => {
        const s = validateParetoSearch({
            defectBits: "1,3,5",
        });
        expect(s.defectBits).toEqual([1, 3, 5]);
    });

    it("parses array defectBits", () => {
        const s = validateParetoSearch({
            defectBits: ["2", 4],
        });
        expect(s.defectBits).toEqual([2, 4]);
    });

    it("parses topN as a positive integer only", () => {
        expect(validateParetoSearch({ topN: "10" }).topN).toBe(10);
        expect(validateParetoSearch({ topN: 0 }).topN).toBeUndefined();
        expect(validateParetoSearch({ topN: -1 }).topN).toBeUndefined();
        expect(validateParetoSearch({ topN: "3.5" }).topN).toBeUndefined();
        expect(validateParetoSearch({ topN: "abc" }).topN).toBeUndefined();
    });

    it("parses vitalFewThreshold as any finite number", () => {
        expect(validateParetoSearch({ vitalFewThreshold: "80" }).vitalFewThreshold).toBe(80);
        expect(validateParetoSearch({ vitalFewThreshold: 90.5 }).vitalFewThreshold).toBe(90.5);
        expect(validateParetoSearch({ vitalFewThreshold: "abc" }).vitalFewThreshold).toBeUndefined();
    });

    it("parses string-list narrowing filters", () => {
        const s = validateParetoSearch({
            topologies: "R1,R2, R3",
            partNumbers: ["PN-A", " PN-B "],
            jedecNames: "SOT23",
        });
        expect(s.topologies).toEqual(["R1", "R2", "R3"]);
        expect(s.partNumbers).toEqual(["PN-A", "PN-B"]);
        expect(s.jedecNames).toEqual(["SOT23"]);
    });

    it("parses cardNumbers from CSV and arrays, including zero", () => {
        expect(validateParetoSearch({ cardNumbers: "0,3" }).cardNumbers).toEqual([0, 3]);
        expect(validateParetoSearch({ cardNumbers: ["1", 2] }).cardNumbers).toEqual([1, 2]);
        expect(validateParetoSearch({ cardNumbers: 0 }).cardNumbers).toEqual([0]);
        expect(validateParetoSearch({ axis: "subpanel" }).axis).toBe("Subpanel");
    });

    it("parses skip exclusion and statuses", () => {
        const s = validateParetoSearch({
            skipExclusion: "clean",
            skipStatuses: "ManualSkip,HeuristicMissing",
        });
        expect(s.skipExclusion).toBe("Clean");
        expect(s.skipStatuses).toEqual(["ManualSkip", "HeuristicMissing"]);
    });
});

describe("toApiQuery", () => {
    it("omits empty and default fields", () => {
        expect(toApiQuery({})).toEqual({});
    });

    it("emits enum names as-is", () => {
        expect(
            toApiQuery({
                axis: "Product",
                numerator: "Real",
                opportunity: "Components",
            }),
        ).toEqual({
            axis: "Product",
            numerator: "Real",
            opportunity: "Components",
        });
    });

    it("joins id arrays with commas", () => {
        expect(
            toApiQuery({
                machineIds: [1, 2],
                defectBits: [3, 4],
                topologies: ["R1", "R2"],
            }),
        ).toEqual({
            machineIds: "1,2",
            defectBits: "3,4",
            topologies: "R1,R2",
        });
    });

    it("emits skip params only when set", () => {
        expect(toApiQuery({ skipExclusion: "Raw" }).skipExclusion).toBeUndefined();
        expect(toApiQuery({ skipExclusion: "Clean" }).skipExclusion).toBe("Clean");
        expect(toApiQuery({ skipStatuses: ["ManualSkip"] }).skipStatuses).toBe("ManualSkip");
    });

    it("emits topN and vitalFewThreshold as strings", () => {
        expect(toApiQuery({ topN: 5, vitalFewThreshold: 90 })).toEqual({
            topN: "5",
            vitalFewThreshold: "90",
        });
    });

    it("drops non-positive topN", () => {
        expect(toApiQuery({ topN: 0 })).toEqual({});
        expect(toApiQuery({ topN: -1 })).toEqual({});
    });

    it("emits the full populated shape", () => {
        expect(
            toApiQuery({
                sourceId: "postreflow",
                startUtc: "2026-06-01T00:00:00.000Z",
                endUtc: "2026-07-01T00:00:00.000Z",
                axis: "Defect",
                numerator: "Real",
                opportunity: "All",
                topN: 10,
                vitalFewThreshold: 80,
                machineIds: [1],
                productIds: [2, 3],
                defectBits: [1, 5],
                topologies: ["R1"],
                partNumbers: ["PN-A"],
                jedecNames: ["SOT23"],
                cardNumbers: [0, 3],
            }),
        ).toEqual({
            sourceId: "postreflow",
            startUtc: "2026-06-01T00:00:00.000Z",
            endUtc: "2026-07-01T00:00:00.000Z",
            axis: "Defect",
            numerator: "Real",
            opportunity: "All",
            topN: "10",
            vitalFewThreshold: "80",
            machineIds: "1",
            productIds: "2,3",
            defectBits: "1,5",
            topologies: "R1",
            partNumbers: "PN-A",
            jedecNames: "SOT23",
            cardNumbers: "0,3",
        });
    });
});

describe("withDefectBit / withoutDefectBit", () => {
    it("appends a new bit, preserving order", () => {
        const next = withDefectBit({ defectBits: [1, 3] }, 5);
        expect(next.defectBits).toEqual([1, 3, 5]);
    });

    it("returns the same search when the bit is already present", () => {
        const search: ParetoSearch = { defectBits: [1, 3] };
        expect(withDefectBit(search, 3)).toBe(search);
    });

    it("ignores non-positive and non-integer bits", () => {
        const search: ParetoSearch = { defectBits: [1] };
        expect(withDefectBit(search, 0)).toBe(search);
        expect(withDefectBit(search, -1)).toBe(search);
        expect(withDefectBit(search, 1.5)).toBe(search);
        expect(withDefectBit(search, NaN)).toBe(search);
    });

    it("initialises the array when defectBits is undefined", () => {
        const next = withDefectBit({}, 7);
        expect(next.defectBits).toEqual([7]);
    });

    it("removes an existing bit", () => {
        const next = withoutDefectBit({ defectBits: [1, 3, 5] }, 3);
        expect(next.defectBits).toEqual([1, 5]);
    });

    it("clears the field when removing the last bit", () => {
        const next = withoutDefectBit({ defectBits: [3] }, 3);
        expect(next.defectBits).toBeUndefined();
    });

    it("returns the same search when the bit was not present", () => {
        const search: ParetoSearch = { defectBits: [1, 5] };
        expect(withoutDefectBit(search, 3)).toBe(search);
    });
});

describe("paretoDrillInto", () => {
    it("appends the clicked bit, advances to Part number, and forces the Count scale", () => {
        const next = paretoDrillInto({ axis: "Defect", defectBits: [1] }, "3");
        expect(next.axis).toBe("PartNumber");
        expect(next.defectBits).toEqual([1, 3]);
        // Part number is object-level (no DPMO denominator) → force volume scale.
        expect(next.weight).toBe("Count");
    });

    it("adds the product id and advances to the Defect axis", () => {
        const next = paretoDrillInto({ axis: "Product" }, "42");
        expect(next.axis).toBe("Defect");
        expect(next.productIds).toEqual([42]);
    });

    it("adds the machine id and advances to the Defect axis", () => {
        const next = paretoDrillInto(
            { axis: "AoiMachine", machineIds: [1] },
            "7",
        );
        expect(next.axis).toBe("Defect");
        expect(next.machineIds).toEqual([1, 7]);
    });

    it("advances part number to reference designator, then subpanel, and leaves subpanel terminal", () => {
        const pn = paretoDrillInto({ axis: "PartNumber" }, "PN-A");
        expect(pn).toMatchObject({
            axis: "ReferenceDesignator",
            partNumbers: ["PN-A"],
            weight: "Count",
        });
        const rd = paretoDrillInto(
            { axis: "ReferenceDesignator", weight: "Dpmo" },
            "R12",
        );
        expect(rd).toMatchObject({
            axis: "Subpanel",
            topologies: ["R12"],
            weight: "Dpmo",
        });
        const sub: ParetoSearch = { axis: "Subpanel" };
        expect(paretoDrillInto(sub, "3")).toBe(sub);
        expect(paretoDrillInto({ axis: "Jedec" }, "0402")).toMatchObject({
            axis: "Defect",
            jedecNames: ["0402"],
        });
    });

    it("still advances the axis when the id is already filtered", () => {
        const next = paretoDrillInto(
            { axis: "Product", productIds: [42] },
            "42",
        );
        expect(next.axis).toBe("Defect");
        expect(next.productIds).toEqual([42]);
    });

    it("leaves Day / Shift bars unchanged (not drillable)", () => {
        const day: ParetoSearch = { axis: "Day" };
        expect(paretoDrillInto(day, "2026-07-28")).toBe(day);
        const shift: ParetoSearch = { axis: "Shift" };
        expect(paretoDrillInto(shift, "2026-07-28 · A")).toBe(shift);
    });

    it("ignores a non-numeric id on numeric axes", () => {
        const search: ParetoSearch = { axis: "Product" };
        expect(paretoDrillInto(search, "not-a-number")).toBe(search);
    });

    it("walks the full Product → Defect → Part number → Reference designator → Subpanel chain", () => {
        let s: ParetoSearch = { axis: "Product" };
        s = paretoDrillInto(s, "42");
        expect(s.axis).toBe("Defect");
        expect(s.productIds).toEqual([42]);
        s = paretoDrillInto(s, "3");
        expect(s.axis).toBe("PartNumber");
        expect(s.defectBits).toEqual([3]);
        expect(s.weight).toBe("Count");
        s = paretoDrillInto(s, "PN-A");
        expect(s.axis).toBe("ReferenceDesignator");
        expect(s.partNumbers).toEqual(["PN-A"]);
        s = paretoDrillInto(s, "R7");
        expect(s.axis).toBe("Subpanel");
        expect(s.topologies).toEqual(["R7"]);
        expect(s.weight).toBe("Count");
        expect(paretoDrillInto(s, "1")).toBe(s);
    });
});

describe("withNumericFilter / withoutNumericFilter", () => {
    it("supports cardNumbers including zero", () => {
        const added = withNumericFilter({}, "cardNumbers", 0);
        expect(added.cardNumbers).toEqual([0]);
        const next = withNumericFilter(added, "cardNumbers", 3);
        expect(next.cardNumbers).toEqual([0, 3]);
        expect(withoutNumericFilter(next, "cardNumbers", 0).cardNumbers).toEqual([3]);
        expect(withoutNumericFilter({ cardNumbers: [3] }, "cardNumbers", 3).cardNumbers).toBeUndefined();
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
