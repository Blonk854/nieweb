import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Mock } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
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
import { AdminAuditRoute } from "./admin-audit";
import { useSessionStore } from "../state/session";

/**
 * Component-level tests for the admin audit route: role gating, table
 * rendering, filter application (URL query-string wired correctly),
 * and pagination navigation. Network is stubbed at the global fetch
 * level so no real HTTP is issued.
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
            status === 200
                ? "OK"
                : status === 401
                    ? "Unauthorized"
                    : status === 403
                        ? "Forbidden"
                        : "Error";
        const bodyText =
            hit.body === undefined
                ? ""
                : typeof hit.body === "string"
                    ? hit.body
                    : JSON.stringify(hit.body);
        return new Response(bodyText, {
            status,
            statusText,
            headers: { "Content-Type": "application/json" },
        });
    });
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock as Mock;
}

function renderAdminAudit() {
    const rootRoute = createRootRoute({ component: Outlet });
    const route = createRoute({
        getParentRoute: () => rootRoute,
        path: "/admin/audit",
        component: AdminAuditRoute,
    });
    const routeTree = rootRoute.addChildren([route]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: ["/admin/audit"] }),
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

function sampleEvent(overrides: Partial<{
    id: number;
    eventTimeUtc: string;
    actorUserId: number | null;
    actorDisplayName: string;
    eventType: string;
    targetType: string;
    targetId: string;
    detailsJson: string;
    ipAddress: string | null;
}> = {}) {
    return {
        id: 1,
        eventTimeUtc: "2026-07-20T10:00:00Z",
        actorUserId: 1,
        actorDisplayName: "Root Admin",
        eventType: "auth.signin.ok",
        targetType: "Session",
        targetId: "1",
        detailsJson: JSON.stringify({ email: "root@nieweb.local" }),
        ipAddress: "127.0.0.1",
        ...overrides,
    };
}

describe("AdminAuditRoute", () => {
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
        renderAdminAudit();
        const alert = await screen.findByRole("alert");
        expect(alert).toHaveTextContent(/must be an administrator/i);
    });

    it("lists audit events returned by GET /api/admin/audit", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u) => u.includes("/api/admin/audit"),
                status: 200,
                body: {
                    items: [
                        sampleEvent({
                            id: 10,
                            eventType: "user.created",
                            targetType: "User",
                            targetId: "42",
                            actorUserId: 1,
                            actorDisplayName: "Root Admin",
                            ipAddress: "10.0.0.1",
                        }),
                        sampleEvent({
                            id: 9,
                            eventType: "auth.signin.failed",
                            targetType: "User",
                            targetId: "unknown",
                            actorUserId: null,
                            actorDisplayName: "ghost@nieweb.local",
                            ipAddress: null,
                        }),
                    ],
                    total: 2,
                    page: 1,
                    pageSize: 50,
                },
            },
        ]);
        renderAdminAudit();

        expect(await screen.findByText("user.created")).toBeInTheDocument();
        expect(screen.getByText("auth.signin.failed")).toBeInTheDocument();
        // Anonymous label rendered when actorUserId is null
        expect(screen.getByText("(anonymous)")).toBeInTheDocument();
        // Em-dash placeholder when ipAddress is null
        expect(screen.getByText("—")).toBeInTheDocument();
    });

    it("surfaces a load error banner when the request fails", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u) => u.includes("/api/admin/audit"),
                status: 500,
                body: { error: "boom" },
            },
        ]);
        renderAdminAudit();
        const alert = await screen.findByRole("alert");
        expect(alert).toHaveTextContent(/could not load audit events/i);
    });

    it("applies filters by sending them as query-string parameters", async () => {
        signInAs(["Admin"]);
        const seenUrls: string[] = [];
        const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
            const url =
                typeof input === "string"
                    ? input
                    : input instanceof URL
                        ? input.toString()
                        : input.url;
            seenUrls.push(url);
            return new Response(
                JSON.stringify({
                    items: [],
                    total: 0,
                    page: 1,
                    pageSize: 50,
                }),
                {
                    status: 200,
                    statusText: "OK",
                    headers: { "Content-Type": "application/json" },
                },
            );
        });
        vi.stubGlobal("fetch", fetchMock);

        renderAdminAudit();

        // Wait for the initial (unfiltered) request to fire.
        await waitFor(() => {
            expect(seenUrls.length).toBeGreaterThanOrEqual(1);
        });

        const user = userEvent.setup();
        await user.type(
            screen.getByLabelText(/event type/i),
            "auth.signin.failed",
        );
        await user.click(screen.getByRole("button", { name: /^apply$/i }));

        await waitFor(() => {
            const filtered = seenUrls.find((u) =>
                u.includes("eventType=auth.signin.failed"),
            );
            expect(filtered).toBeDefined();
        });
    });

    it("changes page when the pagination control is clicked", async () => {
        signInAs(["Admin"]);
        const seenPages: string[] = [];
        // Return 3 pages worth of results so the paginator renders "1 2 3".
        const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
            const url =
                typeof input === "string"
                    ? input
                    : input instanceof URL
                        ? input.toString()
                        : input.url;
            const match = url.match(/[?&]page=(\d+)/);
            const page = match ? match[1] : "1";
            seenPages.push(page);
            return new Response(
                JSON.stringify({
                    items: [
                        sampleEvent({
                            id: Number(page) * 100,
                            eventType: `page-${page}-event`,
                        }),
                    ],
                    total: 120, // 3 pages at pageSize=50
                    page: Number(page),
                    pageSize: 50,
                }),
                {
                    status: 200,
                    statusText: "OK",
                    headers: { "Content-Type": "application/json" },
                },
            );
        });
        vi.stubGlobal("fetch", fetchMock);

        renderAdminAudit();

        // Wait for the initial page to render.
        expect(await screen.findByText("page-1-event")).toBeInTheDocument();

        // Click the "2" page button in the Mantine Pagination.
        const user = userEvent.setup();
        const pageTwo = await screen.findByRole("button", { name: "2" });
        await user.click(pageTwo);

        await waitFor(() => {
            expect(seenPages.some((p) => p === "2")).toBe(true);
        });
        expect(await screen.findByText("page-2-event")).toBeInTheDocument();
    });
});
