import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { MantineProvider } from "@mantine/core";
import { I18nextProvider } from "react-i18next";
import { CanvasFilterProvider, type CanvasFilters } from "./FilterContext";
import {
    ReportCanvas,
    newTileId,
    type CanvasTile,
} from "./ReportCanvas";
import i18n from "../../i18n";

// Stub the tile registry so the canvas tests focus on canvas behaviour
// (add / reorder / remove) instead of exercising each tile's data
// fetching. Real tiles are exercised via their own tests + the
// canvas-demo route integration.
vi.mock("./tiles/registry", () => ({
    TILE_REGISTRY: {
        panelYield: () => (
            <div data-testid="stub-panelYield">panel-yield-stub</div>
        ),
        pareto: () => <div data-testid="stub-pareto">pareto-stub</div>,
    },
}));

const NO_FILTERS: CanvasFilters = {};

function wrapperWithProviders({ children }: { children: React.ReactNode }) {
    return (
        <I18nextProvider i18n={i18n}>
            <MantineProvider>
                <CanvasFilterProvider
                    filters={NO_FILTERS}
                    setFilters={() => {}}
                >
                    {children}
                </CanvasFilterProvider>
            </MantineProvider>
        </I18nextProvider>
    );
}

/**
 * Controlled harness: renders `<ReportCanvas>` with an internal
 * `useState` tile list so tests can drive add / reorder / remove
 * through the UI and observe the resulting state.
 */
function Harness(props: { initial?: CanvasTile[] }) {
    const [tiles, setTiles] = useState<CanvasTile[]>(props.initial ?? []);
    return <ReportCanvas tiles={tiles} onTilesChange={setTiles} />;
}

describe("ReportCanvas", () => {
    it("renders the empty-state prompt when the tile list is empty", () => {
        render(<Harness />, { wrapper: wrapperWithProviders });
        expect(
            screen.getByText(/no tiles yet/i),
        ).toBeInTheDocument();
    });

    it("adds a tile to the end of the list when a palette entry is clicked", async () => {
        const user = userEvent.setup();
        render(<Harness />, { wrapper: wrapperWithProviders });

        // Open the palette menu. Mantine renders the dropdown into a
        // portal with `display: none` while the popover animation is
        // in-flight, so query for menuitems directly with
        // `hidden: true` rather than waiting on `role="menu"`
        // (matches DataTable.test.tsx's pattern).
        await user.click(screen.getByRole("button", { name: /add tile/i }));
        const items = await screen.findAllByRole("menuitem", { hidden: true });
        const paretoItem = items.find((i) =>
            /pareto/i.test(i.textContent ?? ""),
        );
        expect(paretoItem).toBeDefined();
        await user.click(paretoItem!);

        expect(screen.getByTestId("stub-pareto")).toBeInTheDocument();
        expect(screen.queryByText(/no tiles yet/i)).not.toBeInTheDocument();
    });

    it("removes a tile when its remove button is clicked", async () => {
        const user = userEvent.setup();
        render(
            <Harness
                initial={[
                    { id: newTileId(), type: "panelYield" },
                    { id: newTileId(), type: "pareto" },
                ]}
            />,
            { wrapper: wrapperWithProviders },
        );

        expect(screen.getByTestId("stub-panelYield")).toBeInTheDocument();
        expect(screen.getByTestId("stub-pareto")).toBeInTheDocument();

        const panelYieldCard = screen.getByTestId("canvas-tile-panelYield");
        const removeButton = within(panelYieldCard).getByRole("button", {
            name: /remove tile/i,
        });
        await user.click(removeButton);

        expect(screen.queryByTestId("stub-panelYield")).not.toBeInTheDocument();
        expect(screen.getByTestId("stub-pareto")).toBeInTheDocument();
    });

    it("moves a tile down when its move-down button is clicked", async () => {
        const user = userEvent.setup();
        render(
            <Harness
                initial={[
                    { id: newTileId(), type: "panelYield" },
                    { id: newTileId(), type: "pareto" },
                ]}
            />,
            { wrapper: wrapperWithProviders },
        );

        const tilesBefore = document.querySelectorAll(
            "[data-testid^='canvas-tile-']",
        );
        expect(
            Array.from(tilesBefore).map((el) =>
                el.getAttribute("data-testid"),
            ),
        ).toEqual(["canvas-tile-panelYield", "canvas-tile-pareto"]);

        const firstCard = screen.getByTestId("canvas-tile-panelYield");
        await user.click(
            within(firstCard).getByRole("button", { name: /move down/i }),
        );

        const tilesAfter = document.querySelectorAll(
            "[data-testid^='canvas-tile-']",
        );
        expect(
            Array.from(tilesAfter).map((el) =>
                el.getAttribute("data-testid"),
            ),
        ).toEqual(["canvas-tile-pareto", "canvas-tile-panelYield"]);
    });

    it("disables move-up on the first tile and move-down on the last tile", () => {
        render(
            <Harness
                initial={[
                    { id: newTileId(), type: "panelYield" },
                    { id: newTileId(), type: "pareto" },
                ]}
            />,
            { wrapper: wrapperWithProviders },
        );

        const first = screen.getByTestId("canvas-tile-panelYield");
        const last = screen.getByTestId("canvas-tile-pareto");
        expect(
            within(first).getByRole("button", { name: /move up/i }),
        ).toBeDisabled();
        expect(
            within(last).getByRole("button", { name: /move down/i }),
        ).toBeDisabled();
    });
});
