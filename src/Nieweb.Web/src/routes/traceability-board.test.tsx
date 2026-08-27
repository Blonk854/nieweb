import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Mock } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
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
import { TraceabilityBoardRoute } from "./traceability-board";
import { validateTraceabilityBoardSearch } from "./traceability-board.search";
import { useSessionStore } from "../state/session";

/**
 * Component tests for the TC3 board-trace route
 * (`/traceability/board`). Verifies the empty-prompt, loading,
 * not-found and stage-rendering paths, plus URL-driven state (the
 * report re-runs when the barcode search-param changes).
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
        const bodyText =
            hit.body === undefined
                ? ""
                : typeof hit.body === "string"
                    ? hit.body
                    : JSON.stringify(hit.body);
        return new Response(bodyText, {
            status: hit.status,
            statusText: hit.status === 200 ? "OK" : "Error",
            headers: { "Content-Type": "application/json" },
        });
    });
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock as Mock;
}

function renderBoard(initialPath: string) {
    const rootRoute = createRootRoute({ component: Outlet });
    const boardRoute = createRoute({
        getParentRoute: () => rootRoute,
        path: "/traceability/board",
        component: TraceabilityBoardRoute,
        validateSearch: validateTraceabilityBoardSearch,
    });
    const routeTree = rootRoute.addChildren([boardRoute]);
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
    render(
        <MantineProvider>
            <QueryClientProvider client={client}>
                <RouterProvider router={router} />
            </QueryClientProvider>
        </MantineProvider>,
    );
    return { router };
}

/** Minimal saved-views 200 response (empty list) so SavedViewsMenu doesn't 500 in tests. */
const savedViewsEmpty = {
    match: (u: string) => u.includes("/api/saved-views"),
    status: 200,
    body: [] as unknown[],
};

/** Build a fake `BoardTrace` response body. */
function boardTrace(barcode: string, opts: {
    postFound?: boolean;
    postError?: string | null;
    preFound?: boolean;
    preError?: string | null;
    postPriors?: Array<{
        panelId: number;
        faceNumber: number;
        panelUtc: string;
        panelStatus: number;
        anomalyBr: number;
        anomalyAr: number;
        nbOfErrorObject: number;
        hasBeenReviewed: boolean;
    }>;
    postPinned?: number | null;
    postPanelId?: number;
    postSelectionWarning?: string | null;
}) {
    const panel = (panelId: number) => ({
        panel: {
            panelId,
            machineId: 1,
            laneNumber: 0,
            panelBarCode: barcode,
            panelNumericDate: 1_780_660_800,
            nbOfValidCards: 2,
            testTime: 12.5,
            panelStatus: 1,
            anomalyBr: 0,
            anomalyAr: 0,
            hasBeenReviewed: true,
            nbOfTestedObject: 100,
            nbOfErrorObject: 3,
            operatorId: 7,
            productId: 42,
            recipeId: 99,
            faceNumber: 1,
        },
        panelUtc: "2026-06-05T12:00:00Z",
        productName: null,
        machineName: null,
        operatorName: null,
        productSvgKey: null,
    });
    const card = (cardId: number, panelId: number) => ({
        panelId,
        cardIdOnPanel: cardId,
        cardStatus: 1,
        anomalyBr: 0,
        anomalyAr: 0,
        nbOfTestedObject: 50,
        nbOfErrorObject: 1,
        machineId: 1,
        productId: 42,
        panelNumericDate: 1_780_660_800,
    });
    const sides = (
        panelId: number,
        cardCount: number,
        extras?: {
            priorPasses?: Array<{
                panelId: number;
                faceNumber: number;
                panelUtc: string;
                panelStatus: number;
                anomalyBr: number;
                anomalyAr: number;
                nbOfErrorObject: number;
                hasBeenReviewed: boolean;
            }>;
            pinnedPanelId?: number | null;
        },
    ) => [
        {
            faceNumber: 1,
            panel: panel(panelId),
            cards: Array.from({ length: cardCount }, (_, i) => card(i, panelId)),
            priorPasses: extras?.priorPasses ?? [],
            pinnedPanelId: extras?.pinnedPanelId ?? null,
        },
    ];
    return {
        barcode,
        stages: [
            {
                sourceId: "postreflow",
                sourceName: "Post-reflow AOI",
                capabilities: 1,
                sides: opts.postFound
                    ? sides(opts.postPanelId ?? 1001, 2, {
                          priorPasses: opts.postPriors,
                          pinnedPanelId: opts.postPinned,
                      })
                    : [],
                pinsAvailable: true,
                error: opts.postError ?? null,
                selectionWarning: opts.postSelectionWarning ?? null,
            },
            {
                sourceId: "prereflow",
                sourceName: "Pre-reflow AOI",
                capabilities: 0,
                sides: opts.preFound ? sides(2001, 1) : [],
                pinsAvailable: false,
                error: opts.preError ?? null,
                selectionWarning: null,
            },
        ],
    };
}

describe("TraceabilityBoardRoute", () => {
    beforeEach(() => {
        void i18n.changeLanguage("en");
        useSessionStore.setState({
            user: {
                email: "tester@example.com",
                displayName: "Tester",
                roles: ["Reader"],
                mustRotatePassword: false,
            },
            token: "fake-token",
        });
    });
    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
        useSessionStore.setState({ user: null, token: null });
    });

    it("shows the empty prompt when no barcode is in the URL", async () => {
        stubFetch([savedViewsEmpty]);
        renderBoard("/traceability/board");
        expect(
            await screen.findByText(/enter a panel barcode above to start/i),
        ).toBeInTheDocument();
    });

    it("fetches the barcode from the URL and renders both stages", async () => {
        stubFetch([
            savedViewsEmpty,
            {
                match: (u) => u.includes("/api/traceability/boards/by-barcode"),
                status: 200,
                body: boardTrace("BOARD-1", { postFound: true, preFound: true }),
            },
        ]);
        renderBoard("/traceability/board?barcode=BOARD-1");

        await waitFor(() => {
            expect(
                screen.getByTestId("traceability-board-stage-postreflow"),
            ).toBeInTheDocument();
        });
        expect(
            screen.getByTestId("traceability-board-stage-prereflow"),
        ).toBeInTheDocument();
        expect(screen.getByTestId("traceability-board-barcode")).toHaveTextContent("BOARD-1");
        // Two sub-panels on post-reflow stage, one on pre-reflow.
        expect(
            screen.getByTestId("traceability-board-cards-postreflow"),
        ).toBeInTheDocument();
        expect(
            screen.getByTestId("traceability-board-cards-prereflow"),
        ).toBeInTheDocument();
    });

    it("shows the not-found alert when the API returns 404", async () => {
        stubFetch([
            savedViewsEmpty,
            {
                match: (u) => u.includes("/api/traceability/boards/by-barcode"),
                status: 404,
                body: "not found",
            },
        ]);
        renderBoard("/traceability/board?barcode=UNKNOWN");
        expect(await screen.findByText(/barcode not found/i)).toBeInTheDocument();
    });

    it("renders a per-stage error alert when a stage's Error field is populated", async () => {
        stubFetch([
            savedViewsEmpty,
            {
                match: (u) => u.includes("/api/traceability/boards/by-barcode"),
                status: 200,
                body: boardTrace("BOARD-2", {
                    postFound: true,
                    preFound: false,
                    preError: "Timeout on pre-reflow",
                }),
            },
        ]);
        renderBoard("/traceability/board?barcode=BOARD-2");
        // The pre-reflow stage should show its error message, while
        // the post-reflow stage still renders normally.
        expect(
            await screen.findByText(/timeout on pre-reflow/i),
        ).toBeInTheDocument();
        expect(
            screen.getByTestId("traceability-board-stage-postreflow"),
        ).toBeInTheDocument();
    });

    it("renders a 'not seen on this stage' hint when a stage has no panel and no error", async () => {
        stubFetch([
            savedViewsEmpty,
            {
                match: (u) => u.includes("/api/traceability/boards/by-barcode"),
                status: 200,
                body: boardTrace("BOARD-3", { postFound: true, preFound: false }),
            },
        ]);
        renderBoard("/traceability/board?barcode=BOARD-3");
        // "Not seen on this stage" is the pre-reflow stage's badge
        // AND its inline text — findAll to accept either.
        const notSeen = await screen.findAllByText(/not seen on this stage/i);
        expect(notSeen.length).toBeGreaterThan(0);
    });

    it("navigates to the same route with a new barcode when the form is submitted", async () => {
        stubFetch([
            savedViewsEmpty,
            {
                match: (u) => u.includes("/api/traceability/boards/by-barcode"),
                status: 200,
                body: boardTrace("SEED", { postFound: true, preFound: true }),
            },
        ]);
        const { router } = renderBoard("/traceability/board?barcode=SEED");
        const input = await screen.findByTestId("traceability-board-input");
        fireEvent.change(input, { target: { value: "NEW-BARCODE" } });
        fireEvent.click(screen.getByTestId("traceability-board-submit"));

        await waitFor(() => {
            expect(router.state.location.search).toMatchObject({
                barcode: "NEW-BARCODE",
            });
        });
    });

    it("syncs the input value when the barcode search param changes via navigation", async () => {
        stubFetch([
            savedViewsEmpty,
            {
                match: (u) => u.includes("/api/traceability/boards/by-barcode"),
                status: 200,
                body: boardTrace("SEED", { postFound: true, preFound: true }),
            },
        ]);
        const { router } = renderBoard("/traceability/board?barcode=SEED");
        const input = await screen.findByTestId("traceability-board-input");
        expect(input).toHaveValue("SEED");

        await act(async () => {
            await router.navigate({
                to: "/traceability/board",
                search: { barcode: "NEW-BARCODE" },
            });
        });

        await waitFor(() => {
            expect(input).toHaveValue("NEW-BARCODE");
        });
    });

    it("shows the barcode-required validation error on empty submit", async () => {
        stubFetch([savedViewsEmpty]);
        renderBoard("/traceability/board");
        fireEvent.click(await screen.findByTestId("traceability-board-submit"));
        expect(await screen.findByText(/please enter a barcode/i)).toBeInTheDocument();
    });
});

/**
 * TC5 Phase D — drill-down behaviour (`View failures` button /
 * subpanel row click opens an inline drill-down that renders the
 * board viewer plus one failed-objects table per stage).
 */
describe("TraceabilityBoardRoute — drill-down", () => {
    beforeEach(() => {
        void i18n.changeLanguage("en");
        useSessionStore.setState({
            user: {
                email: "tester@example.com",
                displayName: "Tester",
                roles: ["Reader"],
                mustRotatePassword: false,
            },
            token: "fake-token",
        });
    });
    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
        useSessionStore.setState({ user: null, token: null });
    });

    /** Build a fake board response whose panels both have failing objects. */
    function boardTraceWithFailures(barcode: string) {
        // Reuse the same shape as `boardTrace(postFound=true, preFound=true)`
        // but set `anomalyBr` bit 5 ("One or more defects") and bump
        // `nbOfErrorObject` so the drilldown button appears.
        const trace = boardTrace(barcode, { postFound: true, preFound: true });
        trace.stages.forEach((s) => {
            for (const side of s.sides) {
                side.panel.panel.anomalyBr = 32;
                side.panel.panel.nbOfErrorObject = 2;
            }
        });
        return trace;
    }

    /** One failed tested-object row. */
    function fakeObject(over: Partial<{
        cardIdOnPanel: number;
        objectId: number;
        topology: string | null;
        errorTableAr: number;
    }> = {}) {
        return {
            panelId: 1001,
            cardIdOnPanel: over.cardIdOnPanel ?? 0,
            objectId: over.objectId ?? 1,
            objectTypeId: 1,
            errorTable: over.errorTableAr ?? 1,
            errorTableAr: over.errorTableAr ?? 1,
            status: 1,
            machineId: 1,
            productId: 42,
            panelNumericDate: 1_780_660_800,
            topology: over.topology === undefined ? "R1" : over.topology,
            partNumberName: "RES",
            jedecName: "0402",
            deltaXUm: 0,
            deltaYUm: 0,
            deltaThetaDeg: 0,
            deltaThicknessUm: 0,
            deltaSurface: 0,
            face: "Top",
            faceNumber: 1,
            feederName: "F1",
            repairState: 0,
            repairUtc: 0,
            repairButtonComment: null,
            repairErrorComment: null,
            repairOperatorComment: null,
            repairOperatorId: null,
        };
    }

    /** Common stubbed responses for the drill-down open path. */
    function drilldownFetches(barcode: string) {
        return [
            savedViewsEmpty,
            {
                match: (u: string) => u.includes("/api/traceability/boards/by-barcode"),
                status: 200,
                body: boardTraceWithFailures(barcode),
            },
            {
                match: (u: string) =>
                    u.includes("/api/sources/postreflow/products"),
                status: 200,
                body: [{ id: 42, name: "BOARD_A", revision: null }],
            },
            {
                match: (u: string) =>
                    u.includes("/api/sources/prereflow/products"),
                status: 200,
                body: [{ id: 42, name: "BOARD_A", revision: null }],
            },
            {
                match: (u: string) =>
                    u.includes("/api/sources/postreflow/operators"),
                status: 200,
                body: [{ id: 7, name: "Alice Anderson" }],
            },
            {
                match: (u: string) =>
                    u.includes("/api/sources/prereflow/operators"),
                status: 200,
                body: [{ id: 7, name: "Alice Anderson" }],
            },
            {
                match: (u: string) =>
                    u.includes("/api/traceability/panels/postreflow/1001/failed-objects"),
                status: 200,
                body: {
                    panel: boardTraceWithFailures(barcode).stages[0].sides[0].panel,
                    objects: [
                        fakeObject({ cardIdOnPanel: 0, objectId: 1, topology: "R1", errorTableAr: 1 }),
                        fakeObject({ cardIdOnPanel: 1, objectId: 2, topology: "U5", errorTableAr: 3 }),
                    ],
                },
            },
            {
                match: (u: string) =>
                    u.includes("/api/traceability/panels/prereflow/2001/failed-objects"),
                status: 200,
                body: {
                    panel: boardTraceWithFailures(barcode).stages[1].sides[0].panel,
                    objects: [
                        fakeObject({ cardIdOnPanel: 0, objectId: 3, topology: "C7", errorTableAr: 4 }),
                    ],
                },
            },
            // BoardViewer requests the SVG — return 404 so the viewer
            // shows its "not cached yet" banner and the tables still
            // render around it.
            {
                match: (u: string) => u.includes("/api/board-svgs/"),
                status: 404,
                body: "not cached",
            },
        ];
    }

    it("does not show the drill-down button when the panel has zero failures", async () => {
        // Start from the shared helper but leave `anomalyBr` at its
        // default of 0 (bit 5 "One or more defects" not set). This
        // is the true "clean panel" signal in the CR4 schema —
        // `nbOfErrorObject` alone is not reliable because it is
        // zeroed after a false-call review.
        const cleanTrace = boardTrace("CLEAN", { postFound: true, preFound: true });
        cleanTrace.stages.forEach((s) => {
            for (const side of s.sides) {
                side.panel.panel.anomalyBr = 0;
                side.panel.panel.nbOfErrorObject = 0;
            }
        });
        stubFetch([
            savedViewsEmpty,
            {
                match: (u: string) => u.includes("/api/traceability/boards/by-barcode"),
                status: 200,
                body: cleanTrace,
            },
        ]);
        renderBoard("/traceability/board?barcode=CLEAN");
        await screen.findByTestId("traceability-board-stage-postreflow");
        // No drill-down open button on a clean board.
        expect(
            screen.queryByTestId("traceability-board-open-drilldown-postreflow"),
        ).not.toBeInTheDocument();
    });

    it("still shows the drill-down on a false-call panel (nbOfErrorObject=0, anomalyBr bit 5 set)", async () => {
        // A panel that was flagged during AOI (anomalyBr bit 5 set)
        // but whose defects were all sanctioned as false calls
        // during review — the review clears `nbOfErrorObject` to 0
        // but never touches `anomalyBr`. Operators still need to be
        // able to open the drill-down to inspect the false-call
        // history, so the button must remain visible.
        const falseCallTrace = boardTrace("FALSE-CALL", {
            postFound: true,
            preFound: true,
        });
        falseCallTrace.stages.forEach((s) => {
            for (const side of s.sides) {
                side.panel.panel.anomalyBr = 32;
                side.panel.panel.nbOfErrorObject = 0;
            }
        });
        stubFetch([
            savedViewsEmpty,
            {
                match: (u: string) => u.includes("/api/traceability/boards/by-barcode"),
                status: 200,
                body: falseCallTrace,
            },
        ]);
        renderBoard("/traceability/board?barcode=FALSE-CALL");
        expect(
            await screen.findByTestId(
                "traceability-board-open-drilldown-postreflow",
            ),
        ).toBeInTheDocument();
    });

    it("opens the drill-down section when the View failures button is clicked", async () => {
        stubFetch(drilldownFetches("BOARD-D1"));
        renderBoard("/traceability/board?barcode=BOARD-D1");
        const openBtn = await screen.findByTestId(
            "traceability-board-open-drilldown-postreflow",
        );
        fireEvent.click(openBtn);

        // Drilldown card + one per-stage failed-objects table.
        expect(
            await screen.findByTestId("traceability-board-drilldown"),
        ).toBeInTheDocument();
        expect(
            await screen.findByTestId(
                "traceability-board-drilldown-table-postreflow",
            ),
        ).toBeInTheDocument();
        expect(
            screen.getByTestId("traceability-board-drilldown-table-prereflow"),
        ).toBeInTheDocument();
    });

    it("closes the drill-down when the Close button is clicked", async () => {
        stubFetch(drilldownFetches("BOARD-D2"));
        renderBoard("/traceability/board?barcode=BOARD-D2");
        fireEvent.click(
            await screen.findByTestId(
                "traceability-board-open-drilldown-postreflow",
            ),
        );
        await screen.findByTestId("traceability-board-drilldown");
        fireEvent.click(screen.getByTestId("traceability-board-drilldown-close"));
        await waitFor(() => {
            expect(
                screen.queryByTestId("traceability-board-drilldown"),
            ).not.toBeInTheDocument();
        });
    });

    it("opens the drill-down when a subpanel row is clicked", async () => {
        stubFetch(drilldownFetches("BOARD-D3"));
        renderBoard("/traceability/board?barcode=BOARD-D3");
        // Row on the post-reflow stage (subpanel 0).
        const row = await screen.findByTestId(
            "traceability-board-cards-postreflow-row-0",
        );
        fireEvent.click(row);
        expect(
            await screen.findByTestId("traceability-board-drilldown"),
        ).toBeInTheDocument();
    });

    it("promotes the pre-reflow stage to active when a pre-reflow row is clicked", async () => {
        stubFetch(drilldownFetches("BOARD-D4"));
        renderBoard("/traceability/board?barcode=BOARD-D4");
        fireEvent.click(
            await screen.findByTestId(
                "traceability-board-open-drilldown-postreflow",
            ),
        );
        // Wait for both per-stage tables to be present after fetches.
        const preRow = await screen.findByTestId(
            "traceability-board-failed-prereflow-row-0-3",
        );
        fireEvent.click(preRow);
        // Active stage flipped → pre-reflow card marked data-active.
        await waitFor(() => {
            expect(
                screen.getByTestId("traceability-board-drilldown-table-prereflow"),
            ).toHaveAttribute("data-active", "true");
        });
    });

    it("selects the primary highlight when a row is clicked", async () => {
        stubFetch(drilldownFetches("BOARD-D5"));
        renderBoard("/traceability/board?barcode=BOARD-D5");
        fireEvent.click(
            await screen.findByTestId(
                "traceability-board-open-drilldown-postreflow",
            ),
        );
        const postRow = await screen.findByTestId(
            "traceability-board-failed-postreflow-row-1-2",
        );
        fireEvent.click(postRow);
        await waitFor(() => {
            expect(postRow).toHaveAttribute("data-selected", "true");
        });
    });

    it("shows the passes menu when priorPasses are present and hides it on pre-reflow", async () => {
        const priors = [
            {
                panelId: 1000,
                faceNumber: 1,
                panelUtc: "2026-06-05T11:00:00Z",
                panelStatus: 2,
                anomalyBr: 1,
                anomalyAr: 0,
                nbOfErrorObject: 1,
                hasBeenReviewed: false,
            },
            {
                panelId: 999,
                faceNumber: 1,
                panelUtc: "2026-06-05T10:00:00Z",
                panelStatus: 2,
                anomalyBr: 1,
                anomalyAr: 0,
                nbOfErrorObject: 1,
                hasBeenReviewed: false,
            },
        ];
        stubFetch([
            savedViewsEmpty,
            {
                match: (u) => u.includes("/api/traceability/boards/by-barcode"),
                status: 200,
                body: boardTrace("REPEAT-001", {
                    postFound: true,
                    preFound: true,
                    postPriors: priors,
                }),
            },
        ]);
        renderBoard("/traceability/board?barcode=REPEAT-001");

        expect(
            await screen.findByTestId("traceability-board-passes-postreflow"),
        ).toBeInTheDocument();
        expect(
            screen.queryByTestId("traceability-board-passes-prereflow"),
        ).not.toBeInTheDocument();
    });

    it("clicking a prior pass updates the URL and re-fetches with the pin", async () => {
        const priors = [
            {
                panelId: 1000,
                faceNumber: 1,
                panelUtc: "2026-06-05T11:00:00Z",
                panelStatus: 2,
                anomalyBr: 1,
                anomalyAr: 0,
                nbOfErrorObject: 1,
                hasBeenReviewed: false,
            },
        ];
        const fetchMock = stubFetch([
            savedViewsEmpty,
            {
                match: (u) =>
                    u.includes("/api/traceability/boards/by-barcode") &&
                    !u.includes("panelId="),
                status: 200,
                body: boardTrace("REPEAT-001", {
                    postFound: true,
                    preFound: true,
                    postPriors: priors,
                }),
            },
            {
                match: (u) =>
                    u.includes("/api/traceability/boards/by-barcode") &&
                    u.includes("panelId=postreflow%3A1000"),
                status: 200,
                body: boardTrace("REPEAT-001", {
                    postFound: true,
                    preFound: true,
                    postPanelId: 1000,
                    postPinned: 1000,
                    postPriors: [
                        {
                            panelId: 1001,
                            faceNumber: 1,
                            panelUtc: "2026-06-05T12:00:00Z",
                            panelStatus: 1,
                            anomalyBr: 0,
                            anomalyAr: 0,
                            nbOfErrorObject: 0,
                            hasBeenReviewed: true,
                        },
                    ],
                }),
            },
        ]);

        const { router } = renderBoard("/traceability/board?barcode=REPEAT-001");
        const trigger = await screen.findByTestId(
            "traceability-board-passes-postreflow",
        );
        fireEvent.click(trigger);
        fireEvent.click(
            await screen.findByTestId("traceability-board-pass-postreflow-1000"),
        );

        await waitFor(() => {
            expect(router.state.location.search).toMatchObject({
                barcode: "REPEAT-001",
                passes: { postreflow: 1000 },
            });
        });
        await waitFor(() => {
            expect(
                fetchMock.mock.calls.some(
                    (c) =>
                        String(c[0]).includes("panelId=postreflow%3A1000") ||
                        String(c[0]).includes("panelId=postreflow:1000"),
                ),
            ).toBe(true);
        });
        expect(
            await screen.findByTestId("traceability-board-pass-pill-postreflow"),
        ).toBeInTheDocument();
    });

    it("shows a soft selection-warning alert without the stage-error badge", async () => {
        stubFetch([
            savedViewsEmpty,
            {
                match: (u) => u.includes("/api/traceability/boards/by-barcode"),
                status: 200,
                body: boardTrace("REPEAT-001", {
                    postFound: true,
                    preFound: true,
                    postSelectionWarning:
                        "This pass link is older than the retained 10-pass window.",
                }),
            },
        ]);
        renderBoard(
            "/traceability/board?barcode=REPEAT-001&passes[postreflow]=99999",
        );

        expect(
            await screen.findByTestId(
                "traceability-board-selection-warning-postreflow",
            ),
        ).toBeInTheDocument();
        expect(
            screen.queryByText(/this stage could not be queried/i),
        ).not.toBeInTheDocument();
    });
});

