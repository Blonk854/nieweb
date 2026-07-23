import type { ComponentType } from "react";
import { CommentTile } from "./CommentTile";
import { PanelYieldTile } from "./PanelYieldTile";
import { ParetoTile } from "./ParetoTile";
import type { TileType } from "../tileTypes";

/**
 * Runtime tile catalogue used by `<ReportCanvas>` to render each
 * tile by its `TileType`. Kept in a `.tsx` sibling of the pure-data
 * `tileTypes.ts` so consumers that only need the type list (e.g.
 * URL validators, tests) can import from `tileTypes.ts` without
 * pulling any React components into their bundle.
 */
export const TILE_REGISTRY: Readonly<Record<TileType, ComponentType>> = {
    panelYield: PanelYieldTile,
    pareto: ParetoTile,
    comment: CommentTile,
};
