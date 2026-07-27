import type { ComponentType } from "react";
import { CommentTile } from "./CommentTile";
import { PanelYieldTile } from "./PanelYieldTile";
import { ParetoTile } from "./ParetoTile";
import type { TileType } from "../tileTypes";

/**
 * Props every canvas tile component accepts. `config` is the tile's
 * opaque `configJson` string (the per-tile analytic knobs — see
 * `components/reportConfig/tileConfig.ts`). Report-level filters
 * (source / window / machine / product) still arrive through
 * `useCanvasFilters()`, never through props.
 */
export type TileProps = {
    /** The tile's stored `configJson`, or `undefined` for defaults. */
    config?: string;
};

/**
 * Runtime tile catalogue used by `<ReportCanvas>` to render each
 * tile by its `TileType`. Kept in a `.tsx` sibling of the pure-data
 * `tileTypes.ts` so consumers that only need the type list (e.g.
 * URL validators, tests) can import from `tileTypes.ts` without
 * pulling any React components into their bundle.
 */
export const TILE_REGISTRY: Readonly<Record<TileType, ComponentType<TileProps>>> = {
    panelYield: PanelYieldTile,
    pareto: ParetoTile,
    comment: CommentTile,
};
