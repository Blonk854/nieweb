import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Mock } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { userEvent } from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import i18n from "../../i18n";
import { BoardViewer } from "./BoardViewer";

/**
 * Component-level tests for the shared &lt;BoardViewer&gt; primitive
 * (docs/phase-2.md §7.5 TC5 Phase A). Coverage:
 * <ul>
 *   <li>Empty product-name renders a placeholder without firing HTTP.</li>
 *   <li>200 SVG is injected + a highlights overlay is appended above
 *       the source #components layer, one &lt;circle&gt; per matched
 *       highlight, with stage-appropriate stroke &amp; dash.</li>
 *   <li>Post-reflow highlights are red solid; pre-reflow purple dashed.</li>
 *   <li>Primary highlight gets a thicker stroke and drop-shadow filter.</li>
 *   <li>Clicking a marker (with onPrimaryChange bound) calls back with
 *       the (subpanel, reference) pair; clicking the primary marker
 *       again clears it.</li>
 *   <li>404 renders the "not cached" banner with a Retry button; Retry
 *       fires another GET to the same URL.</li>
 *   <li>Stage switcher fires onStageChange with the new value.</li>
 * </ul>
 */

/** Minimal fake panel SVG with a couple of components at known coords. */
const SAMPLE_SVG = `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="100%" height="100%" viewBox="0 0 213360 124460">
  <g id="components">
    <g class="component tested" sub-panel-index="1" reference="U1" transform="rotate(270 28435 97498)">
      <rect x="27000" y="96000" width="3000" height="3000" />
    </g>
    <g class="component tested" sub-panel-index="1" reference="R1" transform="rotate(0 50000 50000)">
      <rect x="49500" y="49500" width="1000" height="1000" />
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
            screen.getByText(/Select a product to view its board layout\./i),
        ).toBeInTheDocument();
        expect(fetchMock).not.toHaveBeenCalled();
    });

    it("fetches the SVG and renders overlay circles for the active-stage highlights (post = red solid)", async () => {
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

        // Overlay should have exactly 2 circles corresponding to the two
        // matched highlights. R1 was not requested so no marker for it.
        const overlay = await waitFor(() => {
            const el = document.querySelector(
                "g[data-nieweb-highlights='true']",
            );
            if (!el) throw new Error("overlay not yet appended");
            return el;
        });
        const circles = overlay.querySelectorAll("circle");
        expect(circles.length).toBe(2);
        // Stage=post ⇒ red solid stroke, no dasharray.
        circles.forEach((c) => {
            expect(c.getAttribute("stroke")).toBe("#d32f2f");
            expect(c.getAttribute("stroke-dasharray")).toBeNull();
        });
        // Centroids come from rotate(θ cx cy).
        const first = overlay.querySelector(
            "circle[data-subpanel='1'][data-reference='U1']",
        );
        expect(first?.getAttribute("cx")).toBe("28435");
        expect(first?.getAttribute("cy")).toBe("97498");
        expect(fetchMock).toHaveBeenCalledTimes(1);
    });

    it("uses purple dashed stroke for the pre-reflow stage", async () => {
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
            highlights: [{ subpanelIndex: 1, reference: "R1" }],
        });
        const circle = await waitFor(() => {
            const el = document.querySelector(
                "g[data-nieweb-highlights='true'] circle",
            );
            if (!el) throw new Error("marker not yet appended");
            return el;
        });
        expect(circle.getAttribute("stroke")).toBe("#9c27b0");
        expect(circle.getAttribute("stroke-dasharray")).toBe("6 4");
    });

    it("promotes the primaryHighlight marker (thicker stroke + drop-shadow filter)", async () => {
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
        const primary = overlay.querySelector<SVGCircleElement>(
            "circle[data-subpanel='3'][data-reference='U1']",
        );
        const other = overlay.querySelector<SVGCircleElement>(
            "circle[data-subpanel='1'][data-reference='U1']",
        );
        expect(primary).not.toBeNull();
        expect(other).not.toBeNull();
        expect(primary!.getAttribute("filter")).toContain("drop-shadow");
        expect(other!.getAttribute("filter")).toBeNull();
        // Primary stroke-width should be strictly greater than the
        // non-primary sibling.
        const wPrimary = Number.parseFloat(
            primary!.getAttribute("stroke-width") ?? "0",
        );
        const wOther = Number.parseFloat(
            other!.getAttribute("stroke-width") ?? "0",
        );
        expect(wPrimary).toBeGreaterThan(wOther);
    });

    it("fires onPrimaryChange when a marker is clicked and clears it on re-click", async () => {
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
            const el = document.querySelector<SVGCircleElement>(
                "g[data-nieweb-highlights='true'] circle[data-subpanel='1'][data-reference='U1']",
            );
            if (!el) throw new Error("marker not yet appended");
            return el;
        });
        await user.click(marker);
        expect(onPrimaryChange).toHaveBeenCalledWith({
            subpanelIndex: 1,
            reference: "U1",
        });

        // Now render again with THIS marker set as primary and click again
        // → should clear (null).
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
            const el = document.querySelector<SVGCircleElement>(
                "g[data-nieweb-highlights='true'] circle[data-subpanel='1'][data-reference='U1']",
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
            // First call 404, second call 200 (so Retry succeeds).
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

        await screen.findByText(/Board layout not yet available/i);
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

    it("renders the stage switcher when onStageChange is provided and reports changes", async () => {
        stubFetch([
            {
                match: (u) => u === "/api/board-svgs/BOARDX",
                status: 200,
                body: SAMPLE_SVG,
            },
        ]);
        const onStageChange = vi.fn();
        renderViewer({
            productName: "BOARDX",
            stage: "post",
            highlights: [],
            onStageChange,
        });
        const preOption = await screen.findByText("Pre-reflow");
        const user = userEvent.setup();
        await user.click(preOption);
        expect(onStageChange).toHaveBeenCalledWith("pre");
    });
});
