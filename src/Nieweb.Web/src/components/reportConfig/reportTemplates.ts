/**
 * Starter report templates offered in the "New report" dialog so a
 * non-technical author can begin from a working example instead of a
 * blank canvas. Templates are plain client-side data (this slice); the
 * dialog expands the chosen template by creating the report with the
 * template's default chrome and then adding each tile via the existing
 * admin endpoints.
 *
 * Tile titles are intentionally left to the tile catalogue default
 * (stored `title` = null) so a template is language-agnostic. Tile
 * `configJson` uses `"{}"` where the tile's canonical defaults are
 * wanted, or an explicit serialized config for a non-default shape.
 */
import type { TileType } from "../canvas/tileTypes";
import { serializeParetoTileConfig, PARETO_TILE_DEFAULT } from "./tileConfig";

export type ReportTemplateTile = {
    tileType: TileType;
    configJson: string;
};

export type ReportTemplate = {
    /** Stable id used as the gallery selection key. */
    id: string;
    nameKey: string;
    descKey: string;
    /** Default chrome (source / window preset) baked into the new report. */
    chromeJson: string | null;
    tiles: readonly ReportTemplateTile[];
};

/** All templates open to the last-7-days window by default. */
const LAST7D_CHROME = JSON.stringify({ defaultWindowPreset: "last7d" });

/** A machine-axis Pareto (vs. the default defect axis) for variety. */
const PARETO_BY_MACHINE = serializeParetoTileConfig({
    ...PARETO_TILE_DEFAULT,
    axis: "AoiMachine",
});

export const REPORT_TEMPLATES: readonly ReportTemplate[] = [
    {
        id: "blank",
        nameKey: "admin.reports.list.create.template.blank.name",
        descKey: "admin.reports.list.create.template.blank.desc",
        chromeJson: null,
        tiles: [],
    },
    {
        id: "yield-overview",
        nameKey: "admin.reports.list.create.template.yieldOverview.name",
        descKey: "admin.reports.list.create.template.yieldOverview.desc",
        chromeJson: LAST7D_CHROME,
        tiles: [{ tileType: "panelYield", configJson: "{}" }],
    },
    {
        id: "top-defects",
        nameKey: "admin.reports.list.create.template.topDefects.name",
        descKey: "admin.reports.list.create.template.topDefects.desc",
        chromeJson: LAST7D_CHROME,
        tiles: [{ tileType: "pareto", configJson: "{}" }],
    },
    {
        id: "yield-and-defects",
        nameKey: "admin.reports.list.create.template.yieldAndDefects.name",
        descKey: "admin.reports.list.create.template.yieldAndDefects.desc",
        chromeJson: LAST7D_CHROME,
        tiles: [
            { tileType: "panelYield", configJson: "{}" },
            { tileType: "pareto", configJson: "{}" },
        ],
    },
    {
        id: "defects-by-machine",
        nameKey: "admin.reports.list.create.template.defectsByMachine.name",
        descKey: "admin.reports.list.create.template.defectsByMachine.desc",
        chromeJson: LAST7D_CHROME,
        tiles: [{ tileType: "pareto", configJson: PARETO_BY_MACHINE }],
    },
];

export const DEFAULT_TEMPLATE_ID = "blank";
