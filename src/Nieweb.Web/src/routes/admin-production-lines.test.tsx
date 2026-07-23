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
import { AdminProductionLinesRoute } from "./admin-production-lines";
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
        const statusText = hit.status === 409 ? "Conflict" : "OK";
        return new Response(hit.status === 204 ? null : bodyText, {
            status: hit.status,
            statusText,
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
        path: "/admin/production-lines",
        component: AdminProductionLinesRoute,
    });
    const routeTree = rootRoute.addChildren([route]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({
            initialEntries: ["/admin/production-lines"],
        }),
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

const LINE_1 = {
    id: 1,
    name: "Line 1",
    displayOrder: 0,
    machineCount: 2,
    createdUtc: "2026-01-01T00:00:00Z",
    lastModifiedUtc: "2026-01-01T00:00:00Z",
};

describe("AdminProductionLinesRoute", () => {
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
        signInAs(["Reader"]);
        renderRoute();
        const alert = await screen.findByRole("alert");
        expect(alert).toHaveTextContent(/must be an administrator/i);
    });

    it("lists production lines returned by GET", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/production-lines") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [LINE_1],
            },
        ]);
        renderRoute();
        expect(
            await screen.findByTestId("admin-production-lines-row-1"),
        ).toBeInTheDocument();
        expect(screen.getByText("Line 1")).toBeInTheDocument();
    });

    it("POSTs a new line when the create modal is submitted", async () => {
        signInAs(["Admin"]);
        const fetchMock = stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/production-lines") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/production-lines") &&
                    i?.method === "POST",
                status: 201,
                body: LINE_1,
            },
        ]);
        const user = userEvent.setup();
        renderRoute();
        await screen.findByText(/no production lines/i);
        await user.click(screen.getByRole("button", { name: /add line/i }));
        const nameInput = await screen.findByTestId(
            "admin-production-lines-name",
        );
        await user.type(nameInput, "Line 1");
        await user.click(screen.getByTestId("admin-production-lines-submit"));
        await waitFor(() => {
            const postCall = fetchMock.mock.calls.find(
                (c) => (c[1] as RequestInit | undefined)?.method === "POST",
            );
            expect(postCall).toBeDefined();
            expect(String(postCall![1]!.body)).toContain('"name":"Line 1"');
        });
    });

    it("shows a conflict alert when POST returns 409", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/production-lines") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/production-lines") &&
                    i?.method === "POST",
                status: 409,
                body: "duplicate name",
            },
        ]);
        const user = userEvent.setup();
        renderRoute();
        await screen.findByText(/no production lines/i);
        await user.click(screen.getByRole("button", { name: /add line/i }));
        const nameInput = await screen.findByTestId(
            "admin-production-lines-name",
        );
        await user.type(nameInput, "Line 1");
        await user.click(screen.getByTestId("admin-production-lines-submit"));
        const alerts = await screen.findAllByRole("alert");
        // Find the one inside the modal.
        const conflictAlert = alerts.find((a) =>
            /already has that name/i.test(a.textContent ?? ""),
        );
        expect(conflictAlert).toBeDefined();
    });

    it("expands a row and fetches its machine detail", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/production-lines") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [LINE_1],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/production-lines/1") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: {
                    line: LINE_1,
                    machines: [
                        {
                            id: 10,
                            productionLineId: 1,
                            sourceId: "postreflow",
                            machineId: 42,
                            machineName: "AOI-A",
                            category: "AOI",
                            displayOrder: 0,
                            createdUtc: "2026-01-01T00:00:00Z",
                        },
                    ],
                },
            },
        ]);
        const user = userEvent.setup();
        renderRoute();
        await user.click(
            await screen.findByTestId("admin-production-lines-expand-1"),
        );
        expect(
            await screen.findByTestId("admin-production-lines-machine-10"),
        ).toBeInTheDocument();
        expect(screen.getByText("AOI-A")).toBeInTheDocument();
    });
});
