import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Mock } from "vitest";
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import { userEvent } from "@testing-library/user-event";
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
import { AdminReportsRoute } from "./admin-reports";
import { useSessionStore } from "../state/session";

/**
 * Component-level tests for the RC2 admin reports list route.
 * Network is stubbed globally so no real HTTP is issued.
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
        const status = hit.status;
        const statusText =
            status === 200 || status === 201
                ? "OK"
                : status === 204
                    ? "No Content"
                    : status === 409
                        ? "Conflict"
                        : "Error";
        const bodyText =
            hit.body === undefined
                ? ""
                : typeof hit.body === "string"
                    ? hit.body
                    : JSON.stringify(hit.body);
        return new Response(status === 204 ? null : bodyText, {
            status,
            statusText,
            headers: { "Content-Type": "application/json" },
        });
    });
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock as Mock;
}

function renderAdminReports() {
    const rootRoute = createRootRoute({ component: Outlet });
    const route = createRoute({
        getParentRoute: () => rootRoute,
        path: "/admin/reports",
        component: AdminReportsRoute,
    });
    // Editor route is not exercised here but the Link inside the list
    // row targets it, so register a stub to keep TanStack Router happy.
    const editorRoute = createRoute({
        getParentRoute: () => rootRoute,
        path: "/admin/reports/$id",
        component: () => null,
    });
    const routeTree = rootRoute.addChildren([route, editorRoute]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: ["/admin/reports"] }),
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

function signInAs(roles: string[]) {
    useSessionStore.getState().setSession(
        {
            email: "root@nieweb.test",
            displayName: "Root Admin",
            roles,
            mustRotatePassword: false,
        },
        "test-token",
    );
}

describe("AdminReportsRoute", () => {
    beforeEach(() => {
        void i18n.changeLanguage("en");
        useSessionStore.getState().clear();
        window.localStorage.clear();
    });

    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it("shows a forbidden alert when the caller is not an Admin", async () => {
        signInAs(["Reader"]);
        renderAdminReports();
        const alert = await screen.findByRole("alert");
        expect(alert).toHaveTextContent(/must be an administrator/i);
    });

    it("lists groups and reports returned by the admin endpoints", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/report-groups") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [
                    { id: 1, name: "Daily production", displayOrder: 0, reportCount: 2 },
                ],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/reports") && (i?.method ?? "GET") === "GET",
                status: 200,
                body: [
                    {
                        id: 10,
                        title: "SMT overview",
                        description: null,
                        reportGroupId: 1,
                        groupName: "Daily production",
                        ownerDisplayName: "Root Admin",
                        isLocked: false,
                        isPinnedHome: false,
                        displayOrder: 0,
                        refreshFrequencySeconds: null,
                        entityCount: 3,
                        lastModifiedUtc: new Date().toISOString(),
                    },
                ],
            },
        ]);
        renderAdminReports();

        expect(
            (await screen.findAllByRole("cell", { name: "Daily production" })).length,
        ).toBeGreaterThanOrEqual(1);
        expect(
            await screen.findByText("SMT overview"),
        ).toBeInTheDocument();
    });

    it("surfaces a load error banner when the list requests fail", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u) => u.endsWith("/api/admin/report-groups"),
                status: 500,
                body: "boom",
            },
            {
                match: (u) => u.endsWith("/api/admin/reports"),
                status: 500,
                body: "boom",
            },
        ]);
        renderAdminReports();
        const alerts = await screen.findAllByRole("alert");
        expect(alerts.some((a) => /could not load reports/i.test(a.textContent ?? ""))).toBe(true);
    });

    it("shows a conflict alert when creating a group returns 409", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/report-groups") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [],
            },
            {
                match: (u) => u.endsWith("/api/admin/reports"),
                status: 200,
                body: [],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/report-groups") && i?.method === "POST",
                status: 409,
                body: "dup",
            },
        ]);
        renderAdminReports();

        const user = userEvent.setup();
        await user.click(await screen.findByRole("button", { name: /add group/i }));
        const dialog = await screen.findByRole("dialog");
        await user.type(
            within(dialog).getByPlaceholderText(/daily production/i),
            "Dup group",
        );
        await user.click(within(dialog).getByRole("button", { name: /^save$/i }));

        const alerts = await within(dialog).findAllByRole("alert");
        expect(alerts[0]).toHaveTextContent(/already exists/i);
    });

    it("posts to POST /api/admin/reports when creating a report", async () => {
        signInAs(["Admin"]);
        let postCount = 0;
        const fetchMock = stubFetch([
            {
                match: (u) => u.endsWith("/api/admin/report-groups"),
                status: 200,
                body: [],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/reports") && (i?.method ?? "GET") === "GET",
                status: 200,
                body: [],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/reports") && i?.method === "POST",
                status: 201,
                body: {
                    id: 99,
                    title: "New",
                    description: null,
                    reportGroupId: null,
                    groupName: null,
                    ownerDisplayName: "Root Admin",
                    isLocked: false,
                    isPinnedHome: false,
                    displayOrder: 0,
                    refreshFrequencySeconds: null,
                    entityCount: 0,
                    lastModifiedUtc: new Date().toISOString(),
                },
            },
        ]);
        const orig = fetchMock.getMockImplementation()!;
        fetchMock.mockImplementation(async (input, init) => {
            const url =
                typeof input === "string" ? input : (input as Request).url;
            if (url.endsWith("/api/admin/reports") && init?.method === "POST") {
                postCount++;
            }
            return orig(input, init);
        });

        renderAdminReports();
        const user = userEvent.setup();
        await user.click(await screen.findByRole("button", { name: /add report/i }));
        const dialog = await screen.findByRole("dialog");
        await user.type(
            within(dialog).getByPlaceholderText(/smt overview/i),
            "My report",
        );
        await user.click(within(dialog).getByRole("button", { name: /^save$/i }));

        await waitFor(() => {
            expect(postCount).toBeGreaterThanOrEqual(1);
        });
    });
});

/**
 * F14: pin/unpin toggle in the admin reports list.
 */
describe("AdminReportsRoute F14 pin toggle", () => {
    beforeEach(() => {
        void i18n.changeLanguage("en");
        useSessionStore.getState().clear();
        window.localStorage.clear();
    });

    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    function reportRow(id: number, title: string, isPinnedHome: boolean) {
        return {
            id,
            title,
            description: null,
            reportGroupId: null,
            groupName: null,
            ownerDisplayName: "Root Admin",
            isLocked: false,
            isPinnedHome,
            displayOrder: 0,
            refreshFrequencySeconds: null,
            entityCount: 0,
            lastModifiedUtc: new Date().toISOString(),
        };
    }

    function pinResponse(id: number, isPinnedHome: boolean) {
        return {
            id,
            title: "Pinned or not",
            description: null,
            reportGroupId: null,
            groupName: null,
            ownerUserId: null,
            ownerDisplayName: "Root Admin",
            isLocked: false,
            isPinnedHome,
            refreshFrequencySeconds: null,
            chromeJson: null,
            displayOrder: 0,
            entityCount: 0,
            createdUtc: new Date().toISOString(),
            lastModifiedUtc: new Date().toISOString(),
        };
    }

    it("posts to POST /api/admin/reports/{id}/pin when toggling an unpinned row", async () => {
        signInAs(["Admin"]);
        const fetchMock = stubFetch([
            { match: (u) => u.endsWith("/api/admin/report-groups"), status: 200, body: [] },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/reports") && (i?.method ?? "GET") === "GET",
                status: 200,
                body: [reportRow(11, "Not yet pinned", false)],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/reports/11/pin") && i?.method === "POST",
                status: 200,
                body: pinResponse(11, true),
            },
        ]);
        renderAdminReports();
        const toggle = await screen.findByTestId("report-pin-toggle-11");
        toggle.click();
        await waitFor(() => {
            expect(
                fetchMock.mock.calls.some(
                    ([u, init]) =>
                        String(u).endsWith("/api/admin/reports/11/pin") &&
                        (init as RequestInit | undefined)?.method === "POST",
                ),
            ).toBe(true);
        });
    });

    it("posts to POST /api/admin/reports/{id}/unpin when toggling a pinned row", async () => {
        signInAs(["Admin"]);
        const fetchMock = stubFetch([
            { match: (u) => u.endsWith("/api/admin/report-groups"), status: 200, body: [] },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/reports") && (i?.method ?? "GET") === "GET",
                status: 200,
                body: [reportRow(22, "Currently pinned", true)],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/reports/22/unpin") && i?.method === "POST",
                status: 200,
                body: pinResponse(22, false),
            },
        ]);
        renderAdminReports();
        const toggle = await screen.findByTestId("report-pin-toggle-22");
        toggle.click();
        await waitFor(() => {
            expect(
                fetchMock.mock.calls.some(
                    ([u, init]) =>
                        String(u).endsWith("/api/admin/reports/22/unpin") &&
                        (init as RequestInit | undefined)?.method === "POST",
                ),
            ).toBe(true);
        });
    });
});
