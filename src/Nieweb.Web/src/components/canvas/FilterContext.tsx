import { createContext, useContext, useMemo, type ReactNode } from "react";

/**
 * Filters that fan out to every tile inside a `<ReportCanvas>`.
 *
 * Tiles never carry their own source / window / narrowing filters
 * — they read this context and re-run their query whenever the
 * canvas-level filters change. That is the "filter fanout"
 * behaviour called out for F10 in `docs/phase-2.md` §7.9: one
 * filter form drives an arbitrary number of report tiles below it.
 *
 * All fields are optional so a canvas can render an empty state
 * (no source picked yet) without every tile throwing.
 */
export type CanvasFilters = {
    /** `SourceDescriptor.Id` — case-insensitive; matches `/api/sources`. */
    sourceId?: string;
    /** Inclusive lower bound in ISO-8601 UTC. */
    startUtc?: string;
    /** Exclusive upper bound in ISO-8601 UTC. */
    endUtc?: string;
    /** Optional per-machine narrowing (Superviseur `MACHINE.Machine_Id`). */
    machineIds?: number[];
    /** Optional per-product narrowing (Superviseur `PRODUCT.Product_Id`). */
    productIds?: number[];
};

/**
 * Contract exposed to consumers of `<CanvasFilterProvider>`. Callers
 * treat the value as read-only; updates flow through `setFilters`
 * (which is expected to also push the change into the URL, so a
 * canvas is bookmarkable).
 */
export type CanvasFilterContextValue = {
    filters: CanvasFilters;
    setFilters: (filters: CanvasFilters) => void;
};

const CanvasFilterContext = createContext<CanvasFilterContextValue | null>(
    null,
);

/**
 * Provider that publishes canvas-level filters to descendant tiles.
 *
 * `<CanvasFilterProvider>` intentionally memoises its value on
 * `filters` + `setFilters` — this keeps tile subtrees from
 * re-rendering on every parent re-render when the filter object
 * reference is stable.
 */
export function CanvasFilterProvider(props: {
    filters: CanvasFilters;
    setFilters: (filters: CanvasFilters) => void;
    children: ReactNode;
}) {
    const value = useMemo<CanvasFilterContextValue>(
        () => ({ filters: props.filters, setFilters: props.setFilters }),
        [props.filters, props.setFilters],
    );
    return (
        <CanvasFilterContext.Provider value={value}>
            {props.children}
        </CanvasFilterContext.Provider>
    );
}

/**
 * Hook used by every tile to read (and, rarely, to update) the
 * canvas-level filter fan-out.
 *
 * Throws when called outside a `<CanvasFilterProvider>` so tiles
 * cannot silently fall through to a stale-looking empty state
 * during development.
 */
export function useCanvasFilters(): CanvasFilterContextValue {
    const ctx = useContext(CanvasFilterContext);
    if (!ctx) {
        throw new Error(
            "useCanvasFilters must be called inside a <CanvasFilterProvider>.",
        );
    }
    return ctx;
}

/**
 * Are the canvas filters populated enough for a tile to run a
 * report? Every tile shares the same three required fields:
 * `sourceId`, `startUtc`, `endUtc`. Tests exercising empty-state
 * rendering can call this directly to assert their branch.
 */
export function canvasFiltersReady(filters: CanvasFilters): boolean {
    return Boolean(filters.sourceId && filters.startUtc && filters.endUtc);
}
