import { describe, it, expect } from "vitest";

import {
    allowedOperators,
    fieldValueKind,
    filterFieldsForTile,
    isClauseValid,
    newClause,
    operatorArity,
    parseFilterRequest,
    valuesForArity,
    type FilterClause,
} from "./filters";

describe("filter field/operator metadata", () => {
    it("exposes the pareto and panel-yield field sets", () => {
        expect(filterFieldsForTile("pareto")).toContain("Defect");
        expect(filterFieldsForTile("pareto")).toContain("PartNumber");
        expect(filterFieldsForTile("panelYield")).toContain("PanelBarcode");
        expect(filterFieldsForTile("panelYield")).not.toContain("Defect");
        expect(filterFieldsForTile("comment")).toEqual([]);
    });

    it("restricts operators per Vieweb table", () => {
        // Defect is set-membership only.
        expect(allowedOperators("Defect")).toEqual(["Equal", "Different", "In", "NotIn"]);
        // Panel status is exact-match only.
        expect(allowedOperators("PanelStatus")).toEqual(["Equal"]);
        // Bar code admits every operator.
        expect(allowedOperators("PanelBarcode")).toHaveLength(10);
        // String fields get the six-operator set (incl. Like/NotLike).
        expect(allowedOperators("PartNumber")).toContain("Like");
        expect(allowedOperators("PartNumber")).not.toContain("Between");
    });

    it("reports operator arity", () => {
        expect(operatorArity("Equal")).toBe("single");
        expect(operatorArity("In")).toBe("list");
        expect(operatorArity("Between")).toBe("range");
    });

    it("reports value kind", () => {
        expect(fieldValueKind("PanelStatus")).toBe("integer");
        expect(fieldValueKind("PartNumber")).toBe("string");
    });
});

describe("isClauseValid", () => {
    const clause = (c: Partial<FilterClause>): FilterClause => ({
        field: "PartNumber",
        operator: "Equal",
        values: ["x"],
        ...c,
    });

    it("accepts a well-formed clause", () => {
        expect(isClauseValid(clause({}))).toBe(true);
    });

    it("rejects a disallowed operator on a field", () => {
        expect(isClauseValid(clause({ field: "Defect", operator: "Like" }))).toBe(false);
    });

    it("enforces arity", () => {
        expect(isClauseValid(clause({ operator: "Between", values: ["1"] }))).toBe(false);
        expect(isClauseValid(clause({ field: "PanelBarcode", operator: "Between", values: ["A", "Z"] }))).toBe(true);
        expect(isClauseValid(clause({ operator: "In", values: [] }))).toBe(false);
    });

    it("rejects empty values", () => {
        expect(isClauseValid(clause({ values: [" "] }))).toBe(false);
    });

    it("requires integers on integer fields", () => {
        expect(isClauseValid(clause({ field: "PanelStatus", operator: "Equal", values: ["1"] }))).toBe(true);
        expect(isClauseValid(clause({ field: "PanelStatus", operator: "Equal", values: ["x"] }))).toBe(false);
    });
});

describe("newClause / valuesForArity", () => {
    it("seeds a clause with the field's first allowed operator", () => {
        const c = newClause("Defect");
        expect(c.operator).toBe("Equal");
        expect(c.values).toEqual([""]);
    });

    it("resizes values to match arity", () => {
        expect(valuesForArity("single", ["a", "b"])).toEqual(["a"]);
        expect(valuesForArity("range", ["a"])).toEqual(["a", ""]);
        expect(valuesForArity("list", [])).toEqual([""]);
        expect(valuesForArity("list", ["a", "b"])).toEqual(["a", "b"]);
    });
});

describe("parseFilterRequest", () => {
    it("returns [] for non-arrays", () => {
        expect(parseFilterRequest(undefined)).toEqual([]);
        expect(parseFilterRequest(null)).toEqual([]);
        expect(parseFilterRequest({})).toEqual([]);
    });

    it("keeps well-formed clauses and drops junk", () => {
        const parsed = parseFilterRequest([
            { field: "PartNumber", operator: "NotLike", values: ["PN-B"] },
            { field: "NotAField", operator: "Equal", values: ["x"] },
            { field: "Package", operator: "NopeOp", values: ["x"] },
            { field: "PanelStatus", operator: "Equal", values: [1] },
        ]);
        expect(parsed).toEqual([
            { field: "PartNumber", operator: "NotLike", values: ["PN-B"] },
            { field: "PanelStatus", operator: "Equal", values: ["1"] },
        ]);
    });
});
