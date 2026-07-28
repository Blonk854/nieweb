/**
 * Guided, form-based editor for a report tile's `configJson`. Renders
 * the {@link ./tileConfigSchema} field list for the given tile type as
 * plain Mantine controls (selects / checkbox / number) so a
 * non-technical author never edits raw JSON. Values are read and
 * written exclusively through the typed contract in {@link ./tileConfig}
 * so the emitted `configJson` always matches what the canvas and the
 * server-side export consume.
 *
 * Tiles without a schema (e.g. `comment`, or a future tile that has no
 * form yet) render nothing here — the parent editor falls back to the
 * "Advanced (JSON)" textarea for those.
 */
import { Checkbox, NumberInput, Select, Stack } from "@mantine/core";
import { useTranslation } from "react-i18next";

import type { TileType } from "../canvas/tileTypes";
import {
    PARETO_TILE_DEFAULT,
    parsePanelYieldTileConfig,
    parseParetoTileConfig,
    serializePanelYieldTileConfig,
    serializeParetoTileConfig,
} from "./tileConfig";
import { TILE_CONFIG_SCHEMAS, type TileConfigField } from "./tileConfigSchema";
import type {
    ParetoAxis,
    ParetoNumerator,
    ParetoOpportunity,
    ParetoWeight,
} from "../../routes/pareto.search";

type ConfigRecord = Record<string, unknown>;

/** Decode the stored `configJson` into a flat record the fields bind to. */
function toRecord(tileType: string, value: string): ConfigRecord {
    if (tileType === "pareto") {
        const c = parseParetoTileConfig(value);
        return {
            axis: c.axis,
            numerator: c.numerator,
            opportunity: c.opportunity,
            weight: c.weight,
            topN: c.topN,
            vitalFewThreshold: c.vitalFewThreshold,
        };
    }
    if (tileType === "panelYield") {
        const c = parsePanelYieldTileConfig(value);
        return { onlyLastInspection: c.onlyLastInspection };
    }
    return {};
}

/** Re-encode an edited record back to a normalised `configJson` string. */
function fromRecord(tileType: string, rec: ConfigRecord, originalValue: string): string {
    if (tileType === "pareto") {
        return serializeParetoTileConfig({
            axis: rec.axis as ParetoAxis,
            numerator: rec.numerator as ParetoNumerator,
            opportunity: rec.opportunity as ParetoOpportunity,
            weight: rec.weight as ParetoWeight,
            topN: typeof rec.topN === "number" ? rec.topN : undefined,
            vitalFewThreshold:
                typeof rec.vitalFewThreshold === "number"
                    ? rec.vitalFewThreshold
                    : PARETO_TILE_DEFAULT.vitalFewThreshold,
            // Preserve Old-school per-entity filters the modern form
            // does not edit, so editing analytic knobs never wipes them.
            filters: parseParetoTileConfig(originalValue).filters,
        });
    }
    if (tileType === "panelYield") {
        return serializePanelYieldTileConfig({
            onlyLastInspection: Boolean(rec.onlyLastInspection),
            filters: parsePanelYieldTileConfig(originalValue).filters,
        });
    }
    return JSON.stringify(rec);
}

export function TileConfigForm(props: {
    tileType: string;
    value: string;
    onChange: (nextConfigJson: string) => void;
}) {
    const { tileType, value, onChange } = props;
    const schema = TILE_CONFIG_SCHEMAS[tileType as TileType];

    if (!schema) {
        return null;
    }

    const record = toRecord(tileType, value);
    const update = (key: string, next: unknown) => {
        onChange(fromRecord(tileType, { ...record, [key]: next }, value));
    };

    return (
        <Stack gap="sm" data-testid={`tile-config-form-${tileType}`}>
            {schema.map((field) => (
                <FieldControl
                    key={field.key}
                    field={field}
                    record={record}
                    onChange={update}
                />
            ))}
        </Stack>
    );
}

function FieldControl(props: {
    field: TileConfigField;
    record: ConfigRecord;
    onChange: (key: string, next: unknown) => void;
}) {
    const { field, record, onChange } = props;
    const { t } = useTranslation();
    // Schema label / help / option keys are built from template strings so
    // they are typed as `string`; they are all valid bundle keys, but the
    // strict `t` overloads only accept literal keys, so narrow here.
    const tr = t as unknown as (key: string) => string;
    const current = record[field.key];

    if (field.kind === "select") {
        return (
            <Select
                data-testid={`tile-config-${field.key}`}
                label={tr(field.labelKey)}
                description={tr(field.helpKey)}
                data={field.options.map((o) => ({
                    value: o.value,
                    label: tr(o.labelKey),
                }))}
                value={typeof current === "string" ? current : null}
                onChange={(v) => {
                    if (v !== null) onChange(field.key, v);
                }}
                allowDeselect={false}
            />
        );
    }

    if (field.kind === "checkbox") {
        return (
            <Checkbox
                data-testid={`tile-config-${field.key}`}
                label={tr(field.labelKey)}
                description={tr(field.helpKey)}
                checked={Boolean(current)}
                onChange={(e) => onChange(field.key, e.currentTarget.checked)}
            />
        );
    }

    // number
    return (
        <NumberInput
            data-testid={`tile-config-${field.key}`}
            label={tr(field.labelKey)}
            description={tr(field.helpKey)}
            placeholder={tr(field.placeholderKey)}
            min={field.min}
            allowDecimal={false}
            value={typeof current === "number" ? current : ""}
            onChange={(v) => onChange(field.key, typeof v === "number" ? v : undefined)}
        />
    );
}
