/**
 * Client-side mirror of the Vieweb 1.6.2 filter grammar defined in
 * `src/Nieweb.Filters/` on the server. The server's
 * `FilterValidator` remains the authoritative gate — this module
 * exists so the SPA can render restricted operator pickers and
 * arity-aware value controls without a round-trip.
 *
 * Whenever the C# enums / metadata change, this file must change in
 * lockstep. The parity test in `filterMetadata.test.ts` fails fast
 * if any field/operator drifts.
 */

/** Vieweb §3.1.2 filter fields. Enum names match the C# enum. */
export type FilterField =
    | "BoardNumber"
    | "PnpMachine"
    | "PnpSubElement1"
    | "PnpSubElement2"
    | "PnpSubElement3"
    | "PnpSubElement4"
    | "PartNumber"
    | "InspectedObject"
    | "Product"
    | "Package"
    | "RepairStatus"
    | "RepairComment"
    | "ReferenceDesignator"
    | "Defect"
    | "PanelBarcode"
    | "BoardIdCode"
    | "AoiMachine"
    | "PanelStatus"
    | "BoardStatus";

/** Ordered field list — drives the field-picker dropdown. */
export const FILTER_FIELDS: readonly FilterField[] = [
    "BoardNumber",
    "PnpMachine",
    "PnpSubElement1",
    "PnpSubElement2",
    "PnpSubElement3",
    "PnpSubElement4",
    "PartNumber",
    "InspectedObject",
    "Product",
    "Package",
    "RepairStatus",
    "RepairComment",
    "ReferenceDesignator",
    "Defect",
    "PanelBarcode",
    "BoardIdCode",
    "AoiMachine",
    "PanelStatus",
    "BoardStatus",
];

/** Vieweb §3.1.2 comparison operators. Enum names match the C# enum. */
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

export const FILTER_OPERATORS: readonly FilterOperator[] = [
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

/** Scalar kind a filter value carries. */
export type FilterValueKind =
    | "String"
    | "Integer"
    | "Decimal"
    | "DateTimeUtc"
    | "Boolean";

/** How many values an operator takes. Matches C# `FilterOperatorArity`. */
export type FilterOperatorArity = "Single" | "List" | "Range";

/** Value arity per operator. */
export function getOperatorArity(op: FilterOperator): FilterOperatorArity {
    switch (op) {
        case "Equal":
        case "Different":
        case "Like":
        case "NotLike":
        case "LessThanOrEqual":
        case "GreaterThanOrEqual":
            return "Single";
        case "In":
        case "NotIn":
            return "List";
        case "Between":
        case "NotBetween":
            return "Range";
    }
}

/** Value kind expected by a field. */
export function getFieldValueKind(field: FilterField): FilterValueKind {
    switch (field) {
        case "BoardNumber":
        case "PanelStatus":
        case "BoardStatus":
            return "Integer";
        // Bar codes and ID codes are text on the AOI DB even when
        // numeric-looking, so LIKE / NOT LIKE remain useful.
        case "PanelBarcode":
        case "BoardIdCode":
            return "String";
        default:
            return "String";
    }
}

/**
 * Server-side compatibility rule: `Like` / `NotLike` require a
 * string value kind, and `Boolean` only supports `Equal` /
 * `Different`. Mirrors `FilterOperatorMetadata.SupportsValueKind`.
 */
export function operatorSupportsValueKind(
    op: FilterOperator,
    kind: FilterValueKind,
): boolean {
    if (op === "Like" || op === "NotLike") {
        return kind === "String";
    }
    if (kind === "Boolean") {
        return op === "Equal" || op === "Different";
    }
    return true;
}

// ------------- Vieweb §3.1.2 operator matrix (verbatim) -------------

const STRING_SET_ONLY: readonly FilterOperator[] = [
    "Equal",
    "Different",
    "In",
    "NotIn",
    "Like",
    "NotLike",
];

const SET_MEMBERSHIP: readonly FilterOperator[] = [
    "Equal",
    "Different",
    "In",
    "NotIn",
];

const ORDERED_SET: readonly FilterOperator[] = [
    "Equal",
    "Different",
    "In",
    "NotIn",
    "Between",
    "NotBetween",
    "LessThanOrEqual",
    "GreaterThanOrEqual",
];

const FULL_TEN_COLUMN: readonly FilterOperator[] = [
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

const EQUAL_ONLY: readonly FilterOperator[] = ["Equal"];

const ALLOWED_OPERATORS: Readonly<
    Record<FilterField, readonly FilterOperator[]>
> = {
    BoardNumber: ORDERED_SET,
    PnpMachine: STRING_SET_ONLY,
    PnpSubElement1: STRING_SET_ONLY,
    PnpSubElement2: STRING_SET_ONLY,
    PnpSubElement3: STRING_SET_ONLY,
    PnpSubElement4: STRING_SET_ONLY,
    PartNumber: STRING_SET_ONLY,
    InspectedObject: SET_MEMBERSHIP,
    Product: STRING_SET_ONLY,
    Package: STRING_SET_ONLY,
    RepairStatus: SET_MEMBERSHIP,
    RepairComment: STRING_SET_ONLY,
    ReferenceDesignator: STRING_SET_ONLY,
    Defect: SET_MEMBERSHIP,
    PanelBarcode: FULL_TEN_COLUMN,
    BoardIdCode: FULL_TEN_COLUMN,
    AoiMachine: STRING_SET_ONLY,
    PanelStatus: EQUAL_ONLY,
    BoardStatus: EQUAL_ONLY,
};

/**
 * Returns the (immutable) operator set Vieweb allows on a field.
 * Iteration order matches {@link FILTER_OPERATORS} so operator
 * dropdowns render in a stable order across every field.
 */
export function getAllowedOperators(
    field: FilterField,
): readonly FilterOperator[] {
    return ALLOWED_OPERATORS[field];
}

export function isOperatorAllowed(
    field: FilterField,
    op: FilterOperator,
): boolean {
    return ALLOWED_OPERATORS[field].includes(op);
}

/**
 * Chooses a sensible default operator for a field — the first
 * allowed operator in the canonical {@link FILTER_OPERATORS} order
 * (so simple fields default to `Equal`).
 */
export function defaultOperatorFor(field: FilterField): FilterOperator {
    const allowed = ALLOWED_OPERATORS[field];
    for (const op of FILTER_OPERATORS) {
        if (allowed.includes(op)) return op;
    }
    // Impossible unless the matrix has an empty row.
    return "Equal";
}

// -------------------- FilterClause / FilterRequest --------------------

/**
 * A single predicate — mirror of the server `FilterClause` record.
 * `values` semantics depend on the operator arity:
 *  - Single: exactly one entry
 *  - List:   one or more entries
 *  - Range:  exactly two entries `[min, max]`
 */
export type FilterClause = {
    field: FilterField;
    operator: FilterOperator;
    values: string[];
};

/** AND-joined clauses, matching server `FilterRequest`. */
export type FilterRequest = {
    clauses: FilterClause[];
};

export const EMPTY_FILTER_REQUEST: FilterRequest = { clauses: [] };

// -------------------- Client-side value validation --------------------

/**
 * Structural validator for a single value string against its kind.
 * Mirrors `FilterValidator.TryParseValue` — returns an i18n key on
 * failure so the caller can surface a localised message.
 */
export function validateValue(
    raw: string,
    kind: FilterValueKind,
): { ok: true } | { ok: false; messageKey: ValueValidationErrorKey } {
    if (raw === null || raw === undefined) {
        return { ok: false, messageKey: "filters.builder.errors.valueRequired" };
    }
    switch (kind) {
        case "String":
            if (raw.length === 0) {
                return {
                    ok: false,
                    messageKey: "filters.builder.errors.stringEmpty",
                };
            }
            return { ok: true };
        case "Integer": {
            const n = Number(raw);
            if (!Number.isFinite(n) || !Number.isInteger(n) || raw.trim() === "") {
                return {
                    ok: false,
                    messageKey: "filters.builder.errors.integerInvalid",
                };
            }
            return { ok: true };
        }
        case "Decimal": {
            const n = Number(raw);
            if (!Number.isFinite(n) || raw.trim() === "") {
                return {
                    ok: false,
                    messageKey: "filters.builder.errors.decimalInvalid",
                };
            }
            return { ok: true };
        }
        case "DateTimeUtc": {
            const d = new Date(raw);
            if (Number.isNaN(d.getTime())) {
                return {
                    ok: false,
                    messageKey: "filters.builder.errors.dateInvalid",
                };
            }
            return { ok: true };
        }
        case "Boolean":
            if (raw !== "true" && raw !== "false") {
                return {
                    ok: false,
                    messageKey: "filters.builder.errors.booleanInvalid",
                };
            }
            return { ok: true };
    }
}

/** i18n keys the value validator may emit. */
export type ValueValidationErrorKey =
    | "filters.builder.errors.valueRequired"
    | "filters.builder.errors.stringEmpty"
    | "filters.builder.errors.integerInvalid"
    | "filters.builder.errors.decimalInvalid"
    | "filters.builder.errors.dateInvalid"
    | "filters.builder.errors.booleanInvalid";

/**
 * Validates a whole clause structurally (operator allowed on field,
 * value count matches arity, values parse). Does **not** check
 * membership in the AOI DB — that belongs to the per-report binder,
 * same as on the server.
 */
export function validateClause(clause: FilterClause): {
    ok: boolean;
    errors: { key: string; messageKey: string }[];
} {
    const errors: { key: string; messageKey: string }[] = [];

    if (!isOperatorAllowed(clause.field, clause.operator)) {
        errors.push({
            key: "operator",
            messageKey: "filters.builder.errors.operatorNotAllowed",
        });
    }

    const kind = getFieldValueKind(clause.field);
    if (!operatorSupportsValueKind(clause.operator, kind)) {
        errors.push({
            key: "operator",
            messageKey: "filters.builder.errors.operatorKindMismatch",
        });
    }

    const arity = getOperatorArity(clause.operator);
    if (arity === "Single" && clause.values.length !== 1) {
        errors.push({
            key: "values",
            messageKey: "filters.builder.errors.aritySingle",
        });
    } else if (arity === "Range" && clause.values.length !== 2) {
        errors.push({
            key: "values",
            messageKey: "filters.builder.errors.arityRange",
        });
    } else if (arity === "List" && clause.values.length === 0) {
        errors.push({
            key: "values",
            messageKey: "filters.builder.errors.arityList",
        });
    }

    for (let i = 0; i < clause.values.length; i++) {
        const v = validateValue(clause.values[i], kind);
        if (!v.ok) {
            errors.push({ key: `values[${i}]`, messageKey: v.messageKey });
        }
    }

    return { ok: errors.length === 0, errors };
}

/**
 * Coerces a clause's values array to match its operator arity —
 * used when the user changes operator or field mid-edit so the row
 * always has a well-formed value shape to render.
 */
export function coerceValuesForArity(
    values: readonly string[],
    arity: FilterOperatorArity,
    defaultValue: string,
): string[] {
    switch (arity) {
        case "Single":
            return [values[0] ?? defaultValue];
        case "Range":
            return [
                values[0] ?? defaultValue,
                values[1] ?? defaultValue,
            ];
        case "List":
            return values.length > 0 ? [...values] : [];
    }
}

/**
 * Default blank value for a value kind — string is empty, number
 * kinds render as "" so the NumberInput starts empty, boolean
 * defaults to "true" (Mantine `Switch` treats it as on).
 */
export function defaultValueFor(kind: FilterValueKind): string {
    switch (kind) {
        case "Boolean":
            return "true";
        case "DateTimeUtc":
            return "";
        default:
            return "";
    }
}
