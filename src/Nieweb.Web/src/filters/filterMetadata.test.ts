/**
 * Parity spec for the client-side mirror of the Vieweb §3.1.2
 * operator matrix. Kept table-driven so a drift in either the C#
 * `FilterOperatorMetadata` or this TS mirror fails a test rather
 * than silently producing a broken picker.
 *
 * The expected values below match
 * `src/Nieweb.Filters/FilterOperatorMetadata.cs` verbatim.
 */
import { describe, expect, it } from "vitest";
import {
    FILTER_FIELDS,
    FILTER_OPERATORS,
    coerceValuesForArity,
    defaultOperatorFor,
    getAllowedOperators,
    getFieldValueKind,
    getOperatorArity,
    isOperatorAllowed,
    operatorSupportsValueKind,
    validateClause,
    validateValue,
    type FilterField,
    type FilterOperator,
    type FilterValueKind,
} from "./filterMetadata";

const STRING_SET_ONLY: FilterOperator[] = [
    "Equal",
    "Different",
    "In",
    "NotIn",
    "Like",
    "NotLike",
];
const SET_MEMBERSHIP: FilterOperator[] = ["Equal", "Different", "In", "NotIn"];
const ORDERED_SET: FilterOperator[] = [
    "Equal",
    "Different",
    "In",
    "NotIn",
    "Between",
    "NotBetween",
    "LessThanOrEqual",
    "GreaterThanOrEqual",
];
const FULL_TEN_COLUMN: FilterOperator[] = [
    "Equal",
    "Different",
    "In",
    "NotIn",
    "Between",
    "NotBetween",
    "Like",
    "NotLike",
    "LessThanOrEqual",
    "GreaterThanOrEqual",
];
const EQUAL_ONLY: FilterOperator[] = ["Equal"];

const EXPECTED_MATRIX: [FilterField, FilterOperator[]][] = [
    ["BoardNumber", ORDERED_SET],
    ["PnpMachine", STRING_SET_ONLY],
    ["PnpSubElement1", STRING_SET_ONLY],
    ["PnpSubElement2", STRING_SET_ONLY],
    ["PnpSubElement3", STRING_SET_ONLY],
    ["PnpSubElement4", STRING_SET_ONLY],
    ["PartNumber", STRING_SET_ONLY],
    ["InspectedObject", SET_MEMBERSHIP],
    ["Product", STRING_SET_ONLY],
    ["Package", STRING_SET_ONLY],
    ["RepairStatus", SET_MEMBERSHIP],
    ["RepairComment", STRING_SET_ONLY],
    ["ReferenceDesignator", STRING_SET_ONLY],
    ["Defect", SET_MEMBERSHIP],
    ["PanelBarcode", FULL_TEN_COLUMN],
    ["BoardIdCode", FULL_TEN_COLUMN],
    ["AoiMachine", STRING_SET_ONLY],
    ["PanelStatus", EQUAL_ONLY],
    ["BoardStatus", EQUAL_ONLY],
];

const EXPECTED_VALUE_KIND: [FilterField, FilterValueKind][] = [
    ["BoardNumber", "Integer"],
    ["PanelBarcode", "String"],
    ["BoardIdCode", "String"],
    ["PanelStatus", "Integer"],
    ["BoardStatus", "Integer"],
    ["Product", "String"],
];

describe("filterMetadata — operator matrix parity", () => {
    it("enumerates every FilterField from the C# enum", () => {
        expect(FILTER_FIELDS.length).toBe(EXPECTED_MATRIX.length);
        expect(new Set(FILTER_FIELDS)).toEqual(
            new Set(EXPECTED_MATRIX.map(([f]) => f)),
        );
    });

    it("enumerates all ten operators", () => {
        expect(FILTER_OPERATORS).toEqual([
            "Equal",
            "Different",
            "In",
            "NotIn",
            "Between",
            "NotBetween",
            "Like",
            "NotLike",
            "LessThanOrEqual",
            "GreaterThanOrEqual",
        ]);
    });

    it.each(EXPECTED_MATRIX)(
        "allows the correct operators for %s",
        (field, expected) => {
            const actual = getAllowedOperators(field);
            expect([...actual].sort()).toEqual([...expected].sort());
        },
    );

    it.each(EXPECTED_VALUE_KIND)(
        "returns the correct value kind for %s",
        (field, expected) => {
            expect(getFieldValueKind(field)).toBe(expected);
        },
    );
});

describe("filterMetadata — arity", () => {
    it("Single arity ops", () => {
        expect(getOperatorArity("Equal")).toBe("Single");
        expect(getOperatorArity("Different")).toBe("Single");
        expect(getOperatorArity("Like")).toBe("Single");
        expect(getOperatorArity("NotLike")).toBe("Single");
        expect(getOperatorArity("LessThanOrEqual")).toBe("Single");
        expect(getOperatorArity("GreaterThanOrEqual")).toBe("Single");
    });

    it("List arity ops", () => {
        expect(getOperatorArity("In")).toBe("List");
        expect(getOperatorArity("NotIn")).toBe("List");
    });

    it("Range arity ops", () => {
        expect(getOperatorArity("Between")).toBe("Range");
        expect(getOperatorArity("NotBetween")).toBe("Range");
    });
});

describe("filterMetadata — operatorSupportsValueKind", () => {
    it("Like / NotLike require String", () => {
        expect(operatorSupportsValueKind("Like", "String")).toBe(true);
        expect(operatorSupportsValueKind("Like", "Integer")).toBe(false);
        expect(operatorSupportsValueKind("NotLike", "Integer")).toBe(false);
        expect(operatorSupportsValueKind("NotLike", "Boolean")).toBe(false);
    });

    it("Boolean fields only allow Equal / Different", () => {
        expect(operatorSupportsValueKind("Equal", "Boolean")).toBe(true);
        expect(operatorSupportsValueKind("Different", "Boolean")).toBe(true);
        expect(operatorSupportsValueKind("Between", "Boolean")).toBe(false);
        expect(operatorSupportsValueKind("In", "Boolean")).toBe(false);
    });

    it("Other combinations are permitted", () => {
        expect(operatorSupportsValueKind("Between", "Integer")).toBe(true);
        expect(operatorSupportsValueKind("In", "String")).toBe(true);
        expect(operatorSupportsValueKind("GreaterThanOrEqual", "Decimal")).toBe(true);
    });
});

describe("filterMetadata — defaults and coercion", () => {
    it("defaultOperatorFor picks the first allowed operator", () => {
        expect(defaultOperatorFor("BoardNumber")).toBe("Equal");
        expect(defaultOperatorFor("PanelStatus")).toBe("Equal");
        expect(defaultOperatorFor("Defect")).toBe("Equal");
    });

    it("isOperatorAllowed is consistent with getAllowedOperators", () => {
        expect(isOperatorAllowed("PanelStatus", "Equal")).toBe(true);
        expect(isOperatorAllowed("PanelStatus", "Between")).toBe(false);
        expect(isOperatorAllowed("BoardIdCode", "Like")).toBe(true);
        expect(isOperatorAllowed("Defect", "Like")).toBe(false);
    });

    it("coerceValuesForArity produces the correct shape", () => {
        expect(coerceValuesForArity(["a", "b"], "Single", "")).toEqual(["a"]);
        expect(coerceValuesForArity([], "Single", "x")).toEqual(["x"]);
        expect(coerceValuesForArity(["a"], "Range", "z")).toEqual(["a", "z"]);
        expect(coerceValuesForArity(["a", "b", "c"], "List", "")).toEqual([
            "a",
            "b",
            "c",
        ]);
        expect(coerceValuesForArity([], "List", "x")).toEqual([]);
    });
});

describe("filterMetadata — validators", () => {
    it("string kind rejects empty", () => {
        expect(validateValue("", "String").ok).toBe(false);
        expect(validateValue("MISSING", "String").ok).toBe(true);
    });

    it("integer kind rejects decimals and NaN", () => {
        expect(validateValue("42", "Integer").ok).toBe(true);
        expect(validateValue("3.14", "Integer").ok).toBe(false);
        expect(validateValue("abc", "Integer").ok).toBe(false);
        expect(validateValue("", "Integer").ok).toBe(false);
    });

    it("decimal accepts scientific and negative", () => {
        expect(validateValue("3.14", "Decimal").ok).toBe(true);
        expect(validateValue("-1.5e2", "Decimal").ok).toBe(true);
        expect(validateValue("abc", "Decimal").ok).toBe(false);
    });

    it("date kind parses ISO-8601", () => {
        expect(validateValue("2026-01-01T00:00:00Z", "DateTimeUtc").ok).toBe(true);
        expect(validateValue("not a date", "DateTimeUtc").ok).toBe(false);
    });

    it("boolean kind accepts true/false only", () => {
        expect(validateValue("true", "Boolean").ok).toBe(true);
        expect(validateValue("false", "Boolean").ok).toBe(true);
        expect(validateValue("yes", "Boolean").ok).toBe(false);
    });

    it("clause validator flags disallowed operator", () => {
        const result = validateClause({
            field: "PanelStatus",
            operator: "Like",
            values: ["OK"],
        });
        expect(result.ok).toBe(false);
        // Both "not allowed on field" and "operator kind mismatch" fire.
        expect(
            result.errors.some(
                (e) => e.messageKey === "filters.builder.errors.operatorNotAllowed",
            ),
        ).toBe(true);
    });

    it("clause validator flags Range with wrong value count", () => {
        const result = validateClause({
            field: "BoardNumber",
            operator: "Between",
            values: ["1"],
        });
        expect(result.ok).toBe(false);
        expect(
            result.errors.some(
                (e) => e.messageKey === "filters.builder.errors.arityRange",
            ),
        ).toBe(true);
    });

    it("clause validator flags empty List", () => {
        const result = validateClause({
            field: "Defect",
            operator: "In",
            values: [],
        });
        expect(result.ok).toBe(false);
        expect(
            result.errors.some(
                (e) => e.messageKey === "filters.builder.errors.arityList",
            ),
        ).toBe(true);
    });

    it("clause validator passes a well-formed clause", () => {
        const result = validateClause({
            field: "BoardNumber",
            operator: "Between",
            values: ["1", "10"],
        });
        expect(result).toEqual({ ok: true, errors: [] });
    });
});
