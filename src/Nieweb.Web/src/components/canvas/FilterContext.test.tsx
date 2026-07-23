import { describe, expect, it, vi } from "vitest";
import { render, renderHook, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import {
    CanvasFilterProvider,
    canvasFiltersReady,
    useCanvasFilters,
    type CanvasFilters,
} from "./FilterContext";

const SAMPLE_FILTERS: CanvasFilters = {
    sourceId: "postreflow",
    startUtc: "2026-07-01T00:00:00Z",
    endUtc: "2026-07-02T00:00:00Z",
};

describe("CanvasFilterProvider", () => {
    it("publishes the current filters and setter to descendant hooks", () => {
        const setFilters = vi.fn();
        const wrapper = ({ children }: { children: ReactNode }) => (
            <CanvasFilterProvider
                filters={SAMPLE_FILTERS}
                setFilters={setFilters}
            >
                {children}
            </CanvasFilterProvider>
        );
        const { result } = renderHook(() => useCanvasFilters(), { wrapper });
        expect(result.current.filters).toEqual(SAMPLE_FILTERS);
        result.current.setFilters({ ...SAMPLE_FILTERS, sourceId: "prereflow" });
        expect(setFilters).toHaveBeenCalledWith({
            ...SAMPLE_FILTERS,
            sourceId: "prereflow",
        });
    });

    it("renders children", () => {
        render(
            <CanvasFilterProvider
                filters={SAMPLE_FILTERS}
                setFilters={() => {}}
            >
                <span>tile</span>
            </CanvasFilterProvider>,
        );
        expect(screen.getByText("tile")).toBeInTheDocument();
    });
});

describe("useCanvasFilters", () => {
    it("throws when used outside a CanvasFilterProvider", () => {
        // Silence the expected React error boundary noise.
        const spy = vi
            .spyOn(console, "error")
            .mockImplementation(() => {});
        expect(() => renderHook(() => useCanvasFilters())).toThrow(
            /CanvasFilterProvider/,
        );
        spy.mockRestore();
    });
});

describe("canvasFiltersReady", () => {
    it("returns true only when source, start and end are all present", () => {
        expect(canvasFiltersReady({})).toBe(false);
        expect(canvasFiltersReady({ sourceId: "s" })).toBe(false);
        expect(
            canvasFiltersReady({ sourceId: "s", startUtc: "2026-07-01T00:00:00Z" }),
        ).toBe(false);
        expect(canvasFiltersReady(SAMPLE_FILTERS)).toBe(true);
    });
});
