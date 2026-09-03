import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, within } from "@testing-library/react";
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
import { AnalyseRoute } from "./analyse";
import { AnalyseProductDetailRoute } from "./analyse-product-detail";
import { useSessionStore } from "../state/session";

vi.mock("echarts-for-react", () => ({
    default: () => <div data-testid="mock-echarts" />,
}));

type Stub = {
    match: (url: string, init?: RequestInit) => boolean;
    status: number;
    body: unknown;
};

function stubFetch(stubs: Stub[]) {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url =
            typeof input === "string"
                ? input
                : input instanceof URL
                    ? input.toString()
                    : input.url;
        const hit = stubs.find((s) => s.match(url, init));
        if (!hit) {
            throw new Error(`Unexpected fetch: ${init?.method ?? "GET"} ${url}`);
        }
        const bodyText = typeof hit.body === "string" ? hit.body : JSON.stringify(hit.body);
        return new Response(bodyText, {
            status: hit.status,
            statusText: hit.status === 200 ? "OK" : "Error",
            headers: { "Content-Type": "application/json" },
        });
    });

    vi.stubGlobal("fetch", fetchMock);
    return fetchMock;
}

function renderAnalyse(initialEntries: string[] = ["/analyse"]) {
    const rootRoute = createRootRoute({ component: Outlet });
    const analyseRoute = createRoute({
        getParentRoute: () => rootRoute,
        path: "/analyse",
        component: AnalyseRoute,
    });
    const analyseProductDetailRoute = createRoute({
        getParentRoute: () => rootRoute,
        path: "/analyse/product/$productId",
        component: AnalyseProductDetailRoute,
    });
    const routeTree = rootRoute.addChildren([analyseRoute, analyseProductDetailRoute]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries }),
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

function signIn() {
    useSessionStore.setState({
        user: {
            email: "test@nieweb.local",
            displayName: "Test User",
            roles: ["Reader"],
            mustRotatePassword: false,
        },
        token: "test-token",
    });
}

const windowPayload = {
    window: {
        startUtc: "2026-08-01T00:00:00Z",
        endUtcExclusive: "2026-08-02T00:00:00Z",
        startEpochSeconds: 1785542400,
        endEpochSecondsExclusive: 1785628800,
    },
    machineIds: null,
    productIds: null,
    onlyLastInspection: true,
};

const liveSummaryPost = {
    source: { id: "postreflow", displayName: "Post-reflow", schemaVersion: "5.0", caps: 0 },
    filter: windowPayload,
    kpi: {
        totalPanels: 120,
        inspectedPanels: 115,
        goodPanels: 110,
        faultyPanels: 5,
        notInspectedPanels: 5,
        fpyPercent: 95.652173,
    },
    dedupeAppliedInMemory: false,
    dedupeNote: null,
};

const linePerformancePost = {
    source: { id: "postreflow", displayName: "Post-reflow", schemaVersion: "5.0", caps: 0 },
    filter: windowPayload,
    overallYield: {
        totalPanels: 120,
        inspectedPanels: 115,
        goodPanels: 110,
        faultyPanels: 5,
        notInspectedPanels: 5,
        fpyPercent: 95.652173,
    },
    overallDpmo: {
        testedObjectCount: 230,
        opportunityCount: 4600,
        defectBitCount: 12,
        dpmoPpm: 2608.695652,
    },
    byMachine: [
        {
            machineId: 10,
            machineName: "AOI-10",
            yield: {
                totalPanels: 60,
                inspectedPanels: 58,
                goodPanels: 55,
                faultyPanels: 3,
                notInspectedPanels: 2,
                fpyPercent: 94.827586,
            },
            dpmo: {
                testedObjectCount: 120,
                opportunityCount: 2400,
                defectBitCount: 5,
                dpmoPpm: 2083.333333,
            },
        },
    ],
    dedupeAppliedInMemory: false,
    dedupeNote: null,
};

const productSummaryPost = {
    source: { id: "postreflow", displayName: "Post-reflow", schemaVersion: "5.0", caps: 0 },
    filter: windowPayload,
    overallYield: {
        totalPanels: 120,
        inspectedPanels: 115,
        goodPanels: 110,
        faultyPanels: 5,
        notInspectedPanels: 5,
        fpyPercent: 95.652173,
    },
    overallDpmo: {
        testedObjectCount: 230,
        opportunityCount: 4600,
        defectBitCount: 12,
        dpmoPpm: 2608.695652,
    },
    products: [
        {
            productId: 200,
            productName: "Gadget",
            yield: {
                totalPanels: 50,
                inspectedPanels: 49,
                goodPanels: 48,
                faultyPanels: 1,
                notInspectedPanels: 1,
                fpyPercent: 97.959184,
            },
            dpmo: {
                testedObjectCount: 100,
                opportunityCount: 2000,
                defectBitCount: 9,
                dpmoPpm: 4500,
            },
            defectBitCount: 9,
            topDefectBits: [{ bitNumber: 4, count: 5 }],
        },
        {
            productId: 100,
            productName: "Widget",
            yield: {
                totalPanels: 60,
                inspectedPanels: 58,
                goodPanels: 55,
                faultyPanels: 3,
                notInspectedPanels: 2,
                fpyPercent: 94.827586,
            },
            dpmo: {
                testedObjectCount: 120,
                opportunityCount: 2400,
                defectBitCount: 5,
                dpmoPpm: 1200,
            },
            defectBitCount: 5,
            topDefectBits: [{ bitNumber: 1, count: 3 }],
        },
    ],
    dedupeAppliedInMemory: false,
    dedupeNote: null,
};

const liveSummaryPre = {
    source: { id: "prereflow", displayName: "Pre-reflow", schemaVersion: "4.3.1", caps: "Panels,Cards" },
    filter: windowPayload,
    kpi: {
        totalPanels: 45,
        inspectedPanels: 40,
        goodPanels: 38,
        faultyPanels: 2,
        notInspectedPanels: 5,
        fpyPercent: 95,
    },
    dedupeAppliedInMemory: true,
    dedupeNote: "fallback",
};

const linePerformancePre = {
    source: { id: "prereflow", displayName: "Pre-reflow", schemaVersion: "4.3.1", caps: "Panels,Cards" },
    filter: windowPayload,
    overallYield: {
        totalPanels: 45,
        inspectedPanels: 40,
        goodPanels: 38,
        faultyPanels: 2,
        notInspectedPanels: 5,
        fpyPercent: 95,
    },
    overallDpmo: {
        testedObjectCount: 80,
        opportunityCount: 1600,
        defectBitCount: 4,
        dpmoPpm: 2500,
    },
    byMachine: [],
    dedupeAppliedInMemory: true,
    dedupeNote: "fallback",
};

const productSummaryPre = {
    source: { id: "prereflow", displayName: "Pre-reflow", schemaVersion: "4.3.1", caps: "Panels,Cards" },
    filter: windowPayload,
    overallYield: {
        totalPanels: 45,
        inspectedPanels: 40,
        goodPanels: 38,
        faultyPanels: 2,
        notInspectedPanels: 5,
        fpyPercent: 95,
    },
    overallDpmo: {
        testedObjectCount: 80,
        opportunityCount: 1600,
        defectBitCount: 4,
        dpmoPpm: 2500,
    },
    products: [],
    dedupeAppliedInMemory: true,
    dedupeNote: "fallback",
};

const panelSummaryPost = {
    source: { id: "postreflow", displayName: "Post-reflow", schemaVersion: "5.0", caps: 0 },
    filter: windowPayload,
    overallYield: {
        totalPanels: 120,
        inspectedPanels: 115,
        goodPanels: 110,
        faultyPanels: 5,
        notInspectedPanels: 5,
        fpyPercent: 95.652173,
    },
    overallDpmo: {
        testedObjectCount: 230,
        opportunityCount: 4600,
        defectBitCount: 12,
        dpmoPpm: 2608.695652,
    },
    totalPanels: 2,
    panels: [
        {
            panelId: 42,
            barcode: "PANEL-42",
            panelUtc: "2026-08-01T10:00:00Z",
            productId: 200,
            productName: "Gadget",
            machineId: 10,
            machineName: "AOI-10",
            panelStatus: -1,
            defectBitCount: 7,
            testedObjectCount: 3,
            topDefectBits: [{ bitNumber: 4, count: 5 }],
        },
        {
            panelId: 43,
            barcode: "PANEL-43",
            panelUtc: "2026-08-01T11:00:00Z",
            productId: 100,
            productName: "Widget",
            machineId: 10,
            machineName: "AOI-10",
            panelStatus: 1,
            defectBitCount: 1,
            testedObjectCount: 1,
            topDefectBits: [{ bitNumber: 1, count: 1 }],
        },
    ],
    dedupeAppliedInMemory: false,
    dedupeNote: null,
};

const panelSummaryPre = {
    ...panelSummaryPost,
    source: { id: "prereflow", displayName: "Pre-reflow", schemaVersion: "4.3.1", caps: "Panels,Cards" },
    panels: [],
    totalPanels: 0,
    dedupeAppliedInMemory: true,
    dedupeNote: "fallback",
};

const cpCpkPost = {
    source: { id: "postreflow", displayName: "Post-reflow", schemaVersion: "5.0", caps: 0 },
    filter: {
        ...windowPayload,
        componentItx: null,
        componentIty: null,
        componentIts: null,
        pasteItx: null,
        pasteIty: null,
        pasteIts: null,
    },
    rows: [
        {
            axis: "DeltaX",
            opportunity: "Components",
            sampleCount: 5,
            mean: 0,
            stdDev: 7.905694,
            min: -10,
            max: 10,
            toleranceInterval: 60,
            toleranceConfigured: true,
            cp: 1.264911,
            cpk: 1.264911,
        },
        {
            axis: "DeltaY",
            opportunity: "Components",
            sampleCount: 0,
            mean: null,
            stdDev: 0,
            min: null,
            max: null,
            toleranceInterval: null,
            toleranceConfigured: false,
            cp: null,
            cpk: null,
        },
    ],
    dedupeAppliedInMemory: false,
    dedupeNote: null,
};

const cpCpkPre = {
    ...cpCpkPost,
    source: { id: "prereflow", displayName: "Pre-reflow", schemaVersion: "4.3.1", caps: "Panels,Cards" },
    rows: [],
    dedupeAppliedInMemory: true,
    dedupeNote: "fallback",
};

const productDetailPostDay = {
    source: { id: "postreflow", displayName: "Post-reflow", schemaVersion: "5.0", caps: 0 },
    filter: {
        ...windowPayload,
        bucket: "Day",
        productId: 200,
    },
    productId: 200,
    productName: "Gadget",
    overallYield: {
        totalPanels: 50,
        inspectedPanels: 49,
        goodPanels: 48,
        faultyPanels: 1,
        notInspectedPanels: 1,
        fpyPercent: 97.959184,
    },
    overallDpmo: {
        testedObjectCount: 100,
        opportunityCount: 2000,
        defectBitCount: 9,
        dpmoPpm: 4500,
    },
    buckets: [
        {
            index: 0,
            label: "2026-08-01",
            startUtc: "2026-08-01T00:00:00Z",
            endUtcExclusive: "2026-08-02T00:00:00Z",
        },
    ],
    trend: [
        {
            bucketIndex: 0,
            label: "2026-08-01",
            yield: {
                totalPanels: 50,
                inspectedPanels: 49,
                goodPanels: 48,
                faultyPanels: 1,
                notInspectedPanels: 1,
                fpyPercent: 97.959184,
            },
            dpmo: {
                testedObjectCount: 100,
                opportunityCount: 2000,
                defectBitCount: 9,
                dpmoPpm: 4500,
            },
            defectBitCount: 5,
            topDefectBits: [
                { bitNumber: 4, count: 5 },
            ],
        },
    ],
    topDefectBits: [
        { bitNumber: 4, count: 5 },
    ],
    dedupeAppliedInMemory: false,
    dedupeNote: null,
};

const productDetailPostWeek = {
    ...productDetailPostDay,
    filter: {
        ...windowPayload,
        bucket: "Week",
        productId: 200,
    },
    buckets: [
        {
            index: 0,
            label: "2026-W31",
            startUtc: "2026-07-27T00:00:00Z",
            endUtcExclusive: "2026-08-03T00:00:00Z",
        },
    ],
    trend: [
        {
            bucketIndex: 0,
            label: "2026-W31",
            yield: productDetailPostDay.trend[0].yield,
            dpmo: productDetailPostDay.trend[0].dpmo,
            defectBitCount: productDetailPostDay.trend[0].defectBitCount,
            topDefectBits: productDetailPostDay.trend[0].topDefectBits,
        },
    ],
};

describe("AnalyseRoute", () => {
    beforeEach(async () => {
        signIn();
        await i18n.changeLanguage("en");
    });

    afterEach(() => {
        cleanup();
        useSessionStore.setState({ user: null, token: null });
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it("auto-selects the first available source and loads contracts", async () => {
        const fetchMock = stubFetch([
            { match: (u) => u.endsWith("/auth/config"), status: 200, body: { oidcEnabled: false, oidcButtonLabel: "", oidcChallengePath: "", analyseEnabled: true } },
            {
                match: (u) => u.endsWith("/api/sources"),
                status: 200,
                body: [
                    { id: "prereflow", displayName: "Pre-reflow", schemaVersion: "4.3.1", capabilities: ["Panels", "Cards"], latestPanelUtc: null, available: false },
                    { id: "postreflow", displayName: "Post-reflow", schemaVersion: "5.0", capabilities: ["Panels", "Cards", "MachineEfficiencyTiming"], latestPanelUtc: null, available: true },
                ],
            },
            {
                match: (u) => u.includes("/api/analyse/contracts") && u.includes("sourceId=postreflow"),
                status: 200,
                body: {
                    source: { id: "postreflow", displayName: "Post-reflow", schemaVersion: "5.0", caps: 0 },
                    filter: windowPayload,
                    dashboards: [
                        { dashboard: "Live", supported: true, missingCapabilities: [], features: [{ featureId: "latest-inspection-filter", supported: true, missingCapability: null, note: null }] },
                        { dashboard: "LinePerformance", supported: true, missingCapabilities: [], features: [{ featureId: "machine-efficiency-time-pie", supported: true, missingCapability: null, note: null }] },
                        { dashboard: "Product", supported: true, missingCapabilities: [], features: [] },
                    ],
                },
            },
            { match: (u) => u.includes("/api/analyse/live-summary") && u.includes("sourceId=postreflow"), status: 200, body: liveSummaryPost },
            { match: (u) => u.includes("/api/analyse/line-performance-summary") && u.includes("sourceId=postreflow"), status: 200, body: linePerformancePost },
            { match: (u) => u.includes("/api/analyse/product-summary") && u.includes("sourceId=postreflow"), status: 200, body: productSummaryPost },
            { match: (u) => u.includes("/api/analyse/panel-summary") && u.includes("sourceId=postreflow"), status: 200, body: panelSummaryPost },
            { match: (u) => u.includes("/api/analyse/cp-cpk") && u.includes("sourceId=postreflow"), status: 200, body: cpCpkPost },
        ]);

        renderAnalyse();

        expect(await screen.findByRole("heading", { name: "Analyse" })).toBeInTheDocument();
        expect(await screen.findByText("Live")).toBeInTheDocument();
        expect(await screen.findByTestId("analyse-live-summary-card")).toBeInTheDocument();
        expect(await screen.findByTestId("analyse-line-performance-card")).toBeInTheDocument();
        expect(await screen.findByTestId("analyse-product-summary-card")).toBeInTheDocument();
        expect(await screen.findByTestId("analyse-panel-summary-card")).toBeInTheDocument();
        expect(await screen.findByTestId("analyse-cp-cpk-card")).toBeInTheDocument();
        expect(await screen.findByTestId("analyse-panel-row-0")).toBeInTheDocument();
        expect(await screen.findByTestId("analyse-cp-cpk-row-0")).toBeInTheDocument();
        expect(await screen.findByRole("radiogroup", { name: "Sort panel cards by" })).toBeInTheDocument();

        const panelUser = userEvent.setup();
        const defaultTopPanel = await screen.findByTestId("analyse-panel-row-0");
        expect(within(defaultTopPanel).getByText("PANEL-42")).toBeInTheDocument();

        await panelUser.click(screen.getByRole("radio", { name: "Barcode" }));
        const barcodeTopPanel = await screen.findByTestId("analyse-panel-row-0");
        expect(within(barcodeTopPanel).getByText("PANEL-42")).toBeInTheDocument();
        expect(await screen.findByRole("radiogroup", { name: "Sort product cards by" })).toBeInTheDocument();
        expect(await screen.findByTestId("analyse-product-detail-200")).toBeInTheDocument();

        const user = userEvent.setup();
        const defaultTopProduct = await screen.findByTestId("analyse-product-row-0");
        expect(within(defaultTopProduct).getByText("Gadget")).toBeInTheDocument();

        await user.click(screen.getByRole("radio", { name: "FPY" }));
        const fpyTopProduct = await screen.findByTestId("analyse-product-row-0");
        expect(within(fpyTopProduct).getByText("Widget")).toBeInTheDocument();

        const contractCalls = fetchMock.mock.calls.filter((c) => (typeof c[0] === "string" ? c[0] : c[0].toString()).includes("/api/analyse/contracts"));
        expect(contractCalls.length).toBe(1);
        const firstUrl = typeof contractCalls[0][0] === "string" ? contractCalls[0][0] : contractCalls[0][0].toString();
        expect(firstUrl).toContain("sourceId=postreflow");
    });

    it("navigates to product detail placeholder from a product card action", async () => {
        stubFetch([
            { match: (u) => u.endsWith("/auth/config"), status: 200, body: { oidcEnabled: false, oidcButtonLabel: "", oidcChallengePath: "", analyseEnabled: true } },
            {
                match: (u) => u.endsWith("/api/sources"),
                status: 200,
                body: [
                    { id: "postreflow", displayName: "Post-reflow", schemaVersion: "5.0", capabilities: ["Panels", "Cards", "MachineEfficiencyTiming"], latestPanelUtc: null, available: true },
                ],
            },
            {
                match: (u) => u.includes("/api/analyse/contracts") && u.includes("sourceId=postreflow"),
                status: 200,
                body: {
                    source: { id: "postreflow", displayName: "Post-reflow", schemaVersion: "5.0", caps: 0 },
                    filter: windowPayload,
                    dashboards: [
                        { dashboard: "Live", supported: true, missingCapabilities: [], features: [{ featureId: "latest-inspection-filter", supported: true, missingCapability: null, note: null }] },
                        { dashboard: "LinePerformance", supported: true, missingCapabilities: [], features: [] },
                        { dashboard: "Product", supported: true, missingCapabilities: [], features: [] },
                    ],
                },
            },
            { match: (u) => u.includes("/api/analyse/live-summary") && u.includes("sourceId=postreflow"), status: 200, body: liveSummaryPost },
            { match: (u) => u.includes("/api/analyse/line-performance-summary") && u.includes("sourceId=postreflow"), status: 200, body: linePerformancePost },
            { match: (u) => u.includes("/api/analyse/product-summary") && u.includes("sourceId=postreflow"), status: 200, body: productSummaryPost },
            { match: (u) => u.includes("/api/analyse/panel-summary") && u.includes("sourceId=postreflow"), status: 200, body: panelSummaryPost },
            { match: (u) => u.includes("/api/analyse/cp-cpk") && u.includes("sourceId=postreflow"), status: 200, body: cpCpkPost },
            { match: (u) => u.includes("/api/analyse/product-detail/200") && u.includes("sourceId=postreflow") && u.includes("bucket=Day"), status: 200, body: productDetailPostDay },
            { match: (u) => u.includes("/api/analyse/product-detail/200") && u.includes("sourceId=postreflow") && u.includes("bucket=Week"), status: 200, body: productDetailPostWeek },
        ]);

        renderAnalyse();
        const user = userEvent.setup();

        const detailLinks = await screen.findAllByRole("link", { name: "Open detail view" });
        await user.click(detailLinks[0]);

        expect(await screen.findByRole("heading", { name: "Product detail" })).toBeInTheDocument();
        expect(await screen.findByText("Product ID: 200")).toBeInTheDocument();
        expect(await screen.findByText("Trend by bucket")).toBeInTheDocument();
        expect(await screen.findByRole("img", { name: "Product trend chart across buckets" })).toBeInTheDocument();
        expect(await screen.findByTestId("analyse-product-detail-bucket")).toBeInTheDocument();
        expect((await screen.findAllByText("b4: 5")).length).toBe(2);

        await user.click(screen.getByTestId("analyse-product-detail-bucket"));
        const weekOption = await screen.findByText("Week");
        await user.click(weekOption);

        expect(await screen.findByText("2026-W31")).toBeInTheDocument();
        expect((await screen.findAllByText("b4: 5")).length).toBe(2);
    });

    it("reloads contracts when the user switches source", async () => {
        const fetchMock = stubFetch([
            { match: (u) => u.endsWith("/auth/config"), status: 200, body: { oidcEnabled: false, oidcButtonLabel: "", oidcChallengePath: "", analyseEnabled: true } },
            {
                match: (u) => u.endsWith("/api/sources"),
                status: 200,
                body: [
                    { id: "postreflow", displayName: "Post-reflow", schemaVersion: "5.0", capabilities: ["Panels", "Cards", "MachineEfficiencyTiming"], latestPanelUtc: null, available: true },
                    { id: "prereflow", displayName: "Pre-reflow", schemaVersion: "4.3.1", capabilities: ["Panels", "Cards"], latestPanelUtc: null, available: true },
                ],
            },
            {
                match: (u) => u.includes("/api/analyse/contracts") && u.includes("sourceId=postreflow"),
                status: 200,
                body: {
                    source: { id: "postreflow", displayName: "Post-reflow", schemaVersion: "5.0", caps: 0 },
                    filter: windowPayload,
                    dashboards: [
                        { dashboard: "Live", supported: true, missingCapabilities: [], features: [{ featureId: "latest-inspection-filter", supported: true, missingCapability: null, note: null }] },
                        { dashboard: "LinePerformance", supported: true, missingCapabilities: [], features: [{ featureId: "machine-efficiency-time-pie", supported: true, missingCapability: null, note: null }] },
                        { dashboard: "Product", supported: true, missingCapabilities: [], features: [] },
                    ],
                },
            },
            { match: (u) => u.includes("/api/analyse/live-summary") && u.includes("sourceId=postreflow"), status: 200, body: liveSummaryPost },
            { match: (u) => u.includes("/api/analyse/line-performance-summary") && u.includes("sourceId=postreflow"), status: 200, body: linePerformancePost },
            { match: (u) => u.includes("/api/analyse/product-summary") && u.includes("sourceId=postreflow"), status: 200, body: productSummaryPost },
            {
                match: (u) => u.includes("/api/analyse/contracts") && u.includes("sourceId=prereflow"),
                status: 200,
                body: {
                    source: { id: "prereflow", displayName: "Pre-reflow", schemaVersion: "4.3.1", caps: "Panels,Cards" },
                    filter: windowPayload,
                    dashboards: [
                        { dashboard: "Live", supported: true, missingCapabilities: [], features: [{ featureId: "latest-inspection-filter", supported: false, missingCapability: "IsLastInspectionFilter", note: "missing" }] },
                        { dashboard: "LinePerformance", supported: true, missingCapabilities: [], features: [{ featureId: "machine-efficiency-time-pie", supported: false, missingCapability: "MachineEfficiencyTiming", note: "missing" }] },
                        { dashboard: "Product", supported: true, missingCapabilities: [], features: [] },
                    ],
                },
            },
            { match: (u) => u.includes("/api/analyse/live-summary") && u.includes("sourceId=prereflow"), status: 200, body: liveSummaryPre },
            { match: (u) => u.includes("/api/analyse/line-performance-summary") && u.includes("sourceId=prereflow"), status: 200, body: linePerformancePre },
            { match: (u) => u.includes("/api/analyse/product-summary") && u.includes("sourceId=prereflow"), status: 200, body: productSummaryPre },
            { match: (u) => u.includes("/api/analyse/panel-summary") && u.includes("sourceId=prereflow"), status: 200, body: panelSummaryPre },
            { match: (u) => u.includes("/api/analyse/cp-cpk") && u.includes("sourceId=prereflow"), status: 200, body: cpCpkPre },
        ]);

        renderAnalyse();
        await screen.findByText("LinePerformance");

        const user = userEvent.setup();
        const sourceInput = screen.getByTestId("analyse-source-select");
        await user.click(sourceInput);

        const listboxId = sourceInput.getAttribute("aria-controls");
        const listbox = listboxId ? document.getElementById(listboxId) : null;
        expect(listbox).not.toBeNull();

        const prereflowOption = await within(listbox as HTMLElement).findByText("Pre-reflow (prereflow)");
        await user.click(prereflowOption);

        expect(await screen.findByText("IsLastInspectionFilter")).toBeInTheDocument();

        const contractCalls = fetchMock.mock.calls.filter((c) => (typeof c[0] === "string" ? c[0] : c[0].toString()).includes("/api/analyse/contracts"));
        const summaryCalls = fetchMock.mock.calls.filter((c) => (typeof c[0] === "string" ? c[0] : c[0].toString()).includes("/api/analyse/live-summary"));
        const linePerformanceCalls = fetchMock.mock.calls.filter((c) => (typeof c[0] === "string" ? c[0] : c[0].toString()).includes("/api/analyse/line-performance-summary"));
        const productCalls = fetchMock.mock.calls.filter((c) => (typeof c[0] === "string" ? c[0] : c[0].toString()).includes("/api/analyse/product-summary"));
        const panelCalls = fetchMock.mock.calls.filter((c) => (typeof c[0] === "string" ? c[0] : c[0].toString()).includes("/api/analyse/panel-summary"));
        const cpCpkCalls = fetchMock.mock.calls.filter((c) => (typeof c[0] === "string" ? c[0] : c[0].toString()).includes("/api/analyse/cp-cpk"));

        expect(contractCalls.length).toBe(2);
        expect(summaryCalls.length).toBe(2);
        expect(linePerformanceCalls.length).toBe(2);
        expect(productCalls.length).toBe(2);
        expect(panelCalls.length).toBe(2);
        expect(cpCpkCalls.length).toBe(2);

        const urls = contractCalls.map((c) => (typeof c[0] === "string" ? c[0] : c[0].toString()));
        const summaryUrls = summaryCalls.map((c) => (typeof c[0] === "string" ? c[0] : c[0].toString()));
        const linePerformanceUrls = linePerformanceCalls.map((c) => (typeof c[0] === "string" ? c[0] : c[0].toString()));
        const productUrls = productCalls.map((c) => (typeof c[0] === "string" ? c[0] : c[0].toString()));

        expect(urls[0]).toContain("sourceId=postreflow");
        expect(urls[1]).toContain("sourceId=prereflow");
        expect(summaryUrls[0]).toContain("sourceId=postreflow");
        expect(summaryUrls[1]).toContain("sourceId=prereflow");
        expect(linePerformanceUrls[0]).toContain("sourceId=postreflow");
        expect(linePerformanceUrls[1]).toContain("sourceId=prereflow");
        expect(productUrls[0]).toContain("sourceId=postreflow");
        expect(productUrls[1]).toContain("sourceId=prereflow");
    });
});
