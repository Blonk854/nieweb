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
import { AdminParametersRoute } from "./admin-parameters";
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
        const statusText =
            hit.status === 200 || hit.status === 201
                ? "OK"
                : hit.status === 204
                    ? "No Content"
                    : hit.status === 409
                        ? "Conflict"
                        : "Error";
        const bodyText =
            hit.body === undefined
                ? ""
                : typeof hit.body === "string"
                    ? hit.body
                    : JSON.stringify(hit.body);
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
        path: "/admin/parameters",
        component: AdminParametersRoute,
    });
    const routeTree = rootRoute.addChildren([route]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: ["/admin/parameters"] }),
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

const SYSTEM_ROW = {
    key: "tolerance.component.itx",
    valueType: "decimal",
    value: "0.02",
    description: "Component tolerance interval X",
    isSystem: true,
    createdUtc: "2026-01-01T00:00:00Z",
    lastModifiedUtc: "2026-06-01T00:00:00Z",
};

const CUSTOM_ROW = {
    key: "custom.knob",
    valueType: "string",
    value: "hello",
    description: null,
    isSystem: false,
    createdUtc: "2026-01-01T00:00:00Z",
    lastModifiedUtc: "2026-06-01T00:00:00Z",
};

describe("AdminParametersRoute", () => {
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
        renderRoute();
        const alert = await screen.findByRole("alert");
        expect(alert).toHaveTextContent(/must be an administrator/i);
    });

    it("lists parameters returned by GET /api/admin/parameters", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/parameters") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [SYSTEM_ROW, CUSTOM_ROW],
            },
        ]);
        renderRoute();
        expect(
            await screen.findByTestId(
                "admin-parameters-row-tolerance.component.itx",
            ),
        ).toBeInTheDocument();
        expect(
            await screen.findByTestId("admin-parameters-row-custom.knob"),
        ).toBeInTheDocument();
        // System row should not expose a Delete button.
        expect(
            screen.queryByTestId(
                "admin-parameters-delete-tolerance.component.itx",
            ),
        ).not.toBeInTheDocument();
        // Custom row should expose a Delete button.
        expect(
            screen.getByTestId("admin-parameters-delete-custom.knob"),
        ).toBeInTheDocument();
    });

    it("submits an edit to /api/admin/parameters/{key}", async () => {
        signInAs(["Admin"]);
        const fetchMock = stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/parameters") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [SYSTEM_ROW],
            },
            {
                match: (u, i) =>
                    u.includes("/api/admin/parameters/tolerance.component.itx") &&
                    i?.method === "PUT",
                status: 200,
                body: { ...SYSTEM_ROW, value: "0.05" },
            },
        ]);
        const user = userEvent.setup();
        renderRoute();
        await user.click(
            await screen.findByTestId(
                "admin-parameters-edit-tolerance.component.itx",
            ),
        );
        const valueInput = await screen.findByTestId("admin-parameters-value");
        await user.clear(valueInput);
        await user.type(valueInput, "0.05");
        await user.click(screen.getByTestId("admin-parameters-submit"));
        await waitFor(() => {
            const putCall = fetchMock.mock.calls.find(
                (c) => (c[1] as RequestInit | undefined)?.method === "PUT",
            );
            expect(putCall).toBeDefined();
            expect(String(putCall![1]!.body)).toContain('"value":"0.05"');
        });
    });

    it("shows the systemProtected alert when DELETE returns 409", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/parameters") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [CUSTOM_ROW],
            },
            {
                match: (u, i) =>
                    u.includes("/api/admin/parameters/custom.knob") &&
                    i?.method === "DELETE",
                status: 409,
                body: "system parameter",
            },
        ]);
        const user = userEvent.setup();
        renderRoute();
        await user.click(
            await screen.findByTestId("admin-parameters-delete-custom.knob"),
        );
        await user.click(
            await screen.findByTestId("admin-parameters-delete-submit"),
        );
        const alert = await screen.findByRole("alert");
        expect(alert).toHaveTextContent(/system parameters cannot be deleted/i);
    });
});
