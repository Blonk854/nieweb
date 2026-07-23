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
import { AdminReportEditorRoute } from "./admin-report-editor";
import { useSessionStore } from "../state/session";

/**
 * Component-level tests for the RC2 report editor route.
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

function renderEditor(reportId = 7) {
    const rootRoute = createRootRoute({ component: Outlet });
    const listRoute = createRoute({
        getParentRoute: () => rootRoute,
        path: "/admin/reports",
        component: () => null,
    });
    const editorRoute = createRoute({
        getParentRoute: () => rootRoute,
        path: "/admin/reports/$id",
        component: AdminReportEditorRoute,
    });
    const routeTree = rootRoute.addChildren([listRoute, editorRoute]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({
            initialEntries: [`/admin/reports/${reportId}`],
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

function detailBody(entities: Array<{ id: number; tileType: string; displayOrder: number; configJson?: string; title?: string | null }> = []) {
    return {
        report: {
            id: 7,
            title: "Test report",
            description: "Desc",
            reportGroupId: null,
            groupName: null,
            ownerDisplayName: "Root",
            isLocked: false,
            isPinnedHome: false,
            displayOrder: 0,
            refreshFrequencySeconds: null,
            chromeJson: null,
            entityCount: entities.length,
            lastModifiedUtc: new Date().toISOString(),
        },
        entities: entities.map((e) => ({
            id: e.id,
            reportId: 7,
            tileType: e.tileType,
            title: e.title ?? null,
            displayOrder: e.displayOrder,
            configJson: e.configJson ?? "{}",
        })),
    };
}

describe("AdminReportEditorRoute", () => {
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
        renderEditor();
        const alert = await screen.findByRole("alert");
        expect(alert).toHaveTextContent(/must be an administrator/i);
    });

    it("loads a report and renders its header and tiles", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u) => u.endsWith("/api/admin/report-groups"),
                status: 200,
                body: [],
            },
            {
                match: (u) => u.endsWith("/api/admin/reports/7"),
                status: 200,
                body: detailBody([
                    { id: 100, tileType: "panelYield", displayOrder: 0 },
                ]),
            },
        ]);
        renderEditor();

        expect(
            await screen.findByRole("heading", { name: "Test report" }),
        ).toBeInTheDocument();
        expect(await screen.findByTestId("tile-row-100")).toBeInTheDocument();
    });

    it("shows an error banner when tile config JSON is invalid", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u) => u.endsWith("/api/admin/report-groups"),
                status: 200,
                body: [],
            },
            {
                match: (u) => u.endsWith("/api/admin/reports/7"),
                status: 200,
                body: detailBody([
                    { id: 200, tileType: "panelYield", displayOrder: 0, configJson: "{}" },
                ]),
            },
        ]);
        renderEditor();

        const row = await screen.findByTestId("tile-row-200");
        const user = userEvent.setup();
        const textarea = within(row).getByLabelText(/config json/i);
        await user.clear(textarea);
        await user.type(textarea, "not-json");
        await user.click(within(row).getByRole("button", { name: /save tile/i }));

        const alert = await within(row).findByRole("alert");
        expect(alert).toHaveTextContent(/must be valid json/i);
    });

    it("shows the empty tiles state when the report has no entities", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u) => u.endsWith("/api/admin/report-groups"),
                status: 200,
                body: [],
            },
            {
                match: (u) => u.endsWith("/api/admin/reports/7"),
                status: 200,
                body: detailBody([]),
            },
        ]);
        renderEditor();
        expect(await screen.findByText(/no tiles yet/i)).toBeInTheDocument();
    });

    it("POSTs a new tile when a palette entry is picked", async () => {
        signInAs(["Admin"]);
        let addCount = 0;
        const fetchMock = stubFetch([
            {
                match: (u) => u.endsWith("/api/admin/report-groups"),
                status: 200,
                body: [],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/reports/7") && (i?.method ?? "GET") === "GET",
                status: 200,
                body: detailBody([]),
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/reports/7/entities") && i?.method === "POST",
                status: 201,
                body: {
                    id: 300,
                    reportId: 7,
                    tileType: "panelYield",
                    title: null,
                    displayOrder: 0,
                    configJson: "{}",
                },
            },
        ]);
        const orig = fetchMock.getMockImplementation()!;
        fetchMock.mockImplementation(async (input, init) => {
            const url =
                typeof input === "string" ? input : (input as Request).url;
            if (
                url.endsWith("/api/admin/reports/7/entities") &&
                init?.method === "POST"
            ) {
                addCount++;
            }
            return orig(input, init);
        });

        renderEditor();

        const user = userEvent.setup();
        await user.click(await screen.findByRole("button", { name: /add tile/i }));
        const menuItems = await screen.findAllByRole("menuitem");
        // First non-label item — pick the panel-yield tile.
        const panelYieldItem = menuItems.find((el) =>
            /panel yield/i.test(el.textContent ?? ""),
        );
        expect(panelYieldItem).toBeDefined();
        await user.click(panelYieldItem!);

        await waitFor(() => {
            expect(addCount).toBeGreaterThanOrEqual(1);
        });
    });

    it("renders the export panel with source select + XLSX/PDF buttons", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u) => u.endsWith("/api/admin/report-groups"),
                status: 200,
                body: [],
            },
            {
                match: (u) => u.endsWith("/api/admin/reports/7"),
                status: 200,
                body: detailBody([
                    { id: 101, tileType: "panelYield", displayOrder: 0 },
                ]),
            },
            {
                match: (u) => u.endsWith("/api/sources"),
                status: 200,
                body: [
                    {
                        id: "postreflow",
                        displayName: "Post-reflow AOI",
                        schemaVersion: "5.0",
                        capabilities: ["PinLevel"],
                        latestPanelUtc: null,
                        available: true,
                    },
                ],
            },
        ]);
        renderEditor();

        expect(
            await screen.findByRole("heading", { name: /export report/i }),
        ).toBeInTheDocument();
        expect(
            await screen.findByRole("button", { name: /download xlsx/i }),
        ).toBeEnabled();
        expect(
            screen.getByRole("button", { name: /download pdf/i }),
        ).toBeEnabled();
    });

    it("issues an authenticated fetch when Download XLSX is clicked", async () => {
        signInAs(["Admin"]);
        const fetchMock = stubFetch([
            {
                match: (u) => u.endsWith("/api/admin/report-groups"),
                status: 200,
                body: [],
            },
            {
                match: (u) => u.endsWith("/api/admin/reports/7"),
                status: 200,
                body: detailBody([
                    { id: 101, tileType: "panelYield", displayOrder: 0 },
                ]),
            },
            {
                match: (u) => u.endsWith("/api/sources"),
                status: 200,
                body: [
                    {
                        id: "postreflow",
                        displayName: "Post-reflow AOI",
                        schemaVersion: "5.0",
                        capabilities: [],
                        latestPanelUtc: null,
                        available: true,
                    },
                ],
            },
            {
                match: (u) => /\/api\/reports\/7\/export\.xlsx\?/.test(u),
                status: 200,
                body: "PK-fake-xlsx-bytes",
            },
        ]);
        // Prevent jsdom's anchor.click() from bubbling into unimplemented
        // navigation logic — we only care that the fetch was made.
        const clickSpy = vi
            .spyOn(HTMLAnchorElement.prototype, "click")
            .mockImplementation(() => { /* noop */ });

        // JSDOM lacks URL.createObjectURL / revokeObjectURL.
        const createSpy = vi.fn(() => "blob:fake");
        const revokeSpy = vi.fn();
        vi.stubGlobal("URL", { ...URL, createObjectURL: createSpy, revokeObjectURL: revokeSpy });

        renderEditor();

        await screen.findByRole("heading", { name: /export report/i });
        const user = userEvent.setup();
        await user.click(screen.getByRole("button", { name: /download xlsx/i }));

        await waitFor(() => {
            const called = fetchMock.mock.calls.some((c) => {
                const url = typeof c[0] === "string" ? c[0] : (c[0] as URL).toString();
                if (!/\/api\/reports\/7\/export\.xlsx\?/.test(url)) return false;
                const headers = (c[1] as RequestInit | undefined)?.headers;
                const auth = headers instanceof Headers
                    ? headers.get("Authorization")
                    : (headers as Record<string, string> | undefined)?.Authorization;
                return auth === "Bearer test-token";
            });
            expect(called).toBe(true);
        });

        expect(createSpy).toHaveBeenCalledTimes(1);
        expect(revokeSpy).toHaveBeenCalledTimes(1);
        clickSpy.mockRestore();
    });

    // -------------------- RC6: comment tile --------------------

    it("offers a Comment entry in the palette", async () => {
        signInAs(["Admin"]);
        stubFetch([
            {
                match: (u) => u.endsWith("/api/admin/report-groups"),
                status: 200,
                body: [],
            },
            {
                match: (u) => u.endsWith("/api/admin/reports/7"),
                status: 200,
                body: detailBody([]),
            },
        ]);
        renderEditor();

        const user = userEvent.setup();
        await user.click(await screen.findByRole("button", { name: /add tile/i }));
        const menuItems = await screen.findAllByRole("menuitem");
        const commentItem = menuItems.find((el) =>
            /^comment$/i.test((el.textContent ?? "").trim()),
        );
        expect(commentItem).toBeDefined();
    });

    it("shows the markdown textarea for a comment tile and saves as JSON", async () => {
        signInAs(["Admin"]);
        let savedBody: unknown = null;
        const fetchMock = stubFetch([
            {
                match: (u) => u.endsWith("/api/admin/report-groups"),
                status: 200,
                body: [],
            },
            {
                match: (u, i) =>
                    u.endsWith("/api/admin/reports/7") && (i?.method ?? "GET") === "GET",
                status: 200,
                body: detailBody([
                    {
                        id: 400,
                        tileType: "comment",
                        displayOrder: 0,
                        configJson: "{\"markdown\":\"Existing note.\"}",
                        title: "Notes",
                    },
                ]),
            },
            {
                match: (u, i) =>
                    /\/api\/admin\/reports\/7\/entities\/400$/.test(u) &&
                    i?.method === "PUT",
                status: 204,
            },
        ]);
        const orig = fetchMock.getMockImplementation()!;
        fetchMock.mockImplementation(async (input, init) => {
            const url =
                typeof input === "string" ? input : (input as Request).url;
            if (
                /\/api\/admin\/reports\/7\/entities\/400$/.test(url) &&
                init?.method === "PUT"
            ) {
                savedBody = init.body ? JSON.parse(init.body as string) : null;
            }
            return orig(input, init);
        });

        renderEditor();

        const row = await screen.findByTestId("tile-row-400");
        const user = userEvent.setup();

        // The comment tile shows a "Markdown" textarea rather than the
        // "Config JSON" textarea.
        const markdown = within(row).getByLabelText(/markdown/i);
        expect(markdown).toHaveValue("Existing note.");
        expect(
            within(row).queryByLabelText(/config json/i),
        ).not.toBeInTheDocument();

        await user.clear(markdown);
        await user.type(markdown, "Hello world.");
        await user.click(within(row).getByRole("button", { name: /save tile/i }));

        await waitFor(() => {
            expect(savedBody).not.toBeNull();
        });
        expect(savedBody).toMatchObject({
            tileType: "comment",
            configJson: JSON.stringify({ markdown: "Hello world." }),
        });
    });
});
