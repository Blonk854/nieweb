import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import {
    createMemoryHistory,
    createRootRoute,
    createRoute,
    createRouter,
    Outlet,
    RouterProvider,
} from "@tanstack/react-router";
import i18n from "../i18n";
import { BarcodeLookupCard } from "./BarcodeLookupCard";

/**
 * Test harness that mounts `<BarcodeLookupCard />` alongside a stub
 * `/traceability/board` route so navigation can be observed through
 * the router's `state.location`.
 */
function renderCard() {
    const rootRoute = createRootRoute({ component: Outlet });
    const homeRoute = createRoute({
        getParentRoute: () => rootRoute,
        path: "/",
        component: BarcodeLookupCard,
    });
    const boardRoute = createRoute({
        getParentRoute: () => rootRoute,
        path: "/traceability/board",
        component: () => null,
        validateSearch: (raw: Record<string, unknown>) => ({
            barcode: typeof raw.barcode === "string" ? raw.barcode : undefined,
        }),
    });
    const routeTree = rootRoute.addChildren([homeRoute, boardRoute]);
    const history = createMemoryHistory({ initialEntries: ["/"] });
    const router = createRouter({ routeTree, history });
    render(
        <MantineProvider>
            <RouterProvider router={router} />
        </MantineProvider>,
    );
    return { router };
}

describe("BarcodeLookupCard", () => {
    beforeEach(() => {
        void i18n.changeLanguage("en");
    });
    afterEach(() => {
        cleanup();
        vi.restoreAllMocks();
    });

    it("renders the barcode form with an accessible input", async () => {
        renderCard();
        expect(
            await screen.findByRole("heading", {
                level: 4,
                name: /look up a panel by barcode/i,
            }),
        ).toBeInTheDocument();
        expect(screen.getByTestId("home-barcode-input")).toBeInTheDocument();
        expect(screen.getByTestId("home-barcode-submit")).toBeInTheDocument();
    });

    it("shows a validation error when submitted empty", async () => {
        renderCard();
        fireEvent.click(await screen.findByTestId("home-barcode-submit"));
        expect(await screen.findByText(/please enter a barcode/i)).toBeInTheDocument();
    });

    it("shows a validation error for a barcode longer than 64 chars", async () => {
        renderCard();
        const input = await screen.findByTestId("home-barcode-input");
        // Mantine TextInput enforces maxLength via HTML, so we bypass
        // it by dispatching the change event with a longer value —
        // this proves the JS guard fires as a defence-in-depth.
        fireEvent.change(input, { target: { value: "x".repeat(65) } });
        fireEvent.click(screen.getByTestId("home-barcode-submit"));
        expect(
            await screen.findByText(/must be 64 characters or fewer/i),
        ).toBeInTheDocument();
    });

    it("navigates to /traceability/board with the trimmed barcode on submit", async () => {
        const { router } = renderCard();
        const input = await screen.findByTestId("home-barcode-input");
        fireEvent.change(input, { target: { value: "  ABC-123  " } });
        fireEvent.click(screen.getByTestId("home-barcode-submit"));

        await vi.waitFor(() => {
            const loc = router.state.location;
            expect(loc.pathname).toBe("/traceability/board");
            expect(loc.search).toMatchObject({ barcode: "ABC-123" });
        });
    });
});
