import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { userEvent } from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { I18nextProvider } from "react-i18next";
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
import { ParetoRoute } from "./pareto";
import { validateParetoSearch } from "./pareto.search";
import type { ParetoResult, ParetoRow } from "../api/pareto";

/**
 * Route-level Pareto tests. The chart is mocked so bar clicks go
 * through the same `onBarClick` callback ParetoChart would fire from
 * ECharts — no canvas coordinates.
 */

type Captured = {
    onBarClick: ((row: ParetoRow, index: number) => void) | undefined;
    axis: string | undefined;
};
const captured: Captured = { onBarClick: undefined, axis: undefined };

vi.mock("../charts/ParetoChart", () => ({
    ParetoChart: (props: {
        onBarClick?: (row: ParetoRow, index: number) => void;
        axis: string;
    }) => {
        captured.onBarClick = props.onBarClick;
        captured.axis = props.axis;
        return <div data-testid="mock-echarts" data-axis={props.axis} />;
    },
}));

const START = "2026-01-01T00:00:00.000Z";
const END = "2026-01-02T00:00:00.000Z";
const SOURCE = "postreflow";

function mkRow(
    groupKey: string | null,
    groupName: string | null,
    defectCount: number,
    opportunityCount: number,
): ParetoRow {
    return {
        groupKey,
        groupName,
        defectCount,
        weightedScore: defectCount,
        opportunityCount,
        opportunitySharePercent: opportunityCount > 0 ? 100 : 0,
        dpmoPpm:
            opportunityCount > 0
                ? (defectCount * 1_000_000) / opportunityCount
                : 0,
        defectSharePercent: 100,
        cumulativePercent: 100,
        isVitalFew: true,
        opportunitiesApplicable: opportunityCount > 0,
    };
}

const REFDES_ROW = mkRow("R12", "R12", 5, 0);
const SUBPANEL_ROW = mkRow("1", "1", 5, 200);
const OTHERS_ROW = mkRow(null, null, 2, 0);

function resultForAxis(axis: string, url: string): ParetoResult {
    const parsed = new URL(url, "http://localhost");
    const topologies = parsed.searchParams.get("topologies");
    const cardNumbers = parsed.searchParams.get("cardNumbers");
    const isRefDes = axis === "ReferenceDesignator";
    const isSubpanel = axis === "Subpanel";
    return {
        source: { id: SOURCE, displayName: "Post-reflow AOI" },
        window: { startUtc: START, endUtcExclusive: END },
        axis: (isRefDes
            ? "ReferenceDesignator"
            : isSubpanel
              ? "Subpanel"
              : "Defect") as ParetoResult["axis"],
        numerator: "Real",
        opportunity: "All",
        weight: (parsed.searchParams.get("weight") as ParetoResult["weight"]) || "Count",
        appliedFilters: {
            machineIds: [],
            productIds: [],
            defectBits: [],
            topologies: topologies ? topologies.split(",") : [],
            partNumbers: [],
            jedecNames: [],
            cardNumbers: cardNumbers ? cardNumbers.split(",").map(Number) : [],
        },
        overall: {
            testedObjectCount: 10,
            opportunityCount: 200,
            defectBitCount: 5,
            dpmoPpm: 25_000,
        },
        rows: isRefDes ? [REFDES_ROW] : isSubpanel ? [SUBPANEL_ROW] : [mkRow("1", "bit 1", 5, 200)],
        othersBucket: isRefDes ? OTHERS_ROW : null,
        skipExclusion: "Raw",
        skipExcludedCards: 0,
        vitalFewThresholdPercent: 80,
    };
}

function stubFetch(): void {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
        const url =
            typeof input === "string"
                ? input
                : input instanceof URL
                  ? input.toString()
                  : input.url;
        if (url.includes("/api/sources") && url.includes("/machines")) {
            return json([]);
        }
        if (url.includes("/api/sources") && url.includes("/products")) {
            return json([]);
        }
        if (url.includes("/active-filters")) {
            return json({ pairs: [] });
        }
        if (url.endsWith("/api/sources") || /\/api\/sources\/?$/.test(url)) {
            return json([
                {
                    id: SOURCE,
                    displayName: "Post-reflow AOI",
                    available: true,
                },
            ]);
        }
        if (url.includes("/api/saved-views")) {
            return json([]);
        }
        if (url.includes("/api/reports/pareto")) {
            const parsed = new URL(url, "http://localhost");
            const axis = parsed.searchParams.get("axis") ?? "Defect";
            return json(resultForAxis(axis, url));
        }
        throw new Error(`Unexpected fetch: ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);
}

function json(body: unknown): Response {
    return new Response(JSON.stringify(body), {
        status: 200,
        statusText: "OK",
        headers: { "Content-Type": "application/json" },
    });
}

function paretoPath(search: Record<string, string>): string {
    return `/report/pareto?${new URLSearchParams(search).toString()}`;
}

function renderPareto(initialPath: string) {
    captured.onBarClick = undefined;
    captured.axis = undefined;
    const rootRoute = createRootRoute({ component: Outlet });
    const pareto = createRoute({
        getParentRoute: () => rootRoute,
        path: "/report/pareto",
        component: ParetoRoute,
        validateSearch: validateParetoSearch,
    });
    const routeTree = rootRoute.addChildren([pareto]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: [initialPath] }),
    });
    const client = new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
        },
    });
    return {
        router,
        ...render(
            <I18nextProvider i18n={i18n}>
                <MantineProvider>
                    <QueryClientProvider client={client}>
                        <RouterProvider router={router} />
                    </QueryClientProvider>
                </MantineProvider>
            </I18nextProvider>,
        ),
    };
}

async function waitForResults(): Promise<void> {
    await screen.findByText(/Total defects/i, {}, { timeout: 5000 });
    await screen.findByTestId("mock-echarts", {}, { timeout: 5000 });
}

const BASE_SEARCH = {
    sourceId: SOURCE,
    startUtc: START,
    endUtc: END,
};

describe("ParetoRoute drill and URL sync", () => {
    beforeEach(() => {
        void i18n.changeLanguage("en");
        stubFetch();
    });

    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it("drills a Reference designator bar into Subpanel with that topology", async () => {
        const { router } = renderPareto(
            paretoPath({ ...BASE_SEARCH, axis: "ReferenceDesignator" }),
        );
        await waitForResults();
        expect(captured.onBarClick).toEqual(expect.any(Function));
        captured.onBarClick!(REFDES_ROW, 0);

        await waitFor(() => {
            const search = router.state.location.search as {
                axis?: string;
                topologies?: string[];
            };
            expect(search.axis).toBe("Subpanel");
            expect(search.topologies).toEqual(["R12"]);
        });
        await waitFor(() => {
            expect(screen.getByTestId("pareto-axis")).toHaveValue("Subpanel");
        });
        expect(
            screen.getByText(/Reference designator: R12/i),
        ).toBeInTheDocument();
    });

    it("does not attach a working drill on Subpanel; weight stays enabled and the N/A banner is absent", async () => {
        const { router } = renderPareto(
            paretoPath({
                ...BASE_SEARCH,
                axis: "Subpanel",
                weight: "Dpmo",
            }),
        );
        await waitForResults();

        expect(screen.getByTestId("pareto-axis")).toHaveValue("Subpanel");
        expect(screen.getByTestId("pareto-weight")).not.toBeDisabled();
        expect(
            screen.queryByText(/Opportunity counts and DPMO are not available/i),
        ).not.toBeInTheDocument();
        expect(captured.onBarClick).toBeUndefined();

        const before = router.state.location.href;
        expect(router.state.location.href).toBe(before);
        expect(
            (router.state.location.search as { cardNumbers?: number[] }).cardNumbers,
        ).toBeUndefined();
    });

    it("renders a cardNumbers chip and removing it drops the filter from the URL", async () => {
        const user = userEvent.setup();
        const { router } = renderPareto(
            paretoPath({
                ...BASE_SEARCH,
                axis: "Subpanel",
                cardNumbers: "0",
            }),
        );
        await waitForResults();
        expect(await screen.findByText("Subpanel: 0")).toBeInTheDocument();

        await user.click(
            screen.getByRole("button", { name: /Remove filter Subpanel: 0/i }),
        );
        await waitFor(() => {
            const search = router.state.location.search as {
                cardNumbers?: number[];
            };
            expect(search.cardNumbers).toBeUndefined();
        });
        expect(screen.queryByText("Subpanel: 0")).not.toBeInTheDocument();
    });

    it("restores axis, chips, and results on Back after a RefDes drill", async () => {
        const { router } = renderPareto(
            paretoPath({ ...BASE_SEARCH, axis: "ReferenceDesignator" }),
        );
        await waitForResults();
        captured.onBarClick!(REFDES_ROW, 0);
        await waitFor(() => {
            expect(
                (router.state.location.search as { axis?: string }).axis,
            ).toBe("Subpanel");
        });
        await waitFor(() => {
            expect(screen.getByTestId("pareto-axis")).toHaveValue("Subpanel");
        });

        router.history.back();

        await waitFor(() => {
            expect(
                (router.state.location.search as { axis?: string }).axis,
            ).toBe("ReferenceDesignator");
        });
        await waitFor(() => {
            expect(screen.getByTestId("pareto-axis")).toHaveValue(
                "Reference designator",
            );
        });
        expect(
            screen.queryByText(/Reference designator: R12/i),
        ).not.toBeInTheDocument();
        await waitForResults();
        expect(captured.axis).toBe("ReferenceDesignator");
    });

    it("does not drill the Others bucket on a drillable axis", async () => {
        const { router } = renderPareto(
            paretoPath({ ...BASE_SEARCH, axis: "ReferenceDesignator" }),
        );
        await waitForResults();
        const before = router.state.location.href;
        captured.onBarClick!(OTHERS_ROW, 1);
        expect(router.state.location.href).toBe(before);
        expect(
            (router.state.location.search as { axis?: string }).axis,
        ).toBe("ReferenceDesignator");
    });
});
