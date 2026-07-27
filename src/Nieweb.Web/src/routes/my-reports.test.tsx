import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Mock } from "vitest";
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import { userEvent } from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import i18n from "../i18n";
import { MyReportsRoute } from "./my-reports";
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
            statusText: hit.status < 400 ? "OK" : "Error",
            headers: { "Content-Type": "application/json" },
        });
    });
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock as Mock;
}

function signInAs(roles: string[]) {
    useSessionStore.getState().setSession(
        {
            email: "author@nieweb.test",
            displayName: "Author One",
            roles,
            mustRotatePassword: false,
        },
        "test-token",
    );
}

function reportDto(id: number, overrides: Record<string, unknown> = {}) {
    return {
        id,
        title: "R",
        description: null,
        reportGroupId: null,
        groupName: null,
        ownerUserId: 1,
        ownerDisplayName: "Author One",
        isLocked: false,
        isPinnedHome: false,
        refreshFrequencySeconds: null,
        chromeJson: null,
        displayOrder: 0,
        entityCount: 0,
        createdUtc: new Date().toISOString(),
        lastModifiedUtc: new Date().toISOString(),
        ...overrides,
    };
}

function renderMyReports() {
    const client = new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
        },
    });
    return render(
        <MantineProvider>
            <QueryClientProvider client={client}>
                <MyReportsRoute />
            </QueryClientProvider>
        </MantineProvider>,
    );
}

describe("MyReportsRoute", () => {
    beforeEach(() => {
        void i18n.changeLanguage("en");
        useSessionStore.getState().clear();
        window.localStorage.clear();
    });

    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
    });

    it("shows a forbidden panel for a non-author user", async () => {
        signInAs(["Reader"]);
        stubFetch([{ match: (u) => u.endsWith("/api/reports/mine"), status: 200, body: [] }]);
        renderMyReports();
        expect(await screen.findByText(/author role/i)).toBeInTheDocument();
    });

    it("creates a report from a template and adds its tiles", async () => {
        signInAs(["Author"]);
        let entityCount = 0;
        let createChromeJson: string | null = null;
        const fetchMock = stubFetch([
            {
                match: (u, i) =>
                    u.endsWith("/api/reports/mine") && (i?.method ?? "GET") === "GET",
                status: 200,
                body: [],
            },
            {
                match: (u, i) => u.endsWith("/api/reports") && i?.method === "POST",
                status: 201,
                body: reportDto(77, { title: "Yield + defects" }),
            },
            {
                match: (u, i) =>
                    /\/api\/reports\/77\/entities$/.test(u) && i?.method === "POST",
                status: 201,
                body: {
                    id: 1,
                    reportId: 77,
                    tileType: "panelYield",
                    title: null,
                    displayOrder: 0,
                    configJson: "{}",
                    createdUtc: new Date().toISOString(),
                    lastModifiedUtc: new Date().toISOString(),
                },
            },
        ]);
        const orig = fetchMock.getMockImplementation()!;
        fetchMock.mockImplementation(async (input, init) => {
            const url =
                typeof input === "string" ? input : (input as Request).url;
            if (url.endsWith("/api/reports") && init?.method === "POST") {
                const parsed = init?.body
                    ? (JSON.parse(init.body as string) as { chromeJson?: string | null })
                    : null;
                createChromeJson = parsed?.chromeJson ?? null;
            }
            if (/\/api\/reports\/77\/entities$/.test(url) && init?.method === "POST") {
                entityCount++;
            }
            return orig(input, init);
        });

        renderMyReports();
        const user = userEvent.setup();
        await user.click(await screen.findByRole("button", { name: /new report/i }));
        const dialog = await screen.findByRole("dialog");
        await user.click(within(dialog).getByTestId("my-report-template-yield-and-defects"));
        await user.click(within(dialog).getByRole("button", { name: /^create$/i }));

        await waitFor(() => {
            expect(entityCount).toBe(2);
        });
        expect(createChromeJson).toBe(JSON.stringify({ defaultWindowPreset: "last7d" }));
    });
});
