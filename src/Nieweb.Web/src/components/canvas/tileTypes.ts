/**
 * Fixed tile catalogue for the F10 report canvas.
 *
 * A `TileType` is the canonical string that flows through the URL
 * (see `routes/canvas-demo.search.ts`), so it must never change
 * without a migration — adding new tile types is safe, renaming
 * or removing existing ones breaks bookmarks.
 */
export type TileType = "panelYield" | "pareto" | "comment";

/** Order used to render the palette. */
export const TILE_TYPES: readonly TileType[] = ["panelYield", "pareto", "comment"];

/**
 * Metadata every tile ships with. `TILE_REGISTRY` maps `TileType`
 * to its React component; `TILE_LABEL_KEYS` maps to the i18n key
 * used in the palette and in tile headings. Splitting the maps
 * keeps the pure-data side importable from search / URL modules
 * without pulling in React.
 */
export const TILE_LABEL_KEYS = {
    panelYield: "canvas.tiles.panelYield.title",
    pareto: "canvas.tiles.pareto.title",
    comment: "canvas.tiles.comment.title",
} as const satisfies Readonly<Record<TileType, string>>;

/**
 * Coerce an arbitrary string (typically taken straight from the
 * URL) into a `TileType`, dropping unknown values. Returns
 * `undefined` when the input is not a supported tile type.
 */
export function toTileType(raw: unknown): TileType | undefined {
    return typeof raw === "string" &&
        (TILE_TYPES as readonly string[]).includes(raw)
        ? (raw as TileType)
        : undefined;
}
