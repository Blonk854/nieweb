/**
 * SPA mirror of the `Nieweb.Filters` model (server:
 * `src/Nieweb.Filters/*`). Drives the Old-school per-entity filter
 * builder and is persisted verbatim inside a tile's `configJson`
 * `filters` array, which the server parses in
 * `ReportEndpoints.TileConfig.cs` (`ParseTileFilters`). Enum string
 * values MUST match the backend `FilterField` / `FilterOperator`
 * member names exactly (the server parses them case-insensitively).
 *
 * Scope: only the fields the two embeddable tiles can honour today —
 * Pareto (component / `TESTED_OBJECT`) and Panel Yield (`PANELS`).
 * See `ReportFilterRows` on the server for the matching allow-lists.
 */

// -------------------- fields & operators --------------------

/** Vieweb §3.1.2 filter field (subset supported by the two tiles). */
export type FilterField =
    | "ReferenceDesignator"
    | "PartNumber"
    | "Package"
    | "Product"
    | "AoiMachine"
    | "Defect"
    | "PanelBarcode"
    | "PanelStatus";

/** Vieweb §3.1.2 comparison operator. */
export type FilterOperator =
    | "Equal"
    | "Different"
    | "In"
    | "NotIn"
    | "Between"
    | "NotBetween"
    | "Like"
    | "NotLike"
    | "LessThanOrEqual"
    | "GreaterThanOrEqual";

/** How many values an operator takes. */
export type FilterArity = "single" | "list" | "range";

/** Scalar kind a field's values carry (drives the value input control). */
export type FilterValueKind = "string" | "integer";

/** A single AND-joined predicate. Persisted verbatim in `configJson`. */
export type FilterClause = {
    field: FilterField;
    operator: FilterOperator;
    /** 1 (single), 1+ (list), or exactly 2 (range) invariant-culture strings. */
    values: string[];
};

/** Ordered list of clauses; all AND-joined (Vieweb never allowed OR). */
export type FilterRequest = FilterClause[];

// -------------------- metadata --------------------

export const ALL_OPERATORS: readonly FilterOperator[] = [
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

// Vieweb §3.1.2 operator sets (verbatim from FilterFieldMetadata).
const STRING_SET_ONLY: readonly FilterOperator[] = [
    "Equal",
    "Different",
    "In",
    "NotIn",
    "Like",
    "NotLike",
];
const SET_MEMBERSHIP: readonly FilterOperator[] = ["Equal", "Different", "In", "NotIn"];
const FULL_TEN: readonly FilterOperator[] = [...ALL_OPERATORS];
const EQUAL_ONLY: readonly FilterOperator[] = ["Equal"];

const ALLOWED_BY_FIELD: Readonly<Record<FilterField, readonly FilterOperator[]>> = {
    ReferenceDesignator: STRING_SET_ONLY,
    PartNumber: STRING_SET_ONLY,
    Package: STRING_SET_ONLY,
    Product: STRING_SET_ONLY,
    AoiMachine: STRING_SET_ONLY,
    Defect: SET_MEMBERSHIP,
    PanelBarcode: FULL_TEN,
    PanelStatus: EQUAL_ONLY,
};

const VALUE_KIND_BY_FIELD: Readonly<Record<FilterField, FilterValueKind>> = {
    ReferenceDesignator: "string",
    PartNumber: "string",
    Package: "string",
    Product: "string",
    AoiMachine: "string",
    Defect: "string",
    PanelBarcode: "string",
    PanelStatus: "integer",
};

/** Fields the Pareto tile can filter on (matches server `TestedObjectFields`). */
export const PARETO_FILTER_FIELDS: readonly FilterField[] = [
    "ReferenceDesignator",
    "PartNumber",
    "Package",
    "Product",
    "AoiMachine",
    "Defect",
];

/** Fields the Panel Yield tile can filter on (matches server `PanelFields`). */
export const PANEL_YIELD_FILTER_FIELDS: readonly FilterField[] = [
    "PanelBarcode",
    "PanelStatus",
    "Product",
    "AoiMachine",
];

/** Fields a given tile type can filter on. Unknown tiles get no fields. */
export function filterFieldsForTile(tileType: string): readonly FilterField[] {
    switch (tileType) {
        case "pareto":
            return PARETO_FILTER_FIELDS;
        case "panelYield":
            return PANEL_YIELD_FILTER_FIELDS;
        default:
            return [];
    }
}

/** Operators allowed on a field (Vieweb §3.1.2). */
export function allowedOperators(field: FilterField): readonly FilterOperator[] {
    return ALLOWED_BY_FIELD[field] ?? [];
}

/** Value arity of an operator. */
export function operatorArity(op: FilterOperator): FilterArity {
    switch (op) {
        case "In":
        case "NotIn":
            return "list";
        case "Between":
        case "NotBetween":
            return "range";
        default:
            return "single";
    }
}

/** Scalar value kind of a field. */
export function fieldValueKind(field: FilterField): FilterValueKind {
    return VALUE_KIND_BY_FIELD[field] ?? "string";
}

// -------------------- validation & (de)serialisation --------------------

/**
 * Returns true when a clause is structurally valid (known field /
 * operator, operator allowed on the field, correct value arity, and
 * every value non-empty). Mirrors the server `FilterValidator` closely
 * enough that the UI never submits a request the server would reject.
 */
export function isClauseValid(clause: FilterClause): boolean {
    if (!(clause.field in ALLOWED_BY_FIELD)) return false;
    if (!allowedOperators(clause.field).includes(clause.operator)) return false;

    const values = clause.values ?? [];
    if (values.some((v) => v.trim().length === 0)) return false;

    switch (operatorArity(clause.operator)) {
        case "single":
            if (values.length !== 1) return false;
            break;
        case "range":
            if (values.length !== 2) return false;
            break;
        case "list":
            if (values.length < 1) return false;
            break;
    }

    if (fieldValueKind(clause.field) === "integer") {
        if (values.some((v) => !Number.isInteger(Number(v)))) return false;
    }
    return true;
}

/** A clause seeded with the field's first allowed operator + empty values. */
export function newClause(field: FilterField): FilterClause {
    const op = allowedOperators(field)[0] ?? "Equal";
    return { field, operator: op, values: valuesForArity(operatorArity(op), []) };
}

/** Resize a value array to match an operator's arity, preserving entries. */
export function valuesForArity(arity: FilterArity, current: string[]): string[] {
    switch (arity) {
        case "single":
            return [current[0] ?? ""];
        case "range":
            return [current[0] ?? "", current[1] ?? ""];
        case "list":
            return current.length > 0 ? current : [""];
    }
}

/**
 * Parse an arbitrary `filters` array (typically from a tile's
 * `configJson`) into well-formed clauses, dropping anything malformed.
 * Total: never throws.
 */
export function parseFilterRequest(raw: unknown): FilterRequest {
    if (!Array.isArray(raw)) return [];
    const out: FilterClause[] = [];
    for (const el of raw) {
        if (el === null || typeof el !== "object") continue;
        const obj = el as Record<string, unknown>;
        const field = obj.field;
        const operator = obj.operator;
        if (typeof field !== "string" || typeof operator !== "string") continue;
        if (!(field in ALLOWED_BY_FIELD)) continue;
        if (!ALL_OPERATORS.includes(operator as FilterOperator)) continue;
        const values = Array.isArray(obj.values)
            ? obj.values.filter((v): v is string | number => typeof v === "string" || typeof v === "number").map(String)
            : [];
        out.push({
            field: field as FilterField,
            operator: operator as FilterOperator,
            values,
        });
    }
    return out;
}
