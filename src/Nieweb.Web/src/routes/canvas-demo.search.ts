import type { SourceInfo } from "../api/sources";
import { TILE_TYPES, toTileType, type TileType } from "../components/canvas/tileTypes";

/**
 * URL-serialised state for the F10 canvas-demo route.
 *
 * The demo route hosts a `<CanvasFilterProvider>` and a
 * `<ReportCanvas>`. Every input that changes what is rendered
 * (source, window, narrowing filters, tile list + order) is
 * mirrored here so the whole dashboard is bookmarkable /
 * shareable, matching the pattern used by the Panel Yield and
 * Pareto routes.
 */
export type CanvasDemoSearch = {
    /** `SourceDescriptor.Id`, case-insensitive. */
    sourceId?: string;
    /** ISO-8601 instant; inclusive lower bound. */
    startUtc?: string;
    /** ISO-8601 instant; exclusive upper bound. */
    endUtc?: string;
    /** Optional MACHINE.Machine_Id list. */
    machineIds?: number[];
    /** Optional PRODUCT.Product_Id list. */
    productIds?: number[];
    /**
     * Ordered list of tile types laid out on the canvas. Empty
     * (or missing) means "no tiles" and the canvas will render
     * the empty-state prompt.
     */
    tiles?: TileType[];
};

/**
 * Emit the URL query-string form of a `CanvasDemoSearch`. Arrays
 * are joined with commas — the same shape used by the other
 * report routes so users can copy filter fragments between URLs.
 */
export function toApiQuery(search: CanvasDemoSearch): Record<string, string> {
    const out: Record<string, string> = {};
    if (search.sourceId) out.sourceId = search.sourceId;
    if (search.startUtc) out.startUtc = search.startUtc;
    if (search.endUtc) out.endUtc = search.endUtc;
    if (search.machineIds && search.machineIds.length > 0) {
        out.machineIds = search.machineIds.join(",");
    }
    if (search.productIds && search.productIds.length > 0) {
        out.productIds = search.productIds.join(",");
    }
    if (search.tiles && search.tiles.length > 0) {
        out.tiles = search.tiles.join(",");
    }
    return out;
}

/**
 * TanStack Router `validateSearch` for the canvas-demo route.
 * Unknown / malformed keys are dropped rather than throwing so a
 * stale bookmark never renders a hard error.
 */
export function validateCanvasDemoSearch(
    raw: Record<string, unknown>,
): CanvasDemoSearch {
    return {
        sourceId: toStringOrUndef(raw.sourceId),
        startUtc: toStringOrUndef(raw.startUtc),
        endUtc: toStringOrUndef(raw.endUtc),
        machineIds: toNumberArray(raw.machineIds),
        productIds: toNumberArray(raw.productIds),
        tiles: toTileArray(raw.tiles),
    };
}

function toStringOrUndef(v: unknown): string | undefined {
    if (typeof v !== "string") return undefined;
    const trimmed = v.trim();
    return trimmed.length > 0 ? trimmed : undefined;
}

function toNumberArray(v: unknown): number[] | undefined {
    if (Array.isArray(v)) {
        const nums = v
            .map((x) => (typeof x === "string" || typeof x === "number" ? Number(x) : NaN))
            .filter((n) => Number.isFinite(n) && Number.isInteger(n));
        return nums.length > 0 ? nums : undefined;
    }
    if (typeof v === "string") {
        const nums = v
            .split(",")
            .map((s) => s.trim())
            .filter((s) => s.length > 0)
            .map((s) => Number(s))
            .filter((n) => Number.isFinite(n) && Number.isInteger(n));
        return nums.length > 0 ? nums : undefined;
    }
    return undefined;
}

function toTileArray(v: unknown): TileType[] | undefined {
    const raws = Array.isArray(v)
        ? v
        : typeof v === "string"
          ? v.split(",")
          : [];
    const tiles = raws
        .map((s) => (typeof s === "string" ? s.trim() : ""))
        .map((s) => toTileType(s))
        .filter((t): t is TileType => t !== undefined);
    return tiles.length > 0 ? tiles : undefined;
}

/**
 * Choose the first available source id when the URL is blank —
 * same rule used by the other report routes. Kept co-located so
 * canvas tests can seed identical defaults.
 */
export function pickDefaultSourceId(
    sources: readonly SourceInfo[],
): string | undefined {
    if (sources.length === 0) return undefined;
    return (sources.find((s) => s.available) ?? sources[0]).id;
}

/** Re-export so route + tests share the same tile-type source. */
export { TILE_TYPES };
