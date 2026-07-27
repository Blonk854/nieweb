/**
 * Canonical per-tile configuration contract shared by the canvas
 * tiles ({@link ../canvas/tiles}), the guided report editor forms
 * ({@link ./TileConfigForm}), and the starter templates
 * ({@link ./reportTemplates}). The server-side export path parses the
 * same JSON shape in `ReportEndpoints.TileConfig.cs`, so the numbers a
 * user sees on screen and the numbers in the exported PDF / CSV / XLSX
 * stay identical (KPI parity).
 *
 * A tile's `configJson` string carries ONLY the tile-specific analytic
 * knobs. The report-level filters (source, time window, machine /
 * product narrowing) come from the canvas filter fanout
 * ({@link ../canvas/FilterContext}) at view time and are never baked
 * into a tile.
 *
 * Every parser here is deliberately total: malformed JSON, missing
 * fields, and unknown enum values all fall back to the documented
 * defaults so one bad tile can never blank a report.
 */
import {
    PARETO_AXES,
    PARETO_NUMERATORS,
    PARETO_OPPORTUNITIES,
    PARETO_WEIGHTS,
    type ParetoAxis,
    type ParetoNumerator,
    type ParetoOpportunity,
    type ParetoWeight,
} from "../../routes/pareto.search";

// -------------------- panelYield --------------------

/** Tile-specific config for the `panelYield` tile. */
export type PanelYieldTileConfig = {
    /**
     * Post-reflow only: restrict to each panel's last inspection pass.
     * Ignored by pre-reflow sources (no `IS_LAST_INSPECTION` column).
     */
    onlyLastInspection: boolean;
};

export const PANEL_YIELD_TILE_DEFAULT: PanelYieldTileConfig = {
    onlyLastInspection: true,
};

// -------------------- pareto --------------------

/**
 * Tile-specific config for the `pareto` tile. Defaults match the
 * "DPMO real defects" view the stand-alone `/report/pareto` route and
 * the canvas tile already use, so an unconfigured tile renders the
 * boss-approved default.
 */
export type ParetoTileConfig = {
    axis: ParetoAxis;
    numerator: ParetoNumerator;
    opportunity: ParetoOpportunity;
    weight: ParetoWeight;
    /** Cap on visible bars; excess rolls into an Others bucket. `undefined` = no cap. */
    topN?: number;
    /** Cumulative-% threshold that highlights the "vital few". */
    vitalFewThreshold: number;
};

export const PARETO_TILE_DEFAULT: ParetoTileConfig = {
    axis: "Defect",
    numerator: "Real",
    opportunity: "Components",
    weight: "Count",
    topN: 10,
    vitalFewThreshold: 80,
};

// -------------------- comment --------------------

/** Tile-specific config for the `comment` tile. */
export type CommentTileConfig = {
    markdown: string;
};

export const COMMENT_TILE_DEFAULT: CommentTileConfig = { markdown: "" };

// -------------------- parsers --------------------

/**
 * Parse an arbitrary `configJson` string into a JSON object, or
 * `undefined` when the input is empty / malformed / not an object.
 */
function parseObject(configJson: string | null | undefined): Record<string, unknown> | undefined {
    if (typeof configJson !== "string" || configJson.trim().length === 0) {
        return undefined;
    }
    try {
        const parsed: unknown = JSON.parse(configJson);
        if (parsed !== null && typeof parsed === "object" && !Array.isArray(parsed)) {
            return parsed as Record<string, unknown>;
        }
    } catch {
        // fall through to undefined — callers substitute defaults
    }
    return undefined;
}

function readEnum<T extends string>(
    raw: unknown,
    allowed: readonly T[],
    fallback: T,
): T {
    if (typeof raw === "string") {
        const match = allowed.find((v) => v.toLowerCase() === raw.toLowerCase());
        if (match) return match;
    }
    return fallback;
}

function readBool(raw: unknown, fallback: boolean): boolean {
    if (typeof raw === "boolean") return raw;
    if (raw === "true") return true;
    if (raw === "false") return false;
    return fallback;
}

function readTopN(raw: unknown, fallback: number | undefined): number | undefined {
    if (raw === null) return undefined;
    if (typeof raw === "number" && Number.isFinite(raw) && Number.isInteger(raw)) {
        return raw > 0 ? raw : undefined;
    }
    if (typeof raw === "string" && raw.trim().length > 0) {
        const n = Number(raw);
        if (Number.isInteger(n)) return n > 0 ? n : undefined;
    }
    return fallback;
}

function readNumber(raw: unknown, fallback: number): number {
    if (typeof raw === "number" && Number.isFinite(raw)) return raw;
    if (typeof raw === "string" && raw.trim().length > 0) {
        const n = Number(raw);
        if (Number.isFinite(n)) return n;
    }
    return fallback;
}

/** Parse a `panelYield` tile's config, substituting defaults per field. */
export function parsePanelYieldTileConfig(
    configJson: string | null | undefined,
): PanelYieldTileConfig {
    const obj = parseObject(configJson);
    if (!obj) return { ...PANEL_YIELD_TILE_DEFAULT };
    return {
        onlyLastInspection: readBool(
            obj.onlyLastInspection,
            PANEL_YIELD_TILE_DEFAULT.onlyLastInspection,
        ),
    };
}

/** Parse a `pareto` tile's config, substituting defaults per field. */
export function parseParetoTileConfig(
    configJson: string | null | undefined,
): ParetoTileConfig {
    const obj = parseObject(configJson);
    if (!obj) return { ...PARETO_TILE_DEFAULT };
    return {
        axis: readEnum<ParetoAxis>(obj.axis, PARETO_AXES, PARETO_TILE_DEFAULT.axis),
        numerator: readEnum<ParetoNumerator>(
            obj.numerator,
            PARETO_NUMERATORS,
            PARETO_TILE_DEFAULT.numerator,
        ),
        opportunity: readEnum<ParetoOpportunity>(
            obj.opportunity,
            PARETO_OPPORTUNITIES,
            PARETO_TILE_DEFAULT.opportunity,
        ),
        weight: readEnum<ParetoWeight>(
            obj.weight,
            PARETO_WEIGHTS,
            PARETO_TILE_DEFAULT.weight,
        ),
        topN: readTopN(obj.topN, PARETO_TILE_DEFAULT.topN),
        vitalFewThreshold: readNumber(
            obj.vitalFewThreshold,
            PARETO_TILE_DEFAULT.vitalFewThreshold,
        ),
    };
}

/** Parse a `comment` tile's config (markdown body). */
export function parseCommentTileConfig(
    configJson: string | null | undefined,
): CommentTileConfig {
    const obj = parseObject(configJson);
    if (!obj) return { ...COMMENT_TILE_DEFAULT };
    return {
        markdown: typeof obj.markdown === "string" ? obj.markdown : COMMENT_TILE_DEFAULT.markdown,
    };
}

// -------------------- serialisers --------------------

/**
 * Serialise a config object back to a compact JSON string. `configJson`
 * is stored verbatim, so we keep the shape stable (sorted-ish key order
 * driven by the type) and drop `undefined` fields.
 */
export function serializePanelYieldTileConfig(config: PanelYieldTileConfig): string {
    return JSON.stringify({ onlyLastInspection: config.onlyLastInspection });
}

export function serializeParetoTileConfig(config: ParetoTileConfig): string {
    return JSON.stringify({
        axis: config.axis,
        numerator: config.numerator,
        opportunity: config.opportunity,
        weight: config.weight,
        // Emit `null` (not omitted) for "no cap" so a fully-serialised
        // config round-trips losslessly: an absent key means "inherit
        // the default", whereas `null` means the author cleared the cap.
        topN: typeof config.topN === "number" && config.topN > 0 ? config.topN : null,
        vitalFewThreshold: config.vitalFewThreshold,
    });
}

export function serializeCommentTileConfig(config: CommentTileConfig): string {
    return JSON.stringify({ markdown: config.markdown });
}
