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
import { AdminUsersRoute } from "./admin-users";
import { useSessionStore } from "../state/session";

/**
 * Component-level tests for the admin users route: role gating,
 * table rendering, create-user modal happy path, and 409 conflict
 * surfacing. Network is stubbed at the global fetch level so no real
 * HTTP is issued.
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
                    : status === 401
                        ? "Unauthorized"
                        : status === 403
                            ? "Forbidden"
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

function renderAdminUsers() {
    const rootRoute = createRootRoute({ component: Outlet });
    const route = createRoute({
        getParentRoute: () => rootRoute,
        path: "/admin/users",
        component: AdminUsersRoute,
    });
    const routeTree = rootRoute.addChildren([route]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: ["/admin/users"] }),
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

describe("AdminUsersRoute", () => {
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
        renderAdminUsers();
        const alert = await screen.findByRole("alert");
        expect(alert).toHaveTextContent(/must be an administrator/i);
    });

    it("lists users returned by GET /api/admin/users", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/users") && (i?.method ?? "GET") === "GET",
                status: 200,
                body: [
                    {
                        id: 1,
                        email: "root@nieweb.local",
                        displayName: "Root",
                        isDisabled: false,
                        isOidcProvisioned: false,
                        roles: ["Admin"],
                        createdUtc: "2026-01-01T00:00:00Z",
                        lastLoginUtc: "2026-07-20T14:00:00Z",
                    },
                    {
                        id: 2,
                        email: "reader@nieweb.local",
                        displayName: "Read Only",
                        isDisabled: true,
                        isOidcProvisioned: false,
                        roles: ["Reader"],
                        createdUtc: "2026-02-15T00:00:00Z",
                        lastLoginUtc: null,
                    },
                ],
            },
        ]);
        renderAdminUsers();

        expect(
            await screen.findByRole("cell", { name: "root@nieweb.local" }),
        ).toBeInTheDocument();
        expect(
            screen.getByRole("cell", { name: "reader@nieweb.local" }),
        ).toBeInTheDocument();
        // Disabled badge
        expect(screen.getByText("Disabled")).toBeInTheDocument();
        // "Never" for the row without a last-login
        expect(screen.getByText("Never")).toBeInTheDocument();
    });

    it("surfaces a load error banner when the list request fails", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u) => u.endsWith("/api/admin/users"),
                status: 500,
                body: { error: "boom" },
            },
        ]);
        renderAdminUsers();
        const alert = await screen.findByRole("alert");
        expect(alert).toHaveTextContent(/could not load users/i);
    });

    it("posts to the create endpoint and reloads the list on success", async () => {
        signInAs(["Admin"]);
        let listCallCount = 0;
        const fetchMock = stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/users") && (i?.method ?? "GET") === "GET",
                status: 200,
                body: [],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/users") && i?.method === "POST",
                status: 201,
                body: {
                    id: 42,
                    email: "new@nieweb.local",
                    displayName: "New User",
                    isDisabled: false,
                    isOidcProvisioned: false,
                    roles: ["Reader"],
                    createdUtc: "2026-07-21T00:00:00Z",
                    lastLoginUtc: null,
                },
            },
        ]);
        // Intercept counter for the GET requests so we can verify refetch.
        const origMock = fetchMock.getMockImplementation()!;
        fetchMock.mockImplementation(async (input, init) => {
            if (
                (typeof input === "string" ? input : (input as Request).url).endsWith(
                    "/api/admin/users",
                ) &&
                (!init || init.method === "GET" || init.method === undefined)
            ) {
                listCallCount++;
            }
            return origMock(input, init);
        });

        renderAdminUsers();

        // Open the create modal.
        const user = userEvent.setup();
        await user.click(await screen.findByRole("button", { name: /add user/i }));

        // Fill the form.
        const dialog = await screen.findByRole("dialog");
        await user.type(
            within(dialog).getByPlaceholderText("user@example.com"),
            "new@nieweb.local",
        );
        await user.type(
            within(dialog).getByPlaceholderText(/full name shown in the UI/i),
            "New User",
        );
        await user.type(
            within(dialog).getByPlaceholderText(/ask the user to rotate/i),
            "TempPass123!",
        );
        await user.click(within(dialog).getByRole("button", { name: /create user/i }));

        // Wait for the list to be refetched (count goes from 1 -> 2).
        await waitFor(() => {
            expect(listCallCount).toBeGreaterThanOrEqual(2);
        });
    });

    it("shows a conflict alert when the create endpoint returns 409", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/users") && (i?.method ?? "GET") === "GET",
                status: 200,
                body: [],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/users") && i?.method === "POST",
                status: 409,
                body: "dup",
            },
        ]);
        renderAdminUsers();

        const user = userEvent.setup();
        await user.click(await screen.findByRole("button", { name: /add user/i }));
        const dialog = await screen.findByRole("dialog");
        await user.type(
            within(dialog).getByPlaceholderText("user@example.com"),
            "dup@nieweb.local",
        );
        await user.type(
            within(dialog).getByPlaceholderText(/full name shown in the UI/i),
            "Dup",
        );
        await user.type(
            within(dialog).getByPlaceholderText(/ask the user to rotate/i),
            "TempPass123!",
        );
        await user.click(within(dialog).getByRole("button", { name: /create user/i }));

        const alerts = await within(dialog).findAllByRole("alert");
        expect(alerts[0]).toHaveTextContent(/already exists/i);
    });
});
