import { Fragment, useCallback, useMemo } from "react";
import {
    ActionIcon,
    Alert,
    Button,
    Group,
    NumberInput,
    Select,
    Stack,
    Switch,
    TagsInput,
    Text,
    TextInput,
    Tooltip,
} from "@mantine/core";
import { DateTimePicker } from "@mantine/dates";
import "@mantine/dates/styles.css";
import { IconAlertCircle, IconPlus, IconX } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    FILTER_FIELDS,
    coerceValuesForArity,
    defaultOperatorFor,
    defaultValueFor,
    getAllowedOperators,
    getFieldValueKind,
    getOperatorArity,
    operatorSupportsValueKind,
    validateClause,
    type FilterClause,
    type FilterField,
    type FilterOperator,
    type FilterOperatorArity,
    type FilterValueKind,
} from "./filterMetadata";

/**
 * F11 filter builder component (docs/phase-2.md §7.9).
 *
 * Renders an editable stack of {@link FilterClause} rows honouring
 * the Vieweb §3.1.2 operator matrix:
 *  - The field picker exposes every {@link FilterField} the caller
 *    hasn't excluded via `fields`.
 *  - The operator picker for each row is *restricted* to the
 *    operators Vieweb allowed on that field.
 *  - Value controls switch on operator arity (Single / List /
 *    Range) and field value kind (String / Integer / Decimal /
 *    DateTimeUtc / Boolean).
 *  - When the user changes the field, the operator is snapped to
 *    the first allowed one for the new field, and the values array
 *    is resized to the new arity — nothing dangling is ever
 *    emitted.
 *
 * The component is fully controlled: state lives in the parent
 * (`value`) and updates flow through `onChange`. Consumers submit
 * `{ clauses: value }` to any endpoint that accepts the server
 * `FilterRequest`.
 *
 * Server-side {@link FilterValidator} remains the authoritative
 * gate — this component's client-side validation is only a
 * usability aid.
 */
export type FilterBuilderProps = {
    /** Current clause list. */
    value: FilterClause[];
    /** Called on every add / edit / remove. */
    onChange: (next: FilterClause[]) => void;
    /**
     * Optional subset of fields to expose (useful when a report
     * only understands a slice of the matrix). Defaults to
     * every field.
     */
    fields?: readonly FilterField[];
    /**
     * Test-id root for the outer container and each row. Row
     * testids follow `${testIdRoot}-row-${index}`.
     */
    testIdRoot?: string;
};

const DEFAULT_TEST_ID = "filter-builder";

export function FilterBuilder(props: FilterBuilderProps) {
    const { t } = useTranslation();
    const rootId = props.testIdRoot ?? DEFAULT_TEST_ID;
    const availableFields = props.fields ?? FILTER_FIELDS;

    const fieldOptions = useMemo(
        () =>
            availableFields.map((f) => ({
                value: f,
                label: t(`filters.builder.fields.${f}`),
            })),
        [availableFields, t],
    );

    const addClause = useCallback(() => {
        const field = availableFields[0] ?? FILTER_FIELDS[0];
        const operator = defaultOperatorFor(field);
        const kind = getFieldValueKind(field);
        const arity = getOperatorArity(operator);
        const values = coerceValuesForArity(
            [],
            arity,
            defaultValueFor(kind),
        );
        // "List" arity starts empty on purpose so the user has to
        // add at least one value before submitting.
        props.onChange([...props.value, { field, operator, values }]);
    }, [availableFields, props]);

    const patchClause = useCallback(
        (index: number, patch: Partial<FilterClause>) => {
            const next = [...props.value];
            const current = next[index];
            const merged: FilterClause = { ...current, ...patch };

            // If the field changed, snap operator + values.
            if (patch.field && patch.field !== current.field) {
                const allowed = getAllowedOperators(patch.field);
                const newOperator = allowed.includes(current.operator)
                    ? current.operator
                    : defaultOperatorFor(patch.field);
                const newKind = getFieldValueKind(patch.field);
                const finalOp = operatorSupportsValueKind(
                    newOperator,
                    newKind,
                )
                    ? newOperator
                    : defaultOperatorFor(patch.field);
                const arity = getOperatorArity(finalOp);
                merged.operator = finalOp;
                merged.values = coerceValuesForArity(
                    current.values,
                    arity,
                    defaultValueFor(newKind),
                );
            } else if (patch.operator && patch.operator !== current.operator) {
                const arity = getOperatorArity(patch.operator);
                const kind = getFieldValueKind(merged.field);
                merged.values = coerceValuesForArity(
                    current.values,
                    arity,
                    defaultValueFor(kind),
                );
            }

            next[index] = merged;
            props.onChange(next);
        },
        [props],
    );

    const removeClause = useCallback(
        (index: number) => {
            const next = [...props.value];
            next.splice(index, 1);
            props.onChange(next);
        },
        [props],
    );

    return (
        <Stack gap="sm" data-testid={rootId}>
            {props.value.length === 0 && (
                <Text c="dimmed" size="sm" data-testid={`${rootId}-empty`}>
                    {t("filters.builder.emptyState")}
                </Text>
            )}

            {props.value.map((clause, index) => (
                <ClauseRow
                    key={index}
                    rootId={rootId}
                    index={index}
                    clause={clause}
                    fieldOptions={fieldOptions}
                    onFieldChange={(field) => patchClause(index, { field })}
                    onOperatorChange={(operator) =>
                        patchClause(index, { operator })
                    }
                    onValuesChange={(values) =>
                        patchClause(index, { values })
                    }
                    onRemove={() => removeClause(index)}
                />
            ))}

            <Group>
                <Button
                    variant="light"
                    leftSection={<IconPlus size={16} />}
                    onClick={addClause}
                    data-testid={`${rootId}-add`}
                >
                    {t("filters.builder.addClause")}
                </Button>
            </Group>
        </Stack>
    );
}

type FieldOption = { value: FilterField; label: string };

function ClauseRow(props: {
    rootId: string;
    index: number;
    clause: FilterClause;
    fieldOptions: FieldOption[];
    onFieldChange: (field: FilterField) => void;
    onOperatorChange: (operator: FilterOperator) => void;
    onValuesChange: (values: string[]) => void;
    onRemove: () => void;
}) {
    const { t } = useTranslation();
    const {
        rootId,
        index,
        clause,
        fieldOptions,
        onFieldChange,
        onOperatorChange,
        onValuesChange,
        onRemove,
    } = props;

    const operatorOptions = useMemo(
        () =>
            getAllowedOperators(clause.field).map((op) => ({
                value: op,
                label: t(`filters.builder.operators.${op}`),
            })),
        [clause.field, t],
    );

    const kind = getFieldValueKind(clause.field);
    const arity = getOperatorArity(clause.operator);
    const validation = validateClause(clause);
    const rowError = validation.errors.find((e) => e.key === "operator");
    const valuesError = validation.errors.find((e) => e.key === "values");

    return (
        <Fragment>
            <Group
                align="flex-end"
                wrap="nowrap"
                data-testid={`${rootId}-row-${index}`}
            >
                <Select
                    label={index === 0 ? t("filters.builder.field") : undefined}
                    data={fieldOptions}
                    value={clause.field}
                    onChange={(v) => {
                        if (v) onFieldChange(v as FilterField);
                    }}
                    allowDeselect={false}
                    searchable
                    w={260}
                    data-testid={`${rootId}-field-${index}`}
                />
                <Select
                    label={
                        index === 0 ? t("filters.builder.operator") : undefined
                    }
                    data={operatorOptions}
                    value={clause.operator}
                    onChange={(v) => {
                        if (v) onOperatorChange(v as FilterOperator);
                    }}
                    allowDeselect={false}
                    w={180}
                    data-testid={`${rootId}-operator-${index}`}
                    error={
                        rowError
                            ? t(rowError.messageKey as never, {
                                  field: t(
                                      `filters.builder.fields.${clause.field}` as never,
                                  ),
                                  operator: t(
                                      `filters.builder.operators.${clause.operator}` as never,
                                  ),
                              })
                            : undefined
                    }
                />
                <ValuesControl
                    rootId={rootId}
                    index={index}
                    kind={kind}
                    arity={arity}
                    values={clause.values}
                    onChange={onValuesChange}
                    withLabel={index === 0}
                    error={
                        valuesError
                            ? t(valuesError.messageKey as never)
                            : undefined
                    }
                />
                <Tooltip label={t("filters.builder.removeClause")}>
                    <ActionIcon
                        variant="subtle"
                        color="red"
                        onClick={onRemove}
                        aria-label={t("filters.builder.removeClause")}
                        data-testid={`${rootId}-remove-${index}`}
                    >
                        <IconX size={16} />
                    </ActionIcon>
                </Tooltip>
            </Group>
        </Fragment>
    );
}

function ValuesControl(props: {
    rootId: string;
    index: number;
    kind: FilterValueKind;
    arity: FilterOperatorArity;
    values: string[];
    onChange: (next: string[]) => void;
    withLabel: boolean;
    error?: string;
}) {
    const { t } = useTranslation();
    const { rootId, index, kind, arity, values, onChange, withLabel, error } =
        props;

    const singleLabel = withLabel ? t("filters.builder.value") : undefined;
    const minLabel = withLabel ? t("filters.builder.valueMin") : undefined;
    const maxLabel = withLabel ? t("filters.builder.valueMax") : undefined;

    if (arity === "List") {
        return (
            <TagsInput
                label={
                    withLabel ? t("filters.builder.valueList") : undefined
                }
                placeholder={t("filters.builder.valueListPlaceholder")}
                value={values}
                onChange={onChange}
                clearable
                w={280}
                data-testid={`${rootId}-values-${index}`}
                error={error}
            />
        );
    }

    if (arity === "Range") {
        return (
            <Group gap="xs" wrap="nowrap" data-testid={`${rootId}-values-${index}`}>
                <SingleValueInput
                    kind={kind}
                    value={values[0] ?? ""}
                    onChange={(v) => onChange([v, values[1] ?? ""])}
                    label={minLabel}
                    testId={`${rootId}-value-min-${index}`}
                    error={error}
                />
                <SingleValueInput
                    kind={kind}
                    value={values[1] ?? ""}
                    onChange={(v) => onChange([values[0] ?? "", v])}
                    label={maxLabel}
                    testId={`${rootId}-value-max-${index}`}
                />
            </Group>
        );
    }

    return (
        <SingleValueInput
            kind={kind}
            value={values[0] ?? ""}
            onChange={(v) => onChange([v])}
            label={singleLabel}
            testId={`${rootId}-value-${index}`}
            error={error}
        />
    );
}

function SingleValueInput(props: {
    kind: FilterValueKind;
    value: string;
    onChange: (next: string) => void;
    label?: string;
    testId: string;
    error?: string;
}) {
    const { kind, value, onChange, label, testId, error } = props;
    const { t } = useTranslation();

    if (kind === "Integer" || kind === "Decimal") {
        return (
            <NumberInput
                label={label}
                value={value === "" ? "" : Number(value)}
                onChange={(v) => {
                    if (v === "" || v === null || v === undefined) {
                        onChange("");
                    } else {
                        onChange(String(v));
                    }
                }}
                allowDecimal={kind === "Decimal"}
                w={160}
                data-testid={testId}
                error={error}
            />
        );
    }

    if (kind === "DateTimeUtc") {
        return (
            <DateTimePicker
                label={label}
                value={value === "" ? null : value}
                onChange={(v) => onChange(v ? String(v) : "")}
                valueFormat="YYYY-MM-DD HH:mm"
                clearable
                w={200}
                data-testid={testId}
                error={error}
            />
        );
    }

    if (kind === "Boolean") {
        return (
            <Switch
                label={label ?? t("filters.builder.valueBoolean")}
                checked={value === "true"}
                onChange={(event) =>
                    onChange(event.currentTarget.checked ? "true" : "false")
                }
                data-testid={testId}
            />
        );
    }

    // Fallback: String
    return (
        <TextInput
            label={label}
            placeholder={t("filters.builder.valuePlaceholder")}
            value={value}
            onChange={(event) => onChange(event.currentTarget.value)}
            w={220}
            data-testid={testId}
            error={error}
        />
    );
}

/**
 * Small helper for callers that want to surface a top-level
 * warning banner summarising invalid clauses. Not rendered by
 * {@link FilterBuilder} itself so the parent can control layout.
 */
export function FilterBuilderErrorSummary(props: { clauses: FilterClause[] }) {
    const { t } = useTranslation();
    const invalid = props.clauses
        .map((c, i) => ({ index: i, result: validateClause(c) }))
        .filter((x) => !x.result.ok);

    if (invalid.length === 0) return null;

    return (
        <Alert
            color="red"
            icon={<IconAlertCircle size={16} />}
            title={t("filters.builder.errors.summaryTitle")}
            data-testid="filter-builder-error-summary"
        >
            {t("filters.builder.errors.summaryBody", { count: invalid.length })}
        </Alert>
    );
}
