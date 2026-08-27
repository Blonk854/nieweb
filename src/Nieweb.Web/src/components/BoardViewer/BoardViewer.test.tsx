import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Mock } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BoardViewer } from "./BoardViewer";
import i18n from "../../i18n";

/**
 * Behaviour tests for the redesigned BoardViewer. Verifies:
 * <ul>
 *   <li>Component highlights are cloned red-filled silhouettes,
 *       one per (sub-panel, reference).</li>
 *   <li>The subpanel(s) implicated by any highlight get a pulsing
 *       outline path.</li>
 *   <li>The primary highlight adds a crosshair layer.</li>
 *   <li>Clicking a component highlight toggles the primary via
 *       onPrimaryChange.</li>
 *   <li>Stage badge overlays the panel with the stage label.</li>
 *   <li>Zoom reset control resets the internal zoom state.</li>
 * </ul>
 */

/**
 * Panel SVG with two sub-panels, three tested components, and a
 * viewBox we can pin crosshair math against.
 */
const SAMPLE_SVG = `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="100%" height="100%" viewBox="0 0 213360 124460">
  <g id="sub-panels">
    <g class="sub-panel" index="1">
      <path class="border" d="M0 90000 L60000 90000 L60000 120000 L0 120000 Z"/>
    </g>
    <g class="sub-panel" index="3">
      <path class="border" d="M70000 30000 L120000 30000 L120000 60000 L70000 60000 Z"/>
    </g>
  </g>
  <g id="components">
    <g class="component tested" sub-panel-index="1" reference="U1" transform="rotate(270 28435 97498)">
      <rect x="27000" y="96000" width="3000" height="3000" />
    </g>
    <g class="component tested" sub-panel-index="1" reference="R1" transform="rotate(0 50000 100000)">
      <rect x="49500" y="99500" width="1000" height="1000" />
    </g>
    <g class="component tested" sub-panel-index="3" reference="U1" transform="rotate(270 78473 47460)">
      <rect x="77000" y="46000" width="3000" height="3000" />
    </g>
  </g>
</svg>`;

type FetchResponse = {
    match: (url: string) => boolean;
    status: number;
    body?: string;
    contentType?: string;
};

function stubFetch(responses: FetchResponse[]): Mock {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
        const url =
            typeof input === "string"
                ? input
                : input instanceof URL
                    ? input.toString()
                    : input.url;
        const hit = responses.find((r) => r.match(url));
        if (!hit) {
            throw new Error(`Unexpected fetch: ${url}`);
        }
        return new Response(hit.body ?? "", {
            status: hit.status,
            statusText:
                hit.status === 200
                    ? "OK"
                    : hit.status === 404
                        ? "Not Found"
                        : hit.status === 400
                            ? "Bad Request"
                            : "Error",
            headers: { "Content-Type": hit.contentType ?? "image/svg+xml" },
        });
    });
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock as Mock;
}

type ViewerHarnessProps = React.ComponentProps<typeof BoardViewer>;

function renderViewer(props: ViewerHarnessProps) {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    return render(
        <MantineProvider>
            <QueryClientProvider client={client}>
                <BoardViewer {...props} />
            </QueryClientProvider>
        </MantineProvider>,
    );
}

beforeEach(async () => {
    await i18n.changeLanguage("en");
});

afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
});

describe("BoardViewer", () => {
    it("renders a placeholder when productName is blank without firing HTTP", () => {
        const fetchMock = stubFetch([]);
        renderViewer({
            productName: "   ",
            stage: "post",
            highlights: [],
        });
        expect(
            screen.getByText(/Select a product to view its panel layout\./i),
        ).toBeInTheDocument();
        expect(fetchMock).not.toHaveBeenCalled();
    });

    it("fetches the SVG and clones one red-filled component highlight per (sub-panel, reference)", async () => {
        const fetchMock = stubFetch([
            {
                match: (u) => u === "/api/board-svgs/HA010522401_1st",
                status: 200,
                body: SAMPLE_SVG,
            },
        ]);
        renderViewer({
            productName: "HA010522401_1st",
            stage: "post",
            highlights: [
                { subpanelIndex: 1, reference: "U1" },
                { subpanelIndex: 3, reference: "U1" },
            ],
        });

        // Overlay layer with two component clones — R1 was not
        // requested so no clone for it.
        const overlay = await waitFor(() => {
            const el = document.querySelector(
                "g[data-nieweb-highlights='true']",
            );
            if (!el) throw new Error("overlay not yet appended");
            return el;
        });
        const clones = overlay.querySelectorAll<SVGGElement>(
            "g.nieweb-component-highlight",
        );
        expect(clones.length).toBe(2);
        // Both stages use red — no dashed/purple variant anymore.
        // The red fill is applied via the scoped stylesheet.
        expect(
            overlay.querySelector(
                "g.nieweb-component-highlight[data-subpanel='1'][data-reference='U1']",
            ),
        ).not.toBeNull();
        expect(
            overlay.querySelector(
                "g.nieweb-component-highlight[data-subpanel='3'][data-reference='U1']",
            ),
        ).not.toBeNull();
        expect(fetchMock).toHaveBeenCalledTimes(1);
    });

    it("draws a pulsing outline path for every unique sub-panel that has a highlight", async () => {
        stubFetch([
            {
                match: (u) => u === "/api/board-svgs/BOARDX",
                status: 200,
                body: SAMPLE_SVG,
            },
        ]);
        renderViewer({
            productName: "BOARDX",
            stage: "post",
            highlights: [
                { subpanelIndex: 1, reference: "U1" },
                { subpanelIndex: 1, reference: "R1" },
                { subpanelIndex: 3, reference: "U1" },
            ],
        });
        const outlines = await waitFor(() => {
            const els = document.querySelectorAll<SVGPathElement>(
                "g[data-nieweb-highlights='true'] path.nieweb-subpanel-outline",
            );
            if (els.length === 0) {
                throw new Error("no subpanel outlines yet");
            }
            return els;
        });
        // Two unique sub-panel indices → two outlines, even though
        // the highlights list has three entries.
        expect(outlines.length).toBe(2);
        const indices = Array.from(outlines).map(
            (o) => o.getAttribute("data-subpanel"),
        );
        expect(indices.sort()).toEqual(["1", "3"]);
    });

    it("marks the primary highlight on both the component clone and its sub-panel outline", async () => {
        stubFetch([
            {
                match: (u) => u === "/api/board-svgs/BOARDX",
                status: 200,
                body: SAMPLE_SVG,
            },
        ]);
        renderViewer({
            productName: "BOARDX",
            stage: "post",
            highlights: [
                { subpanelIndex: 1, reference: "U1" },
                { subpanelIndex: 3, reference: "U1" },
            ],
            primaryHighlight: { subpanelIndex: 3, reference: "U1" },
        });
        const overlay = await waitFor(() => {
            const el = document.querySelector(
                "g[data-nieweb-highlights='true']",
            );
            if (!el) throw new Error("overlay not yet appended");
            return el;
        });
        const primary = overlay.querySelector(
            "g.nieweb-component-highlight[data-subpanel='3'][data-reference='U1']",
        );
        const other = overlay.querySelector(
            "g.nieweb-component-highlight[data-subpanel='1'][data-reference='U1']",
        );
        expect(primary?.getAttribute("data-primary")).toBe("true");
        expect(other?.getAttribute("data-primary")).toBeNull();

        // Only the sub-panel outline hosting the primary component
        // gets data-primary="true" — that's the one the CSS pulse
        // is scoped to. The other failed sub-panel stays static.
        const primaryOutline = overlay.querySelector(
            "path.nieweb-subpanel-outline[data-subpanel='3']",
        );
        const otherOutline = overlay.querySelector(
            "path.nieweb-subpanel-outline[data-subpanel='1']",
        );
        expect(primaryOutline?.getAttribute("data-primary")).toBe("true");
        expect(otherOutline?.getAttribute("data-primary")).toBeNull();

        // Crosshair is opt-in and defaults to OFF.
        expect(overlay.querySelector("g.nieweb-crosshair")).toBeNull();
    });

    it("adds a crosshair spanning the viewBox once the header switch is enabled", async () => {
        stubFetch([
            {
                match: (u) => u === "/api/board-svgs/BOARDX",
                status: 200,
                body: SAMPLE_SVG,
            },
        ]);
        renderViewer({
            productName: "BOARDX",
            stage: "post",
            highlights: [
                { subpanelIndex: 1, reference: "U1" },
                { subpanelIndex: 3, reference: "U1" },
            ],
            primaryHighlight: { subpanelIndex: 3, reference: "U1" },
        });
        await waitFor(() => {
            const el = document.querySelector(
                "g[data-nieweb-highlights='true']",
            );
            if (!el) throw new Error("overlay not yet appended");
        });

        // Flip the toggle on. Mantine's Switch renders a real
        // checkbox behind the label; click the label to toggle.
        const user = userEvent.setup();
        await user.click(screen.getByLabelText(/Crosshair/i));

        const crosshair = await waitFor(() => {
            const el = document.querySelector(
                "g[data-nieweb-highlights='true'] g.nieweb-crosshair",
            );
            if (!el) throw new Error("crosshair not yet appended");
            return el;
        });
        const lines = crosshair.querySelectorAll("line");
        expect(lines.length).toBe(2);
        const hLine = Array.from(lines).find(
            (l) => l.getAttribute("y1") === l.getAttribute("y2"),
        );
        const vLine = Array.from(lines).find(
            (l) => l.getAttribute("x1") === l.getAttribute("x2"),
        );
        // Primary component centroid (from SAMPLE_SVG: rotate(270 78473 47460)).
        expect(hLine?.getAttribute("y1")).toBe("47460");
        expect(vLine?.getAttribute("x1")).toBe("78473");
        // Dasharray is sized from the viewBox (not a fixed CSS px
        // value) so the lines stay visible at full-panel zoom.
        // SAMPLE_SVG viewBox max span = 213360 → 2% = 4267.
        expect(hLine?.getAttribute("stroke-dasharray")).toBe("4267 2987");
        expect(hLine?.getAttribute("stroke")).toBe("#ff3b30");
        expect(hLine?.getAttribute("stroke-width")).toBe("2");
    });

    it("renders a yellow paint splat at the reported micron coord for Foreign Material rows", async () => {
        stubFetch([
            {
                match: (u) => u === "/api/board-svgs/BOARDX",
                status: 200,
                body: SAMPLE_SVG,
            },
        ]);
        renderViewer({
            productName: "BOARDX",
            stage: "post",
            highlights: [
                {
                    subpanelIndex: 1,
                    reference: "FM1",
                    objectTypeId: 33554432,
                    xUm: 12345,
                    yUm: 98765,
                },
            ],
        });
        const splat = await waitFor(() => {
            const el = document.querySelector<SVGGElement>(
                "g[data-nieweb-highlights='true'] g.nieweb-fm-splat",
            );
            if (!el) throw new Error("splat not yet appended");
            return el;
        });
        // Positioned via translate(x y) so we can round-trip the
        // AOI-reported micron coord. The AOI machine reports
        // Delta_X / Delta_Y with origin at LOWER_LEFT (Y-up); the
        // panel SVG uses UPPER_LEFT (Y-down). BoardViewer mirrors Y
        // using viewBox height: for SAMPLE_SVG (viewBox 0 0 213360
        // 124460) and yUm=98765 the flipped SVG y = 124460 - 98765
        // = 25695.
        expect(splat.getAttribute("transform")).toBe("translate(12345 25695)");
        // The FM splat replaces the normal component clone — there
        // must be no red-fill clone for the same key.
        expect(
            document.querySelector(
                "g[data-nieweb-highlights='true'] g.nieweb-component-highlight[data-reference='FM1']",
            ),
        ).toBeNull();
    });

    it("fires onPrimaryChange when a component highlight is clicked, and clears it on re-click", async () => {
        stubFetch([
            {
                match: (u) => u === "/api/board-svgs/BOARDX",
                status: 200,
                body: SAMPLE_SVG,
            },
        ]);
        const onPrimaryChange = vi.fn();
        const user = userEvent.setup();
        const { rerender } = renderViewer({
            productName: "BOARDX",
            stage: "post",
            highlights: [{ subpanelIndex: 1, reference: "U1" }],
            primaryHighlight: null,
            onPrimaryChange,
        });
        const marker = await waitFor(() => {
            const el = document.querySelector<SVGGElement>(
                "g[data-nieweb-highlights='true'] g.nieweb-component-highlight[data-subpanel='1'][data-reference='U1']",
            );
            if (!el) throw new Error("marker not yet appended");
            return el;
        });
        await user.click(marker);
        expect(onPrimaryChange).toHaveBeenCalledWith({
            subpanelIndex: 1,
            reference: "U1",
        });

        // Re-render with this marker set as primary → clicking again
        // should clear it.
        const client = new QueryClient({
            defaultOptions: { queries: { retry: false } },
        });
        rerender(
            <MantineProvider>
                <QueryClientProvider client={client}>
                    <BoardViewer
                        productName="BOARDX"
                        stage="post"
                        highlights={[{ subpanelIndex: 1, reference: "U1" }]}
                        primaryHighlight={{ subpanelIndex: 1, reference: "U1" }}
                        onPrimaryChange={onPrimaryChange}
                    />
                </QueryClientProvider>
            </MantineProvider>,
        );
        const marker2 = await waitFor(() => {
            const el = document.querySelector<SVGGElement>(
                "g[data-nieweb-highlights='true'] g.nieweb-component-highlight[data-subpanel='1'][data-reference='U1']",
            );
            if (!el) throw new Error("marker not yet re-appended");
            return el;
        });
        await user.click(marker2);
        expect(onPrimaryChange).toHaveBeenLastCalledWith(null);
    });

    it("shows the 'not cached' banner + Retry button on 404 and retries on click", async () => {
        let callCount = 0;
        const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
            callCount += 1;
            const url =
                typeof input === "string"
                    ? input
                    : input instanceof URL
                        ? input.toString()
                        : input.url;
            if (url !== "/api/board-svgs/MISSING") {
                throw new Error(`Unexpected fetch: ${url}`);
            }
            if (callCount === 1) {
                return new Response("not cached", {
                    status: 404,
                    statusText: "Not Found",
                });
            }
            return new Response(SAMPLE_SVG, {
                status: 200,
                statusText: "OK",
                headers: { "Content-Type": "image/svg+xml" },
            });
        });
        vi.stubGlobal("fetch", fetchMock);

        renderViewer({
            productName: "MISSING",
            stage: "post",
            highlights: [{ subpanelIndex: 1, reference: "U1" }],
        });

        await screen.findByText(/Panel layout not yet available/i);
        const retry = screen.getByRole("button", { name: /Retry/i });
        const user = userEvent.setup();
        await user.click(retry);

        await waitFor(() => {
            const overlay = document.querySelector(
                "g[data-nieweb-highlights='true']",
            );
            if (!overlay) throw new Error("still no overlay");
        });
        expect(fetchMock).toHaveBeenCalledTimes(2);
    });

    it("renders a stage badge above the panel", async () => {
        stubFetch([
            {
                match: (u) => u === "/api/board-svgs/BOARDX",
                status: 200,
                body: SAMPLE_SVG,
            },
        ]);
        renderViewer({
            productName: "BOARDX",
            stage: "post",
            stageLabel: "Post-reflow AOI (HLYAOI2024)",
            highlights: [{ subpanelIndex: 1, reference: "U1" }],
        });
        const badge = await screen.findByTestId("board-viewer-stage-badge");
        expect(badge).toHaveTextContent(/Post-reflow AOI \(HLYAOI2024\)/i);
    });

    it("falls back to the i18n stage name when stageLabel is not provided", async () => {
        stubFetch([
            {
                match: (u) => u === "/api/board-svgs/BOARDX",
                status: 200,
                body: SAMPLE_SVG,
            },
        ]);
        renderViewer({
            productName: "BOARDX",
            stage: "pre",
            highlights: [{ subpanelIndex: 1, reference: "U1" }],
        });
        const badge = await screen.findByTestId("board-viewer-stage-badge");
        expect(badge).toHaveTextContent(/Pre-reflow/i);
    });

    it("exposes a zoom reset control", async () => {
        stubFetch([
            {
                match: (u) => u === "/api/board-svgs/BOARDX",
                status: 200,
                body: SAMPLE_SVG,
            },
        ]);
        renderViewer({
            productName: "BOARDX",
            stage: "post",
            highlights: [{ subpanelIndex: 1, reference: "U1" }],
        });
        const reset = await screen.findByTestId("board-viewer-zoom-reset");
        expect(reset).toBeInTheDocument();
        const level = screen.getByTestId("board-viewer-zoom-level");
        expect(level).toHaveTextContent("100%");
    });
});
