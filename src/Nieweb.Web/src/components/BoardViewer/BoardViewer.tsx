import { useEffect, useMemo, useRef, useState } from "react";
import {
    Alert,
    Box,
    Button,
    Card,
    Group,
    Loader,
    SegmentedControl,
    Stack,
    Text,
} from "@mantine/core";
import {
    IconAlertTriangle,
    IconPhoto,
    IconRefresh,
} from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";

import { fetchBoardSvg } from "../../api/boardSvgs";
import { ApiError } from "../../api/client";
import {
    computeHighlightGeometry,
    parseComponentCentroids,
    type ComponentCentroid,
    type HighlightGeometry,
} from "./svgParsing";

/**
 * Shared board-viewer primitive (docs/phase-2.md §7.5 TC5 Phase A).
 * Renders the cached product SVG from
 * `GET /api/board-svgs/{productName}` (TC4 Phase C) and overlays
 * per-stage circle markers on the components identified by
 * `highlights`.
 *
 * <h3>Design</h3>
 * <ul>
 *   <li>The source SVG is injected via
 *       <code>dangerouslySetInnerHTML</code>. The payload is served
 *       by our own API from an admin-configured cache — the endpoint
 *       already refuses <code>..</code> and invalid filename chars,
 *       and the source directory is machine-controlled, so the SVG
 *       is treated as first-party content. We do NOT run
 *       server-side sanitisation because that would break the
 *       Sigmalink-generated <code>&lt;style&gt;</code> block with
 *       CSS variables and <code>vector-effect</code> attributes
 *       both AOI systems rely on.</li>
 *   <li>Component centroids are parsed from the
 *       <code>transform="rotate(θ cx cy)"</code> attribute on
 *       <code>&lt;g class="component" sub-panel-index="…"
 *       reference="…"&gt;</code> nodes. Parsing runs once per SVG
 *       body via <code>useMemo</code> and produces a
 *       <code>Map&lt;"index:ref", ComponentCentroid&gt;</code> for
 *       O(1) highlight lookup.</li>
 *   <li>Highlight geometry (per-marker radius) is derived from
 *       <code>SVGGraphicsElement.getBBox()</code> at render time so
 *       0402s stay tiny and QFPs get proportional circles. The
 *       overlay layer is a fresh <code>&lt;g class="highlights"&gt;</code>
 *       appended above <code>#components</code> — never mutating
 *       the source SVG so the cache file remains interchangeable.</li>
 *   <li>Only ONE stage is active at a time (default post-reflow).
 *       Post-reflow uses solid red <code>#d32f2f</code>; pre-reflow
 *       uses dashed purple <code>#9c27b0</code>. Both colour and
 *       stroke pattern act as redundant accessibility channels.</li>
 *   <li>The <code>primary</code> highlight (matched by
 *       <code>sub-panel-index + reference</code>) gets a thicker
 *       stroke and drop-shadow glow. Clicking any marker calls
 *       <code>onPrimaryChange</code> so tables can two-way bind.</li>
 *   <li>404 from the API (SVG not yet cached) degrades to a
 *       localised banner with a manual "Retry" button — the caller
 *       is expected to still render its own data tables.</li>
 * </ul>
 */
export type BoardViewerStage = "pre" | "post";

/** Identifies one component on the board via (subpanel, reference). */
export type BoardHighlight = {
    subpanelIndex: number;
    reference: string;
};

export type BoardViewerProps = {
    /**
     * Product name (matches the ProductName in the AOI DB and the
     * base filename in the SVG cache). Whitespace-trimmed by the
     * caller — an empty/whitespace value renders a placeholder.
     */
    productName: string;
    /** Which stage's failures are currently on the board. */
    stage: BoardViewerStage;
    /** Optional stage switcher — when omitted the SegmentedControl is hidden. */
    onStageChange?: (stage: BoardViewerStage) => void;
    /** Failing components in the active stage. */
    highlights: readonly BoardHighlight[];
    /** Optional focused highlight for row ↔ marker binding. */
    primaryHighlight?: BoardHighlight | null;
    /** Callback when the user clicks a marker on the board. */
    onPrimaryChange?: (h: BoardHighlight | null) => void;
    /** Optional pixel height override; the SVG scales to fill width. */
    height?: number;
};

const STAGE_STYLE: Record<
    BoardViewerStage,
    { stroke: string; dash: string | undefined; glow: string }
> = {
    post: { stroke: "#d32f2f", dash: undefined, glow: "rgba(211,47,47,0.55)" },
    pre: { stroke: "#9c27b0", dash: "6 4", glow: "rgba(156,39,176,0.55)" },
};

const SVG_QUERY_KEY = ["board-viewer", "svg"] as const;

function samePoint(
    a: BoardHighlight | null | undefined,
    b: BoardHighlight | null | undefined,
): boolean {
    if (!a || !b) return false;
    return a.subpanelIndex === b.subpanelIndex && a.reference === b.reference;
}

export function BoardViewer(props: BoardViewerProps) {
    const { t } = useTranslation();
    const {
        productName,
        stage,
        onStageChange,
        highlights,
        primaryHighlight,
        onPrimaryChange,
        height,
    } = props;

    const trimmedName = productName.trim();

    const svgQuery = useQuery({
        queryKey: [...SVG_QUERY_KEY, trimmedName],
        queryFn: ({ signal }) => fetchBoardSvg(trimmedName, { signal }),
        enabled: trimmedName.length > 0,
        retry: false,
        refetchOnWindowFocus: false,
        staleTime: 5 * 60 * 1000, // matches server Cache-Control max-age
    });

    const svgText = svgQuery.data?.svg ?? "";

    // Centroids are parsed once per SVG body — cheap for a few thousand
    // components. The parsed value is a Map keyed by "index:reference".
    const centroids = useMemo(() => {
        if (!svgText) return new Map<string, ComponentCentroid>();
        try {
            return parseComponentCentroids(svgText);
        } catch {
            return new Map<string, ComponentCentroid>();
        }
    }, [svgText]);

    // Wire up the highlight overlay after the SVG is mounted in the DOM.
    // We defer to a separate <g> layer rather than mutating the source
    // markup so the cached SVG stays identical across users and stages.
    const hostRef = useRef<HTMLDivElement | null>(null);
    const [geometry, setGeometry] = useState<HighlightGeometry[]>([]);

    useEffect(() => {
        const host = hostRef.current;
        if (!host || !svgText || centroids.size === 0) {
            setGeometry([]);
            return;
        }
        const svgEl = host.querySelector<SVGSVGElement>("svg");
        if (!svgEl) {
            setGeometry([]);
            return;
        }
        // Fresh geometry pass — resolve each highlight against the
        // parsed centroid map, then use getBBox() (falls back to a
        // sensible default if the browser/jsdom can't measure).
        const resolved = computeHighlightGeometry(svgEl, centroids, highlights);
        setGeometry(resolved);
    }, [svgText, centroids, highlights]);

    // Auth guard: whitespace productName renders a placeholder card
    // rather than firing a bogus request (the API would 400 anyway).
    if (trimmedName.length === 0) {
        return (
            <Card withBorder padding="lg" radius="md">
                <Text c="dimmed">{t("boardViewer.emptyPrompt")}</Text>
            </Card>
        );
    }

    const isNotFound =
        svgQuery.error instanceof ApiError && svgQuery.error.status === 404;
    const isBadRequest =
        svgQuery.error instanceof ApiError && svgQuery.error.status === 400;
    const isOtherError =
        svgQuery.isError && !isNotFound && !isBadRequest;

    return (
        <Card withBorder padding="md" radius="md" data-testid="board-viewer">
            <Stack gap="sm">
                <Group justify="space-between" align="center" wrap="wrap">
                    <Group gap="xs">
                        <IconPhoto size={18} />
                        <Text fw={600}>{t("boardViewer.heading")}</Text>
                        <Text size="sm" c="dimmed">
                            {trimmedName}
                        </Text>
                    </Group>
                    {onStageChange && (
                        <SegmentedControl
                            size="xs"
                            value={stage}
                            onChange={(v) =>
                                onStageChange(v === "pre" ? "pre" : "post")
                            }
                            data={[
                                {
                                    value: "post",
                                    label: t("boardViewer.stagePost"),
                                },
                                {
                                    value: "pre",
                                    label: t("boardViewer.stagePre"),
                                },
                            ]}
                            aria-label={t("boardViewer.stageLabel")}
                        />
                    )}
                </Group>

                {svgQuery.isPending && (
                    <Group gap="xs">
                        <Loader size="sm" />
                        <Text c="dimmed" size="sm">
                            {t("boardViewer.loading")}
                        </Text>
                    </Group>
                )}

                {isNotFound && (
                    <Alert
                        color="yellow"
                        icon={<IconAlertTriangle size={16} />}
                        role="alert"
                        title={t("boardViewer.notCachedTitle")}
                    >
                        <Stack gap="xs">
                            <Text size="sm">
                                {t("boardViewer.notCachedBody")}
                            </Text>
                            <Group>
                                <Button
                                    size="xs"
                                    variant="light"
                                    leftSection={<IconRefresh size={14} />}
                                    onClick={() => void svgQuery.refetch()}
                                    loading={svgQuery.isFetching}
                                >
                                    {t("boardViewer.retry")}
                                </Button>
                            </Group>
                        </Stack>
                    </Alert>
                )}

                {isBadRequest && (
                    <Alert
                        color="red"
                        icon={<IconAlertTriangle size={16} />}
                        role="alert"
                    >
                        {t("boardViewer.badRequest")}
                    </Alert>
                )}

                {isOtherError && (
                    <Alert
                        color="red"
                        icon={<IconAlertTriangle size={16} />}
                        role="alert"
                        title={t("boardViewer.errorTitle")}
                    >
                        {svgQuery.error instanceof Error
                            ? svgQuery.error.message
                            : String(svgQuery.error)}
                    </Alert>
                )}

                {svgText && (
                    <Box
                        ref={hostRef}
                        data-testid="board-viewer-svg-host"
                        style={{
                            position: "relative",
                            width: "100%",
                            height: height ?? undefined,
                            overflow: "hidden",
                            background: "#001a00",
                            borderRadius: 4,
                        }}
                    >
                        {/* Raw SVG injection — see the class comment
                            for the security rationale. */}
                        <div
                            style={{ width: "100%", height: "100%" }}
                            // eslint-disable-next-line react/no-danger
                            dangerouslySetInnerHTML={{ __html: svgText }}
                        />
                        <HighlightOverlay
                            hostRef={hostRef}
                            stage={stage}
                            geometry={geometry}
                            primaryHighlight={primaryHighlight ?? null}
                            onPrimaryChange={onPrimaryChange}
                        />
                    </Box>
                )}
            </Stack>
        </Card>
    );
}

/**
 * Renders (and lifecycle-manages) the overlay <g class="highlights">
 * layer inside the injected SVG. Kept as its own component so that
 * changes to geometry / primaryHighlight don't cause the raw SVG
 * blob to be re-injected (which would blow away parsed centroids).
 */
function HighlightOverlay(props: {
    hostRef: React.RefObject<HTMLDivElement | null>;
    stage: BoardViewerStage;
    geometry: readonly HighlightGeometry[];
    primaryHighlight: BoardHighlight | null;
    onPrimaryChange: ((h: BoardHighlight | null) => void) | undefined;
}) {
    const { hostRef, stage, geometry, primaryHighlight, onPrimaryChange } =
        props;
    const style = STAGE_STYLE[stage];

    useEffect(() => {
        const host = hostRef.current;
        if (!host) return;
        const svgEl = host.querySelector<SVGSVGElement>("svg");
        if (!svgEl) return;

        const NS = "http://www.w3.org/2000/svg";
        // Remove any prior overlay we appended (we own this layer).
        const prior = svgEl.querySelector<SVGGElement>(
            "g[data-nieweb-highlights='true']",
        );
        if (prior) prior.remove();

        if (geometry.length === 0) return;

        const layer = svgEl.ownerDocument!.createElementNS(NS, "g");
        layer.setAttribute("class", "highlights");
        layer.setAttribute("data-nieweb-highlights", "true");
        layer.setAttribute("pointer-events", "auto");

        for (const g of geometry) {
            const isPrimary = samePoint(g.highlight, primaryHighlight);
            const circle = svgEl.ownerDocument!.createElementNS(NS, "circle");
            circle.setAttribute("cx", String(g.cx));
            circle.setAttribute("cy", String(g.cy));
            circle.setAttribute("r", String(g.radius));
            circle.setAttribute("fill", "none");
            circle.setAttribute("stroke", style.stroke);
            circle.setAttribute(
                "stroke-width",
                String(isPrimary ? g.radius * 0.35 : g.radius * 0.18),
            );
            if (style.dash) circle.setAttribute("stroke-dasharray", style.dash);
            circle.setAttribute("vector-effect", "non-scaling-stroke");
            if (isPrimary) {
                // Redundant glow so the primary highlight is
                // recognisable regardless of dashed/solid variant.
                circle.setAttribute(
                    "filter",
                    `drop-shadow(0 0 4px ${style.glow})`,
                );
            }
            circle.setAttribute("data-subpanel", String(g.highlight.subpanelIndex));
            circle.setAttribute("data-reference", g.highlight.reference);
            if (onPrimaryChange) {
                circle.style.cursor = "pointer";
                circle.addEventListener("click", () => {
                    onPrimaryChange(
                        isPrimary
                            ? null
                            : {
                                subpanelIndex: g.highlight.subpanelIndex,
                                reference: g.highlight.reference,
                            },
                    );
                });
            }
            layer.appendChild(circle);
        }

        svgEl.appendChild(layer);

        return () => {
            const still = svgEl.querySelector<SVGGElement>(
                "g[data-nieweb-highlights='true']",
            );
            if (still) still.remove();
        };
    }, [hostRef, stage, geometry, primaryHighlight, style.stroke, style.dash, style.glow, onPrimaryChange]);

    return null;
}
