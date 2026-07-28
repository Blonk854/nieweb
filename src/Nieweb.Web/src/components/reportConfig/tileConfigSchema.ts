/**
 * Declarative field metadata for the guided report-tile editor
 * ({@link ./TileConfigForm}). Each entry describes the plain-language
 * label, help text and (for selects) option list of one tile-specific
 * config knob. The values themselves are read / written through the
 * typed contract in {@link ./tileConfig}, so the schema only carries
 * presentation — never parsing logic.
 *
 * Option value strings match the canonical enum names the API accepts
 * (see `routes/pareto.search.ts`). The `labelKey` on each option maps
 * to a plain-language i18n string so non-technical users never see the
 * raw enum name (e.g. "Real" renders as "Real defects (after review)").
 */
import {
    PARETO_AXES,
    PARETO_NUMERATORS,
    PARETO_OPPORTUNITIES,
    PARETO_WEIGHTS,
} from "../../routes/pareto.search";
import type { TileType } from "../canvas/tileTypes";

export type TileConfigOption = {
    /** Canonical enum value stored in `configJson`. */
    value: string;
    /** i18n key for the plain-language option label. */
    labelKey: string;
};

export type TileConfigField =
    | {
          kind: "select";
          key: string;
          labelKey: string;
          helpKey: string;
          options: readonly TileConfigOption[];
      }
    | {
          kind: "checkbox";
          key: string;
          labelKey: string;
          helpKey: string;
      }
    | {
          kind: "number";
          key: string;
          labelKey: string;
          helpKey: string;
          min: number;
          placeholderKey: string;
      };

const CONFIG_ROOT = "admin.reports.editor.tiles.config";

function paretoOptions(
    field: string,
    values: readonly string[],
): readonly TileConfigOption[] {
    return values.map((value) => ({
        value,
        labelKey: `${CONFIG_ROOT}.pareto.${field}.options.${value}`,
    }));
}

/**
 * Axes offered on a Pareto *tile*. Excludes the time-bucketing axes
 * (`Day` / `Shift`) from {@link PARETO_AXES}: those need a site time
 * zone / shift schedule that a saved tile cannot carry, and the export
 * path would otherwise throw when it runs the tile (see
 * `RunParetoForTileAsync`). They remain available on the stand-alone
 * `/report/pareto` view which has the extra controls.
 */
const TILE_PARETO_AXES: readonly string[] = PARETO_AXES.filter(
    (a) => a !== "Day" && a !== "Shift",
);

/**
 * Ordered field list per tile type. Tiles absent from this map (e.g.
 * `comment`, or future tiles without a form yet) fall back to the raw
 * "Advanced (JSON)" editor in {@link ./TileConfigForm}.
 */
export const TILE_CONFIG_SCHEMAS: Partial<Record<TileType, readonly TileConfigField[]>> = {
    panelYield: [
        {
            kind: "checkbox",
            key: "onlyLastInspection",
            labelKey: `${CONFIG_ROOT}.panelYield.onlyLastInspection.label`,
            helpKey: `${CONFIG_ROOT}.panelYield.onlyLastInspection.help`,
        },
    ],
    pareto: [
        {
            kind: "select",
            key: "axis",
            labelKey: `${CONFIG_ROOT}.pareto.axis.label`,
            helpKey: `${CONFIG_ROOT}.pareto.axis.help`,
            options: paretoOptions("axis", TILE_PARETO_AXES),
        },
        {
            kind: "select",
            key: "numerator",
            labelKey: `${CONFIG_ROOT}.pareto.numerator.label`,
            helpKey: `${CONFIG_ROOT}.pareto.numerator.help`,
            options: paretoOptions("numerator", PARETO_NUMERATORS),
        },
        {
            kind: "select",
            key: "opportunity",
            labelKey: `${CONFIG_ROOT}.pareto.opportunity.label`,
            helpKey: `${CONFIG_ROOT}.pareto.opportunity.help`,
            options: paretoOptions("opportunity", PARETO_OPPORTUNITIES),
        },
        {
            kind: "select",
            key: "weight",
            labelKey: `${CONFIG_ROOT}.pareto.weight.label`,
            helpKey: `${CONFIG_ROOT}.pareto.weight.help`,
            options: paretoOptions("weight", PARETO_WEIGHTS),
        },
        {
            kind: "number",
            key: "topN",
            labelKey: `${CONFIG_ROOT}.pareto.topN.label`,
            helpKey: `${CONFIG_ROOT}.pareto.topN.help`,
            min: 1,
            placeholderKey: `${CONFIG_ROOT}.pareto.topN.placeholder`,
        },
    ],
};

/** True when a tile type has a guided form (vs. Advanced-JSON only). */
export function hasTileConfigForm(tileType: string): boolean {
    return Object.prototype.hasOwnProperty.call(TILE_CONFIG_SCHEMAS, tileType);
}
