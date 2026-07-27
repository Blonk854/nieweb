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
import { AdminSkipClassificationRoute } from "./admin-skip-classification";
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

const DEFAULT_CONFIG = {
    missingRatioThreshold: 0.5,
    minComponentFloor: 8,
    absoluteMissingFloor: 4,
    repairButtonMeanings: [{ label: "X-OUT", meaning: "ManualSkip" }],
};

function renderRoute() {
    const rootRoute = createRootRoute({ component: Outlet });
    const route = createRoute({
        getParentRoute: () => rootRoute,
        path: "/admin/skip-classification",
        component: AdminSkipClassificationRoute,
    });
    const routeTree = rootRoute.addChildren([route]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: ["/admin/skip-classification"] }),
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

describe("AdminSkipClassificationRoute", () => {
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

    it("hydrates the form from GET and shows the button map", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/skip-classification") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: DEFAULT_CONFIG,
            },
        ]);
        renderRoute();

        const ratio = await screen.findByTestId("admin-skip-ratio");
        expect(ratio).toHaveValue("0.5");
        expect(screen.getByDisplayValue("X-OUT")).toBeInTheDocument();
    });

    it("PUTs the config on save", async () => {
        signInAs(["Admin"]);
        const fetchMock = stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/skip-classification") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: DEFAULT_CONFIG,
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/skip-classification") && i?.method === "PUT",
                status: 200,
                body: DEFAULT_CONFIG,
            },
        ]);
        renderRoute();

        const save = await screen.findByTestId("admin-skip-save");
        await userEvent.click(save);

        await waitFor(() => {
            const put = fetchMock.mock.calls.find(
                ([, init]) => (init as RequestInit | undefined)?.method === "PUT",
            );
            expect(put).toBeDefined();
            const body = JSON.parse((put![1] as RequestInit).body as string);
            expect(body.missingRatioThreshold).toBe(0.5);
            expect(body.minComponentFloor).toBe(8);
            expect(body.repairButtonMeanings).toEqual([
                { label: "X-OUT", meaning: "ManualSkip" },
            ]);
        });
    });
});
