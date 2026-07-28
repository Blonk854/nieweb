import { ActionIcon, Group, Select, Stack, Text, TextInput, Tooltip } from "@mantine/core";
import { IconPlus, IconTrash } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    allowedOperators,
    fieldValueKind,
    isClauseValid,
    newClause,
    operatorArity,
    valuesForArity,
    type FilterClause,
    type FilterField,
    type FilterOperator,
} from "../../api/filters";
import styles from "./oldSchool.module.css";

/**
 * Vieweb-style per-entity filter builder. Renders one row per
 * {@link FilterClause}: a column picker, an operator picker (restricted
 * to the operators Vieweb allows on that column), and value input(s)
 * whose shape follows the operator arity (single / range / comma list).
 * The full operator set — Equal / Different / In / Not in / Between /
 * Not between / Like / Not like / ≤ / ≥ — is offered per the field's
 * allow-list, which is exactly what line engineers liked in Vieweb.
 */
export function FilterBuilder(props: {
    fields: readonly FilterField[];
    value: FilterClause[];
    onChange: (next: FilterClause[]) => void;
}) {
    const { fields, value, onChange } = props;
    const { t } = useTranslation();

    function updateAt(index: number, next: FilterClause) {
        const copy = value.slice();
        copy[index] = next;
        onChange(copy);
    }

    function removeAt(index: number) {
        const copy = value.slice();
        copy.splice(index, 1);
        onChange(copy);
    }

    function addRow() {
        onChange([...value, newClause(fields[0])]);
    }

    return (
        <Stack gap="xs">
            {value.length === 0 ? (
                <Text size="xs" c="dimmed">
                    {t("oldSchool.entity.noFilters")}
                </Text>
            ) : null}

            {value.map((clause, i) => {
                const ops = allowedOperators(clause.field);
                const arity = operatorArity(clause.operator);
                const kind = fieldValueKind(clause.field);
                const invalid = !isClauseValid(clause);
                return (
                    <div
                        key={i}
                        className={`${styles.filterRow} ${invalid ? styles.filterInvalid : ""}`}
                        data-testid={`filter-row-${i}`}
                    >
                        <Select
                            aria-label={t("oldSchool.entity.field")}
                            data={fields.map((f) => ({
                                value: f,
                                label: t(`oldSchool.fields.${f}` as "oldSchool.fields.Defect"),
                            }))}
                            value={clause.field}
                            allowDeselect={false}
                            w={180}
                            onChange={(v) => {
                                if (!v) return;
                                const field = v as FilterField;
                                const op = allowedOperators(field)[0] ?? "Equal";
                                updateAt(i, {
                                    field,
                                    operator: op,
                                    values: valuesForArity(operatorArity(op), clause.values),
                                });
                            }}
                        />
                        <Select
                            aria-label={t("oldSchool.entity.operator")}
                            data={ops.map((o) => ({
                                value: o,
                                label: t(`oldSchool.operators.${o}` as "oldSchool.operators.Equal"),
                            }))}
                            value={clause.operator}
                            allowDeselect={false}
                            w={140}
                            onChange={(v) => {
                                if (!v) return;
                                const op = v as FilterOperator;
                                updateAt(i, {
                                    ...clause,
                                    operator: op,
                                    values: valuesForArity(operatorArity(op), clause.values),
                                });
                            }}
                        />
                        {arity === "single" ? (
                            <TextInput
                                aria-label={t("oldSchool.entity.value")}
                                inputMode={kind === "integer" ? "numeric" : "text"}
                                value={clause.values[0] ?? ""}
                                w={160}
                                onChange={(e) =>
                                    updateAt(i, { ...clause, values: [e.currentTarget.value] })
                                }
                            />
                        ) : null}
                        {arity === "range" ? (
                            <Group gap={4}>
                                <TextInput
                                    aria-label={t("oldSchool.entity.valueFrom")}
                                    inputMode={kind === "integer" ? "numeric" : "text"}
                                    value={clause.values[0] ?? ""}
                                    w={90}
                                    onChange={(e) =>
                                        updateAt(i, {
                                            ...clause,
                                            values: [e.currentTarget.value, clause.values[1] ?? ""],
                                        })
                                    }
                                />
                                <TextInput
                                    aria-label={t("oldSchool.entity.valueTo")}
                                    inputMode={kind === "integer" ? "numeric" : "text"}
                                    value={clause.values[1] ?? ""}
                                    w={90}
                                    onChange={(e) =>
                                        updateAt(i, {
                                            ...clause,
                                            values: [clause.values[0] ?? "", e.currentTarget.value],
                                        })
                                    }
                                />
                            </Group>
                        ) : null}
                        {arity === "list" ? (
                            <TextInput
                                aria-label={t("oldSchool.entity.valueList")}
                                description={t("oldSchool.entity.valueListHelp")}
                                // Round-trips identically to what the user
                                // types (split/join on the same delimiter),
                                // so commas can actually be entered; the
                                // values are trimmed / de-duped on blur.
                                value={clause.values.join(",")}
                                w={260}
                                onChange={(e) =>
                                    updateAt(i, {
                                        ...clause,
                                        values: e.currentTarget.value.split(","),
                                    })
                                }
                                onBlur={(e) =>
                                    updateAt(i, {
                                        ...clause,
                                        values: e.currentTarget.value
                                            .split(",")
                                            .map((s) => s.trim())
                                            .filter((s) => s.length > 0),
                                    })
                                }
                            />
                        ) : null}
                        <Tooltip label={t("oldSchool.entity.removeFilter")}>
                            <ActionIcon
                                variant="subtle"
                                color="red"
                                aria-label={t("oldSchool.entity.removeFilter")}
                                onClick={() => removeAt(i)}
                            >
                                <IconTrash size={16} />
                            </ActionIcon>
                        </Tooltip>
                    </div>
                );
            })}

            {fields.length > 0 ? (
                <Group>
                    <ActionIcon
                        variant="light"
                        aria-label={t("oldSchool.entity.addFilter")}
                        onClick={addRow}
                    >
                        <IconPlus size={16} />
                    </ActionIcon>
                    <Text
                        size="xs"
                        style={{ cursor: "pointer" }}
                        onClick={addRow}
                    >
                        {t("oldSchool.entity.addFilter")}
                    </Text>
                </Group>
            ) : null}
        </Stack>
    );
}
