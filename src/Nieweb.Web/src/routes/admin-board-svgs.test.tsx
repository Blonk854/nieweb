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
import { AdminBoardSvgsRoute } from "./admin-board-svgs";
import { useSessionStore } from "../state/session";

/**
 * Component-level tests for the admin board-SVG route
 * (docs/phase-2.md §7.5 TC4 Phase D). Covers:
 * <ul>
 *   <li>Role gating (non-admin sees a forbidden alert).</li>
 *   <li>Status card + sources table rendering from stubbed API.</li>
 *   <li>Create-source modal happy path (POST + list refetch).</li>
 *   <li>Sync-now button opens the result modal with per-product outcome.</li>
 *   <li>Delete-source confirm modal DELETEs and refetches.</li>
 * </ul>
 * Network is stubbed at the global fetch level so no real HTTP fires.
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

function renderRoute() {
    const rootRoute = createRootRoute({ component: Outlet });
    const route = createRoute({
        getParentRoute: () => rootRoute,
        path: "/admin/board-svgs",
        component: AdminBoardSvgsRoute,
    });
    const routeTree = rootRoute.addChildren([route]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: ["/admin/board-svgs"] }),
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

function sampleSource(overrides: Partial<{
    id: number;
    machineName: string;
    uncPath: string;
    isEnabled: boolean;
    lastSyncedUtc: string | null;
    lastSyncError: string | null;
}> = {}) {
    return {
        id: 1,
        machineName: "Post-reflow AOI 1",
        uncPath: "\\\\aoi1\\svgs",
        isEnabled: true,
        lastSyncedUtc: "2026-07-22T09:00:00Z",
        lastSyncErrorUtc: null,
        lastSyncError: null,
        createdUtc: "2026-07-01T00:00:00Z",
        lastModifiedUtc: "2026-07-01T00:00:00Z",
        ...overrides,
    };
}

function sampleStatus(overrides: Partial<{
    cacheDirectory: string;
    cacheDirectoryExists: boolean;
    intervalSeconds: number;
    syncEnabled: boolean;
    cache: Array<{
        productName: string;
        fileName: string;
        sizeBytes: number;
        lastWriteTimeUtc: string;
    }>;
    knownProducts: string[];
    missingProducts: string[];
    sources: unknown[];
}> = {}) {
    return {
        cacheDirectory: "D:\\Nieweb\\cache\\board-svgs",
        cacheDirectoryExists: true,
        intervalSeconds: 300,
        syncEnabled: true,
        sources: [],
        cache: [],
        knownProducts: [],
        missingProducts: [],
        ...overrides,
    };
}

describe("AdminBoardSvgsRoute", () => {
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

    it("renders status card and sources table from the API", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/board-svgs/sources") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [
                    sampleSource({
                        id: 7,
                        machineName: "Pre-reflow AOI 2",
                        uncPath: "\\\\aoi2\\svgs",
                        isEnabled: false,
                    }),
                ],
            },
            {
                match: (u) => u.endsWith("/api/admin/board-svgs/status"),
                status: 200,
                body: sampleStatus({
                    cache: [
                        {
                            productName: "ProductA",
                            fileName: "ProductA.svg",
                            sizeBytes: 1234,
                            lastWriteTimeUtc: "2026-07-22T08:00:00Z",
                        },
                    ],
                    knownProducts: ["ProductA", "ProductB"],
                    missingProducts: ["ProductB"],
                }),
            },
        ]);
        renderRoute();

        // Sources table row
        expect(
            await screen.findByText("Pre-reflow AOI 2"),
        ).toBeInTheDocument();
        expect(screen.getByText("\\\\aoi2\\svgs")).toBeInTheDocument();

        // Status card: cache dir, cached file, missing product
        expect(screen.getByText("D:\\Nieweb\\cache\\board-svgs")).toBeInTheDocument();
        expect(screen.getByText("ProductA.svg")).toBeInTheDocument();
        expect(screen.getByText("ProductB")).toBeInTheDocument();
    });

    it("surfaces a load-error banner when either query fails", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u) => u.endsWith("/api/admin/board-svgs/sources"),
                status: 500,
                body: { error: "boom" },
            },
            {
                match: (u) => u.endsWith("/api/admin/board-svgs/status"),
                status: 500,
                body: { error: "boom" },
            },
        ]);
        renderRoute();
        const alerts = await screen.findAllByRole("alert");
        expect(alerts.some((a) => /could not load/i.test(a.textContent ?? ""))).toBe(true);
    });

    it("creates a new source via the Add source modal and refetches", async () => {
        signInAs(["Admin"]);
        let getListCount = 0;
        const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
            const url =
                typeof input === "string"
                    ? input
                    : input instanceof URL
                        ? input.toString()
                        : input.url;
            const method = init?.method ?? "GET";
            if (
                url.endsWith("/api/admin/board-svgs/sources") &&
                method === "GET"
            ) {
                getListCount++;
                return new Response(JSON.stringify([]), {
                    status: 200,
                    statusText: "OK",
                    headers: { "Content-Type": "application/json" },
                });
            }
            if (url.endsWith("/api/admin/board-svgs/status")) {
                return new Response(JSON.stringify(sampleStatus()), {
                    status: 200,
                    statusText: "OK",
                    headers: { "Content-Type": "application/json" },
                });
            }
            if (
                url.endsWith("/api/admin/board-svgs/sources") &&
                method === "POST"
            ) {
                return new Response(
                    JSON.stringify(
                        sampleSource({
                            id: 99,
                            machineName: "New Machine",
                            uncPath: "\\\\new\\svgs",
                        }),
                    ),
                    {
                        status: 201,
                        statusText: "Created",
                        headers: { "Content-Type": "application/json" },
                    },
                );
            }
            throw new Error(`Unexpected fetch: ${method} ${url}`);
        });
        vi.stubGlobal("fetch", fetchMock);

        renderRoute();

        // Wait for the initial GET.
        await waitFor(() => {
            expect(getListCount).toBeGreaterThanOrEqual(1);
        });

        const user = userEvent.setup();
        await user.click(
            await screen.findByRole("button", { name: /add source/i }),
        );

        const dialog = await screen.findByRole("dialog");
        await user.type(
            within(dialog).getByPlaceholderText(/post-reflow aoi 1/i),
            "New Machine",
        );
        await user.type(
            within(dialog).getByPlaceholderText(/host.*share.*svgs/i),
            "\\\\new\\svgs",
        );
        await user.click(
            within(dialog).getByRole("button", { name: /create source/i }),
        );

        await waitFor(() => {
            expect(getListCount).toBeGreaterThanOrEqual(2);
        });
    });

    it("shows a conflict alert when the create endpoint returns 409", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/board-svgs/sources") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [],
            },
            {
                match: (u) => u.endsWith("/api/admin/board-svgs/status"),
                status: 200,
                body: sampleStatus(),
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/board-svgs/sources") &&
                    i?.method === "POST",
                status: 409,
                body: "Machine name already exists",
            },
        ]);
        renderRoute();
        const user = userEvent.setup();
        await user.click(
            await screen.findByRole("button", { name: /add source/i }),
        );

        const dialog = await screen.findByRole("dialog");
        await user.type(
            within(dialog).getByPlaceholderText(/post-reflow aoi 1/i),
            "Duplicate",
        );
        await user.type(
            within(dialog).getByPlaceholderText(/host.*share.*svgs/i),
            "\\\\dup\\svgs",
        );
        await user.click(
            within(dialog).getByRole("button", { name: /create source/i }),
        );

        const alert = await within(dialog).findByRole("alert");
        expect(alert).toHaveTextContent(/already exists/i);
    });

    it("triggers a sync and renders the per-source / per-product result", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/board-svgs/sources") &&
                    (i?.method ?? "GET") === "GET",
                status: 200,
                body: [sampleSource()],
            },
            {
                match: (u) => u.endsWith("/api/admin/board-svgs/status"),
                status: 200,
                body: sampleStatus(),
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/board-svgs/sync") &&
                    i?.method === "POST",
                status: 200,
                body: {
                    startedUtc: "2026-07-22T10:00:00Z",
                    completedUtc: "2026-07-22T10:00:05Z",
                    cacheDirectory: "D:\\cache",
                    sources: [
                        {
                            sourceId: 1,
                            machineName: "Post-reflow AOI 1",
                            uncPath: "\\\\aoi1\\svgs",
                            enabled: true,
                            reachable: true,
                            filesEnumerated: 3,
                            error: null,
                        },
                    ],
                    products: [
                        {
                            productName: "ProductA",
                            alreadyCached: false,
                            copied: true,
                            sourceMachineName: "Post-reflow AOI 1",
                            sourceFileLastWriteUtc: "2026-07-22T09:59:00Z",
                            bytesCopied: 2048,
                            error: null,
                        },
                    ],
                },
            },
        ]);
        renderRoute();

        const user = userEvent.setup();
        await user.click(
            await screen.findByRole("button", { name: /sync now/i }),
        );

        // Result modal renders.
        const dialog = await screen.findByRole("dialog", {
            name: /sync result/i,
        });
        expect(within(dialog).getByText("ProductA")).toBeInTheDocument();
        expect(within(dialog).getByText(/copied/i)).toBeInTheDocument();
        // "Reachable" appears both as column header and as the badge, so
        // just assert there's at least one match.
        expect(
            within(dialog).getAllByText(/reachable/i).length,
        ).toBeGreaterThan(0);
        expect(within(dialog).getByText(/3 files/i)).toBeInTheDocument();
    });

    it("deletes a source through the confirm modal and refetches", async () => {
        signInAs(["Admin"]);
        let getListCount = 0;
        const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
            const url =
                typeof input === "string"
                    ? input
                    : input instanceof URL
                        ? input.toString()
                        : input.url;
            const method = init?.method ?? "GET";
            if (
                url.endsWith("/api/admin/board-svgs/sources") &&
                method === "GET"
            ) {
                getListCount++;
                return new Response(
                    JSON.stringify(
                        getListCount === 1
                            ? [sampleSource({ id: 12, machineName: "Doomed" })]
                            : [],
                    ),
                    {
                        status: 200,
                        statusText: "OK",
                        headers: { "Content-Type": "application/json" },
                    },
                );
            }
            if (url.endsWith("/api/admin/board-svgs/status")) {
                return new Response(JSON.stringify(sampleStatus()), {
                    status: 200,
                    statusText: "OK",
                    headers: { "Content-Type": "application/json" },
                });
            }
            if (
                url.includes("/api/admin/board-svgs/sources/12") &&
                method === "DELETE"
            ) {
                return new Response(null, {
                    status: 204,
                    statusText: "No Content",
                });
            }
            throw new Error(`Unexpected fetch: ${method} ${url}`);
        });
        vi.stubGlobal("fetch", fetchMock);

        renderRoute();

        const user = userEvent.setup();
        // Click the row's Delete button.
        await user.click(
            await screen.findByRole("button", { name: /^delete$/i }),
        );
        const dialog = await screen.findByRole("dialog", {
            name: /delete source/i,
        });
        // The confirm-body button is the red "Delete" button.
        const confirm = within(dialog).getAllByRole("button", {
            name: /^delete$/i,
        });
        await user.click(confirm[confirm.length - 1]);

        await waitFor(() => {
            expect(getListCount).toBeGreaterThanOrEqual(2);
        });
    });
});
