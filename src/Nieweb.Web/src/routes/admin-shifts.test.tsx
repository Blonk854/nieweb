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
import { AdminShiftsRoute } from "./admin-shifts";
import { useSessionStore } from "../state/session";

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
        return new Response(hit.status === 204 ? null : bodyText, {
            status: hit.status,
            statusText: "OK",
            headers: { "Content-Type": "application/json" },
        });
    });
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock as Mock;
}

function renderRoute() {
    const rootRoute = createRootRoute({ component: Outlet });
    const route = createRoute({
        getParentRoute: () => rootRoute,
        path: "/admin/shifts",
        component: AdminShiftsRoute,
    });
    const routeTree = rootRoute.addChildren([route]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: ["/admin/shifts"] }),
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

describe("AdminShiftsRoute", () => {
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

    it("shows a forbidden alert for non-Admin callers", async () => {
        signInAs(["Author"]);
        renderRoute();
        const alert = await screen.findByRole("alert");
        expect(alert).toHaveTextContent(/must be an administrator/i);
    });

    it("hydrates drafts from GET /api/admin/shifts", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/shifts") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [
                    {
                        id: 1,
                        hour: 6,
                        minute: 0,
                        label: "Morning",
                        displayOrder: 0,
                        createdUtc: "2026-01-01T00:00:00Z",
                        lastModifiedUtc: "2026-01-01T00:00:00Z",
                    },
                    {
                        id: 2,
                        hour: 14,
                        minute: 30,
                        label: null,
                        displayOrder: 1,
                        createdUtc: "2026-01-01T00:00:00Z",
                        lastModifiedUtc: "2026-01-01T00:00:00Z",
                    },
                ],
            },
        ]);
        renderRoute();
        expect(await screen.findByTestId("admin-shifts-row-0")).toBeInTheDocument();
        expect(screen.getByTestId("admin-shifts-row-1")).toBeInTheDocument();
    });

    it("PUTs the whole cycle when Save is clicked", async () => {
        signInAs(["Admin"]);
        const fetchMock = stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/shifts") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [
                    {
                        id: 1,
                        hour: 6,
                        minute: 0,
                        label: "Morning",
                        displayOrder: 0,
                        createdUtc: "2026-01-01T00:00:00Z",
                        lastModifiedUtc: "2026-01-01T00:00:00Z",
                    },
                ],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/shifts") && i?.method === "PUT",
                status: 200,
                body: [
                    {
                        id: 1,
                        hour: 6,
                        minute: 0,
                        label: "Morning",
                        displayOrder: 0,
                        createdUtc: "2026-01-01T00:00:00Z",
                        lastModifiedUtc: "2026-01-01T00:00:00Z",
                    },
                ],
            },
        ]);
        const user = userEvent.setup();
        renderRoute();
        await screen.findByTestId("admin-shifts-row-0");
        await user.click(screen.getByTestId("admin-shifts-save"));
        await waitFor(() => {
            const putCall = fetchMock.mock.calls.find(
                (c) => (c[1] as RequestInit | undefined)?.method === "PUT",
            );
            expect(putCall).toBeDefined();
            const body = JSON.parse(String(putCall![1]!.body)) as {
                entries: { hour: number; minute: number; label: string | null }[];
            };
            expect(body.entries).toHaveLength(1);
            expect(body.entries[0]).toMatchObject({
                hour: 6,
                minute: 0,
                label: "Morning",
            });
        });
        expect(
            await screen.findByText(/shift cycle saved/i),
        ).toBeInTheDocument();
    });

    it("adds a new breakpoint row when Add is clicked", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/shifts") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [],
            },
        ]);
        const user = userEvent.setup();
        renderRoute();
        // Wait for empty state to render.
        await screen.findByText(/no shift breakpoints/i);
        await user.click(screen.getByTestId("admin-shifts-add"));
        expect(
            await screen.findByTestId("admin-shifts-row-0"),
        ).toBeInTheDocument();
    });
});
