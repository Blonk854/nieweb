import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Mock } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import {
    createMemoryHistory,
    createRootRoute,
    createRoute,
    createRouter,
    Outlet,
    RouterProvider,
} from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import i18n from "../i18n";
import { HomeRoute } from "./home";
import { useSessionStore } from "../state/session";

/**
 * Component-level tests for the RC4 home route pinned-reports card.
 * All network calls are stubbed via `vi.stubGlobal("fetch", …)`.
 */

type FetchResponse = {
    match: (url: string, init?: RequestInit) => boolean;
    status: number;
    body?: unknown;
};

function stubFetch(responses: FetchResponse[]): Mock {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url =
            typeof input === "string"
                ? input
                : input instanceof URL
                    ? input.toString()
                    : input.url;
        const hit = responses.find((r) => r.match(url, init));
        if (!hit) {
            throw new Error(`Unexpected fetch: ${init?.method ?? "GET"} ${url}`);
        }
        const bodyText =
            hit.body === undefined
                ? ""
                : typeof hit.body === "string"
                    ? hit.body
                    : JSON.stringify(hit.body);
        return new Response(bodyText, {
            status: hit.status,
            statusText: hit.status === 200 ? "OK" : "Error",
            headers: { "Content-Type": "application/json" },
        });
    });
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock as Mock;
}

function renderHome() {
    const rootRoute = createRootRoute({ component: Outlet });
    const homeRoute = createRoute({
        getParentRoute: () => rootRoute,
        path: "/",
        component: HomeRoute,
    });
    // The pinned-report tile links to /admin/reports/$id; register a
    // stub so TanStack Router can resolve it without navigating.
    const editorRoute = createRoute({
        getParentRoute: () => rootRoute,
        path: "/admin/reports/$id",
        component: () => null,
    });
    // panel-yield Trans link inside the intro.
    const pyRoute = createRoute({
        getParentRoute: () => rootRoute,
        path: "/report/panel-yield",
        component: () => null,
    });
    const routeTree = rootRoute.addChildren([homeRoute, editorRoute, pyRoute]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: ["/"] }),
    });
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

describe("HomeRoute pinned-reports card", () => {
    beforeEach(() => {
        void i18n.changeLanguage("en");
    });

    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it("shows an empty-state message when no reports are pinned", async () => {
        stubFetch([
            { match: (u) => u.endsWith("/api/reports/home"), status: 200, body: [] },
            { match: (u) => u.endsWith("/api/sources"), status: 200, body: [] },
        ]);
        renderHome();
        expect(
            await screen.findByText(/no reports have been pinned/i),
        ).toBeInTheDocument();
    });

    it("renders one tile per pinned report and links to the editor", async () => {
        stubFetch([
            {
                match: (u) => u.endsWith("/api/reports/home"),
                status: 200,
                body: [
                    {
                        id: 42,
                        title: "SMT overview",
                        description: "First-pass yield across every AOI line",
                        reportGroupId: null,
                        groupName: null,
                        ownerDisplayName: "Root",
                        isLocked: false,
                        refreshFrequencySeconds: null,
                        displayOrder: 0,
                        entityCount: 4,
                        lastModifiedUtc: new Date().toISOString(),
                    },
                    {
                        id: 43,
                        title: "Weekly Pareto",
                        description: null,
                        reportGroupId: null,
                        groupName: "Weekly",
                        ownerDisplayName: "Root",
                        isLocked: false,
                        refreshFrequencySeconds: null,
                        displayOrder: 1,
                        entityCount: 1,
                        lastModifiedUtc: new Date().toISOString(),
                    },
                ],
            },
            { match: (u) => u.endsWith("/api/sources"), status: 200, body: [] },
        ]);
        renderHome();

        const tile1 = await screen.findByTestId("home-pinned-report-42");
        expect(tile1).toHaveTextContent(/SMT overview/i);
        expect(tile1.querySelector("a")?.getAttribute("href")).toBe("/admin/reports/42");

        const tile2 = await screen.findByTestId("home-pinned-report-43");
        expect(tile2).toHaveTextContent(/Weekly Pareto/i);
        expect(tile2.querySelector("a")?.getAttribute("href")).toBe("/admin/reports/43");
    });

    it("renders a locked badge on locked pinned reports", async () => {
        stubFetch([
            {
                match: (u) => u.endsWith("/api/reports/home"),
                status: 200,
                body: [
                    {
                        id: 7,
                        title: "Locked report",
                        description: null,
                        reportGroupId: null,
                        groupName: null,
                        ownerDisplayName: "Root",
                        isLocked: true,
                        refreshFrequencySeconds: null,
                        displayOrder: 0,
                        entityCount: 2,
                        lastModifiedUtc: new Date().toISOString(),
                    },
                ],
            },
            { match: (u) => u.endsWith("/api/sources"), status: 200, body: [] },
        ]);
        renderHome();
        const tile = await screen.findByTestId("home-pinned-report-7");
        expect(tile).toHaveTextContent(/locked/i);
    });

    it("shows an error alert when the pinned list fails to load", async () => {
        stubFetch([
            { match: (u) => u.endsWith("/api/reports/home"), status: 500, body: "boom" },
            { match: (u) => u.endsWith("/api/sources"), status: 200, body: [] },
        ]);
        renderHome();
        await waitFor(async () => {
            const alerts = await screen.findAllByRole("alert");
            expect(
                alerts.some((a) => /could not load pinned reports/i.test(a.textContent ?? "")),
            ).toBe(true);
        });
    });
});

/**
 * F14 unpin action. The unpin button is admin-only and issues a
 * POST /api/admin/reports/{id}/unpin, then invalidates the pinned
 * query.
 */
describe("HomeRoute F14 unpin action", () => {
    beforeEach(() => {
        void i18n.changeLanguage("en");
    });

    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
        useSessionStore.setState({ user: null, token: null });
    });

    function pinnedReport(id: number, title: string) {
        return {
            id,
            title,
            description: null,
            reportGroupId: null,
            groupName: null,
            ownerDisplayName: "Root",
            isLocked: false,
            refreshFrequencySeconds: null,
            displayOrder: 0,
            entityCount: 1,
            lastModifiedUtc: new Date().toISOString(),
        };
    }

    it("hides the unpin action for non-admin users", async () => {
        useSessionStore.setState({
            user: {
                email: "reader@t.test",
                displayName: "Reader",
                roles: ["Reader"],
                mustRotatePassword: false,
            },
            token: "tok",
        });
        stubFetch([
            {
                match: (u) => u.endsWith("/api/reports/home"),
                status: 200,
                body: [pinnedReport(42, "Read only")],
            },
            { match: (u) => u.endsWith("/api/sources"), status: 200, body: [] },
        ]);
        renderHome();
        await screen.findByTestId("home-pinned-report-42");
        expect(screen.queryByTestId("home-pinned-unpin-42")).toBeNull();
    });

    it("shows the unpin action for admin users", async () => {
        useSessionStore.setState({
            user: {
                email: "admin@t.test",
                displayName: "Admin",
                roles: ["Admin"],
                mustRotatePassword: false,
            },
            token: "tok",
        });
        stubFetch([
            {
                match: (u) => u.endsWith("/api/reports/home"),
                status: 200,
                body: [pinnedReport(42, "Admin visible")],
            },
            { match: (u) => u.endsWith("/api/sources"), status: 200, body: [] },
        ]);
        renderHome();
        expect(await screen.findByTestId("home-pinned-unpin-42")).toBeInTheDocument();
    });

    it("calls POST /api/admin/reports/{id}/unpin when admin clicks unpin", async () => {
        useSessionStore.setState({
            user: {
                email: "admin@t.test",
                displayName: "Admin",
                roles: ["Admin"],
                mustRotatePassword: false,
            },
            token: "tok",
        });
        let homeCallCount = 0;
        const fetchMock = stubFetch([
            {
                match: (u) => u.endsWith("/api/reports/home"),
                status: 200,
                body: [pinnedReport(42, "To be unpinned")],
            },
            { match: (u) => u.endsWith("/api/sources"), status: 200, body: [] },
            {
                match: (u, init) =>
                    u.endsWith("/api/admin/reports/42/unpin") && init?.method === "POST",
                status: 200,
                body: {
                    id: 42,
                    title: "To be unpinned",
                    description: null,
                    reportGroupId: null,
                    groupName: null,
                    ownerUserId: null,
                    ownerDisplayName: "Root",
                    isLocked: false,
                    isPinnedHome: false,
                    refreshFrequencySeconds: null,
                    chromeJson: null,
                    displayOrder: 0,
                    entityCount: 1,
                    createdUtc: new Date().toISOString(),
                    lastModifiedUtc: new Date().toISOString(),
                },
            },
        ]);
        // Bump the home stub so that after the invalidation the second
        // fetch returns an empty list, mirroring "the tile disappeared".
        renderHome();
        const btn = await screen.findByTestId("home-pinned-unpin-42");
        homeCallCount = fetchMock.mock.calls.filter(([u]) =>
            String(u).endsWith("/api/reports/home"),
        ).length;
        btn.click();
        await waitFor(() => {
            expect(
                fetchMock.mock.calls.some(
                    ([u, init]) =>
                        String(u).endsWith("/api/admin/reports/42/unpin") &&
                        (init as RequestInit | undefined)?.method === "POST",
                ),
            ).toBe(true);
        });
        // The pinned-reports query should have been invalidated & refetched.
        await waitFor(() => {
            const after = fetchMock.mock.calls.filter(([u]) =>
                String(u).endsWith("/api/reports/home"),
            ).length;
            expect(after).toBeGreaterThan(homeCallCount);
        });
    });
});
