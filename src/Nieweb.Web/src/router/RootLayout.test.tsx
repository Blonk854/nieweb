import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { cleanup, render, screen, within } from "@testing-library/react";
import { userEvent } from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
    createMemoryHistory,
    createRootRoute,
    createRoute,
    createRouter,
    RouterProvider,
} from "@tanstack/react-router";

import i18n from "../i18n";
import { RootLayout } from "./RootLayout";
import { useSessionStore } from "../state/session";

/**
 * Smoke coverage for the RootLayout SideNav grouping introduced by
 * the Settings decluttering pass:
 *
 *  - The seven admin + account items live inside a single collapsible
 *    "Settings" NavLink (except Reports, which stays at the top level
 *    because it is content authoring, not settings).
 *  - The Settings parent renders whenever the current viewer would
 *    see at least one child (any signed-in user for Change password,
 *    any admin for the six admin items).
 *  - The "App parameters" label was renamed to "MSA parameters".
 *  - Deep-linking to an admin route auto-expands the Settings branch.
 */

// Bare-bones route tree: we only exercise RootLayout's SideNav, so
// every real route is stubbed with a placeholder that renders its
// path. That keeps the tests independent of route-level fetches
// (auth guards, /api/sources, etc.).
function makeRouter(initialPath: string) {
    const rootRoute = createRootRoute({ component: RootLayout });
    const stub = (label: string) => () => (
        <div data-testid={`route-${label}`}>{label}</div>
    );
    const children = [
        "/",
        "/report/panel-yield",
        "/report/pareto",
        "/report/canvas-demo",
        "/traceability/board",
        "/admin/users",
        "/admin/audit",
        "/admin/reports",
        "/admin/board-svgs",
        "/admin/parameters",
        "/admin/production-lines",
        "/admin/shifts",
        "/settings/timezone",
        "/account/password",
        "/login",
    ].map((path) =>
        createRoute({
            getParentRoute: () => rootRoute,
            path,
            component: stub(path),
        }),
    );
    const routeTree = rootRoute.addChildren(children);
    return createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: [initialPath] }),
    });
}

function renderLayoutAt(initialPath: string) {
    const router = makeRouter(initialPath);
    const client = new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
        },
    });
    return render(
        <MantineProvider>
            <QueryClientProvider client={client}>
                <RouterProvider router={router} />
            </QueryClientProvider>
        </MantineProvider>,
    );
}

function signInAs(roles: readonly string[]) {
    useSessionStore.setState({
        user: {
            email: "test@nieweb.local",
            displayName: "Test User",
            roles,
            mustRotatePassword: false,
        },
        token: "test-token",
    });
}

function signOut() {
    useSessionStore.setState({ user: null, token: null });
}

// Mantine's NavLink lazy-renders its collapse children: when closed,
// they aren't in the DOM at all. Tests that want to probe children
// need to expand the branch first. This helper does that with a
// single click on the parent link.
async function expandSettings() {
    const branch = await screen.findByTestId("nav-settings-branch");
    await userEvent.click(within(branch).getByTestId("nav-settings"));
    return branch;
}

describe("RootLayout SideNav", () => {
    beforeEach(async () => {
        await i18n.changeLanguage("en");
    });
    afterEach(() => {
        cleanup();
        signOut();
    });

    it("hides the Settings group entirely for anonymous visitors", () => {
        signOut();
        renderLayoutAt("/");
        expect(
            screen.queryByTestId("nav-settings-branch"),
        ).not.toBeInTheDocument();
        expect(screen.queryByTestId("nav-settings")).not.toBeInTheDocument();
        expect(screen.queryByText("Settings")).not.toBeInTheDocument();
    });

    it("moves the active highlight to the current route (no locked item)", async () => {
        signInAs(["Reader"]);
        renderLayoutAt("/");
        const user = userEvent.setup();

        const panelYield = await screen.findByRole("link", {
            name: "Panel Yield by Line",
        });
        const pareto = screen.getByRole("link", { name: "Pareto" });

        await user.click(panelYield);
        expect(panelYield).toHaveAttribute("data-active");
        expect(pareto).not.toHaveAttribute("data-active");

        // Selecting another item must release the previous highlight —
        // the reported bug left the first-clicked item locked as active.
        await user.click(pareto);
        expect(pareto).toHaveAttribute("data-active");
        expect(panelYield).not.toHaveAttribute("data-active");
    });

    it("shows Timezone and Change password under Settings for non-admin users", async () => {
        signInAs(["Reader"]);
        renderLayoutAt("/");
        const branch = await expandSettings();
        // Non-admin never gets any of the six admin children.
        expect(within(branch).queryByText("Users")).not.toBeInTheDocument();
        expect(
            within(branch).queryByText("Audit trail"),
        ).not.toBeInTheDocument();
        expect(
            within(branch).queryByText("Panel SVGs"),
        ).not.toBeInTheDocument();
        expect(
            within(branch).queryByText("Production lines"),
        ).not.toBeInTheDocument();
        expect(within(branch).queryByText("Shifts")).not.toBeInTheDocument();
        expect(
            within(branch).queryByText("MSA parameters"),
        ).not.toBeInTheDocument();
        expect(
            within(branch).queryByText("Databases"),
        ).not.toBeInTheDocument();
        // Every signed-in user (admin or not) gets Timezone + Change
        // password — both are per-account/browser preferences.
        expect(within(branch).getByText("Timezone")).toBeInTheDocument();
        expect(
            within(branch).getByText("Change password"),
        ).toBeInTheDocument();
    });

    it("groups all six admin items + Timezone + Change password under Settings for admins", async () => {
        signInAs(["Admin"]);
        renderLayoutAt("/");
        const branch = await expandSettings();
        // Every admin sub-item lives inside the Settings branch.
        for (const label of [
            "Users",
            "Audit trail",
            "Panel SVGs",
            "Production lines",
            "Shifts",
            "MSA parameters",
            "Timezone",
            "Databases",
            "Change password",
        ]) {
            expect(within(branch).getByText(label)).toBeInTheDocument();
        }
    });

    it("keeps Reports as a top-level nav item (outside Settings)", async () => {
        signInAs(["Admin"]);
        renderLayoutAt("/");
        const branch = await expandSettings();
        // Reports is admin-only but intentionally lives at the top level
        // because it is content authoring, not settings.
        expect(screen.getByText("Reports")).toBeInTheDocument();
        expect(within(branch).queryByText("Reports")).not.toBeInTheDocument();
    });

    it("renames the parameters link to 'MSA parameters'", async () => {
        signInAs(["Admin"]);
        renderLayoutAt("/");
        await expandSettings();
        expect(screen.getByText("MSA parameters")).toBeInTheDocument();
        expect(screen.queryByText("App parameters")).not.toBeInTheDocument();
    });

    it("auto-opens Settings when the current route is an admin sub-page", async () => {
        signInAs(["Admin"]);
        renderLayoutAt("/admin/users");
        // Deep-linking to a child route should render its Link inside
        // the Settings branch and have the collapse expanded so the
        // user immediately sees where they are (no expand click needed).
        const branch = await screen.findByTestId("nav-settings-branch");
        const usersLink = within(branch).getByText("Users").closest("a");
        expect(usersLink).not.toBeNull();
        expect(usersLink).toBeVisible();
    });

    it("auto-opens Settings when the current route is /settings/timezone", async () => {
        signInAs(["Reader"]);
        renderLayoutAt("/settings/timezone");
        // The Timezone route is under /settings/, not /admin/ or
        // /account/, so this proves the settingsActive matcher covers
        // the new prefix as well.
        const branch = await screen.findByTestId("nav-settings-branch");
        const tzLink = within(branch).getByText("Timezone").closest("a");
        expect(tzLink).not.toBeNull();
        expect(tzLink).toBeVisible();
    });

    it("keeps Settings collapsed on non-admin routes but expands on click", async () => {
        signInAs(["Admin"]);
        renderLayoutAt("/");
        const branch = await screen.findByTestId("nav-settings-branch");
        // Home page → Settings starts closed. Mantine lazy-renders
        // children, so before expansion no child text is in the DOM.
        expect(within(branch).queryByText("Users")).not.toBeInTheDocument();
        // Clicking the parent toggles the collapse open and mounts
        // the children. (Visibility under Mantine's CSS transition is
        // unreliable in jsdom; DOM presence is the contract we care
        // about — the "auto-opens" test already covers `toBeVisible`.)
        await userEvent.click(within(branch).getByTestId("nav-settings"));
        expect(within(branch).getByText("Users")).toBeInTheDocument();
    });
});
