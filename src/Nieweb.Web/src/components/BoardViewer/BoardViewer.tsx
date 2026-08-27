import {
    memo,
    useCallback,
    useEffect,
    useMemo,
    useRef,
    useState,
} from "react";
import {
    ActionIcon,
    Alert,
    Badge,
    Box,
    Button,
    Card,
    Group,
    Loader,
    Stack,
    Switch,
    Text,
    Tooltip,
} from "@mantine/core";
import {
    IconAlertTriangle,
    IconPhoto,
    IconRefresh,
    IconZoomReset,
} from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";

import { fetchBoardSvg } from "../../api/boardSvgs";
import { ApiError } from "../../api/client";
import {
    cssEscape,
    parseComponentCentroids,
    parseSubpanelOutlines,
    type ComponentCentroid,
    type SubpanelOutline,
} from "./svgParsing";

/**
 * Shared board-viewer primitive (docs/phase-2.md §7.5 TC5 Phase A).
 * Renders the cached product SVG from
 * <code>GET /api/board-svgs/{productName}</code> and overlays four
 * layers on top so the operator can see, at a glance, where a
 * failure lives on the panel:
 *
 * <ol>
 *   <li><strong>Sub-panel outline.</strong> For every unique
 *       <code>sub-panel-index</code> in <code>highlights</code>, a
 *       clone of the sub-panel's <code>&lt;path class="border"&gt;</code>
 *       is drawn on top with a static red stroke — the exact
 *       source outline is reused so the ring hugs the shape even
 *       for weird cut-outs. When the primary highlight lives on a
 *       sub-panel, ONLY that outline gets a subtle CSS pulse so
 *       the operator's eye is drawn to it without every failed
 *       sub-panel flashing in unison.</li>
 *   <li><strong>Component footprint.</strong> Each highlight clones
 *       the matching <code>&lt;g class="component"&gt;</code>
 *       verbatim (transform preserved) into a fresh overlay layer
 *       and re-fills every child in red. Silhouette matches
 *       whatever the CAD file drew (round pad, QFP, connector).</li>
 *   <li><strong>Foreign-material splat.</strong> Highlights whose
 *       Superviseur <code>Object_Type_Id = 0x02000000</code> (33 554 432)
 *       have no CAD counterpart. Instead a stylised yellow paint
 *       splat is placed at the absolute
 *       (<code>xUm</code>, <code>yUm</code>) micron coordinate the
 *       AOI reported in <code>Delta_X</code> / <code>Delta_Y</code>.</li>
 *   <li><strong>Crosshair.</strong> Optional (default off — toggled
 *       via a switch in the card header, preference persisted in
 *       <code>localStorage</code>). When on, the primary highlight
 *       gets a red dashed crosshair across the whole panel viewBox
 *       so tiny 0402 parts remain findable at low zoom. Dash
 *       lengths are sized from the viewBox (SVG user units /
 *       microns) — a fixed CSS dash like <code>6 4</code> would be
 *       invisible on a ~200&nbsp;mm board.</li>
 * </ol>
 *
 * <h3>Mouse controls</h3>
 * <ul>
 *   <li>Wheel: zoom in/out, keeping the point under the cursor
 *       stationary on the board. The wheel handler is attached
 *       natively with <code>{ passive: false }</code> so
 *       <code>preventDefault()</code> actually stops the page from
 *       scrolling while the mouse is over the viewer — React's
 *       synthetic <code>onWheel</code> is passive by default.</li>
 *   <li>Right-mouse-button drag: pan.</li>
 *   <li>Reset button (top-right of the viewer): zoom = 1, pan = 0.</li>
 * </ul>
 *
 * <h3>Stage indicator</h3>
 * <p>Instead of colouring the overlay differently per stage, the
 * active stage is announced by a text Badge overlaid on the panel's
 * dark outer margin so it never obscures any sub-panel content. Both
 * pre- and post-reflow use the same red overlay palette, matching
 * shop-floor mental models where "red = look here".</p>
 *
 * <h3>Overlay persistence across pan / zoom</h3>
 * <p>Pan / zoom lives on a CSS transform on the parent div and does
 * NOT invalidate the overlay-building effect. The SVG-injecting div
 * is memoised so React only touches its DOM when the raw SVG body
 * changes — keeping our appended overlay layer intact through pan,
 * zoom, and re-renders triggered by parent state.</p>
 *
 * <h3>Security note</h3>
 * <p>The source SVG is injected via
 * <code>dangerouslySetInnerHTML</code>. The payload is served by
 * our own API from an admin-configured cache directory — the
 * endpoint refuses <code>..</code> and invalid filename chars, and
 * the source directory is machine-controlled, so the SVG is treated
 * as first-party content. Server-side sanitisation would strip the
 * Sigmalink-generated <code>&lt;style&gt;</code> block plus
 * <code>vector-effect</code> attributes both AOI systems rely on
 * — so we deliberately do not run it.</p>
 */
export type BoardViewerStage = "pre" | "post";

/**
 * Identifies one component on the board via
 * (<code>subpanelIndex</code>, <code>reference</code>). The extra
 * <code>objectTypeId</code> / <code>xUm</code> / <code>yUm</code>
 * fields are optional and only carried for Foreign Material rows
 * (<code>Object_Type_Id = 0x02000000</code>) so the viewer can
 * render a yellow splat at the reported micron coordinate instead
 * of trying to find a non-existent CAD component.
 */
export type BoardHighlight = {
    subpanelIndex: number;
    reference: string;
    /** Superviseur <code>TESTED_OBJECT.Object_Type_Id</code>. */
    objectTypeId?: number;
    /** Absolute X in SVG user units (microns) — Foreign Material only. */
    xUm?: number | null;
    /** Absolute Y in SVG user units (microns) — Foreign Material only. */
    yUm?: number | null;
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
    /**
     * Optional richer stage label rendered as a badge over the
     * panel margin. Falls back to the localised stage name when
     * omitted (e.g. "Post-reflow").
     */
    stageLabel?: string;
    /** Failing components in the active stage. */
    highlights: readonly BoardHighlight[];
    /** Optional focused highlight for row ↔ marker binding. */
    primaryHighlight?: BoardHighlight | null;
    /** Callback when the user clicks a highlighted component. */
    onPrimaryChange?: (h: BoardHighlight | null) => void;
    /** Optional pixel height override; the SVG scales to fill width. */
    height?: number;
};

const OVERLAY_ATTR = "data-nieweb-highlights";
const OVERLAY_RED = "#ff3b30";
/** Crosshair stroke — red matches the highlight palette and stays
 *  readable on the dark-green Sigmalink panel background. */
const CROSSHAIR_COLOR = OVERLAY_RED;
/** Screen-pixel stroke width (paired with non-scaling-stroke). */
const CROSSHAIR_STROKE_PX = 2;
const FM_YELLOW = "#FFEA00";
/** Superviseur constant <code>Object_Type_Id = 0x02000000</code>. */
const OBJECT_TYPE_FOREIGN_MATERIAL = 33554432;
/** Base splat radius in SVG user units (microns). */
const FM_SPLAT_RADIUS_UM = 3000;
const CROSSHAIR_LS_KEY = "nieweb.boardViewer.crosshair";

const ZOOM_MIN = 0.5;
const ZOOM_MAX = 8;
const ZOOM_STEP = 0.1;

const SVG_QUERY_KEY = ["board-viewer", "svg"] as const;

function samePoint(
    a: BoardHighlight | null | undefined,
    b: BoardHighlight | null | undefined,
): boolean {
    if (!a || !b) return false;
    return a.subpanelIndex === b.subpanelIndex && a.reference === b.reference;
}

function isForeignMaterial(h: {
    objectTypeId?: number;
    reference?: string;
}): boolean {
    if (h.objectTypeId === OBJECT_TYPE_FOREIGN_MATERIAL) return true;
    // Belt-and-braces fallback for callers that don't pass
    // objectTypeId — the Superviseur convention names FM rows
    // "FM1", "FM2", …
    return typeof h.reference === "string" && /^FM\d+/i.test(h.reference);
}

function readCrosshairPref(): boolean {
    if (typeof window === "undefined") return false;
    try {
        return window.localStorage.getItem(CROSSHAIR_LS_KEY) === "true";
    } catch {
        return false;
    }
}

function writeCrosshairPref(enabled: boolean): void {
    if (typeof window === "undefined") return;
    try {
        window.localStorage.setItem(CROSSHAIR_LS_KEY, String(enabled));
    } catch {
        // localStorage blocked — carry on with in-memory state.
    }
}

export function BoardViewer(props: BoardViewerProps) {
    const { t } = useTranslation();
    const {
        productName,
        stage,
        stageLabel,
        highlights,
        primaryHighlight,
        onPrimaryChange,
        height,
    } = props;

    const trimmedName = productName.trim();
    const [crosshair, setCrosshair] = useState<boolean>(() => readCrosshairPref());

    const svgQuery = useQuery({
        queryKey: [...SVG_QUERY_KEY, trimmedName],
        queryFn: ({ signal }) => fetchBoardSvg(trimmedName, { signal }),
        enabled: trimmedName.length > 0,
        retry: false,
        refetchOnWindowFocus: false,
        staleTime: 5 * 60 * 1000, // matches server Cache-Control max-age
    });

    const svgText = svgQuery.data?.svg ?? "";

    // Both parses are cheap under DOMParser; re-run only when the
    // raw SVG body itself changes.
    const centroids = useMemo(() => {
        if (!svgText) return new Map<string, ComponentCentroid>();
        try {
            return parseComponentCentroids(svgText);
        } catch {
            return new Map<string, ComponentCentroid>();
        }
    }, [svgText]);
    const subpanels = useMemo(() => {
        if (!svgText) return new Map<number, SubpanelOutline>();
        try {
            return parseSubpanelOutlines(svgText);
        } catch {
            return new Map<number, SubpanelOutline>();
        }
    }, [svgText]);

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

    const handleCrosshairChange = (next: boolean) => {
        setCrosshair(next);
        writeCrosshairPref(next);
    };

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
                    <Switch
                        checked={crosshair}
                        onChange={(e) => handleCrosshairChange(e.currentTarget.checked)}
                        label={t("boardViewer.crosshairToggle")}
                        size="sm"
                        data-testid="board-viewer-crosshair-toggle"
                    />
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
                    <PanZoomStage
                        svgText={svgText}
                        centroids={centroids}
                        subpanels={subpanels}
                        highlights={highlights}
                        primaryHighlight={primaryHighlight ?? null}
                        onPrimaryChange={onPrimaryChange}
                        stage={stage}
                        stageLabel={stageLabel ?? (stage === "pre"
                            ? t("boardViewer.stagePre")
                            : t("boardViewer.stagePost"))}
                        height={height}
                        crosshair={crosshair}
                    />
                )}
            </Stack>
        </Card>
    );
}

// ---------------------------------------------------------------------
// SVG injection — memoised so pan / zoom never blows away the parsed
// DOM (React would otherwise re-run dangerouslySetInnerHTML on every
// re-render, taking our appended overlay with it).
// ---------------------------------------------------------------------

const StaticSvgHost = memo(function StaticSvgHost(props: { svgText: string }) {
    return (
        <div
            style={{ width: "100%", height: "100%" }}
            // eslint-disable-next-line react/no-danger
            dangerouslySetInnerHTML={{ __html: props.svgText }}
        />
    );
});

// ---------------------------------------------------------------------
// Pan / zoom stage
// ---------------------------------------------------------------------

type PanZoomStageProps = {
    svgText: string;
    centroids: ReadonlyMap<string, ComponentCentroid>;
    subpanels: ReadonlyMap<number, SubpanelOutline>;
    highlights: readonly BoardHighlight[];
    primaryHighlight: BoardHighlight | null;
    onPrimaryChange: ((h: BoardHighlight | null) => void) | undefined;
    stage: BoardViewerStage;
    stageLabel: string;
    height: number | undefined;
    crosshair: boolean;
};

function PanZoomStage(props: PanZoomStageProps) {
    const {
        svgText,
        centroids,
        subpanels,
        highlights,
        primaryHighlight,
        onPrimaryChange,
        stage,
        stageLabel,
        height,
        crosshair,
    } = props;
    const { t } = useTranslation();

    const hostRef = useRef<HTMLDivElement | null>(null);
    const [zoom, setZoom] = useState(1);
    const [pan, setPan] = useState({ x: 0, y: 0 });
    const panStart = useRef<
        { mouseX: number; mouseY: number; panX: number; panY: number } | null
    >(null);
    const [panning, setPanning] = useState(false);

    // Paint the overlay layer whenever the SVG body OR the highlight
    // set OR the crosshair preference changes. Zoom / pan live on
    // the parent div's CSS transform and never invalidate this
    // effect — critical because zooming must be smooth AND because
    // otherwise the overlay would blink each frame.
    useEffect(() => {
        const host = hostRef.current;
        if (!host) return;
        const svgEl = host.querySelector<SVGSVGElement>("svg");
        if (!svgEl) return;
        renderOverlay({
            svgEl,
            centroids,
            subpanels,
            highlights,
            primaryHighlight,
            onPrimaryChange,
            crosshair,
        });
    }, [
        svgText,
        centroids,
        subpanels,
        highlights,
        primaryHighlight,
        onPrimaryChange,
        crosshair,
    ]);

    // Native wheel listener with { passive: false } so
    // preventDefault() actually stops the browser page from
    // scrolling while the mouse is over the viewer. React's
    // synthetic onWheel is passive by default in React 17+.
    useEffect(() => {
        const host = hostRef.current;
        if (!host) return;
        const onWheelNative = (e: WheelEvent) => {
            e.preventDefault();
            const rect = host.getBoundingClientRect();
            const cx = e.clientX - rect.left;
            const cy = e.clientY - rect.top;
            setZoom((oldZoom) => {
                const raw = oldZoom * (1 - Math.sign(e.deltaY) * ZOOM_STEP);
                const newZoom = Math.min(Math.max(raw, ZOOM_MIN), ZOOM_MAX);
                if (newZoom === oldZoom) return oldZoom;
                setPan((oldPan) => ({
                    x: cx - ((cx - oldPan.x) / oldZoom) * newZoom,
                    y: cy - ((cy - oldPan.y) / oldZoom) * newZoom,
                }));
                return newZoom;
            });
        };
        host.addEventListener("wheel", onWheelNative, { passive: false });
        return () => {
            host.removeEventListener("wheel", onWheelNative);
        };
    }, []);

    const resetView = useCallback(() => {
        setZoom(1);
        setPan({ x: 0, y: 0 });
    }, []);

    const onMouseDown = useCallback(
        (e: React.MouseEvent<HTMLDivElement>) => {
            // Right-mouse-button starts a pan gesture. Left click
            // stays available for overlay component clicks.
            if (e.button !== 2) return;
            e.preventDefault();
            panStart.current = {
                mouseX: e.clientX,
                mouseY: e.clientY,
                panX: pan.x,
                panY: pan.y,
            };
            setPanning(true);
        },
        [pan.x, pan.y],
    );

    const onMouseMove = useCallback((e: React.MouseEvent<HTMLDivElement>) => {
        const start = panStart.current;
        if (!start) return;
        setPan({
            x: start.panX + (e.clientX - start.mouseX),
            y: start.panY + (e.clientY - start.mouseY),
        });
    }, []);

    const endPan = useCallback(() => {
        panStart.current = null;
        setPanning(false);
    }, []);

    return (
        <Box
            ref={hostRef}
            data-testid="board-viewer-svg-host"
            data-stage={stage}
            onMouseDown={onMouseDown}
            onMouseMove={onMouseMove}
            onMouseUp={endPan}
            onMouseLeave={endPan}
            onContextMenu={(e) => e.preventDefault()}
            style={{
                position: "relative",
                width: "100%",
                height: height ?? undefined,
                minHeight: 320,
                overflow: "hidden",
                background: "#001a00",
                borderRadius: 4,
                cursor: panning ? "grabbing" : "default",
                userSelect: "none",
                touchAction: "none",
            }}
        >
            {/* Scoped keyframes + overlay styling. Style targets the
                overlay layer via data attribute so it never leaks
                onto the source SVG or into other viewer instances.
                Only the primary sub-panel outline animates. */}
            <style>{`
                [${OVERLAY_ATTR}='true'] .nieweb-subpanel-outline {
                    fill: none;
                    stroke: ${OVERLAY_RED};
                    stroke-linejoin: round;
                    stroke-opacity: 0.9;
                    stroke-width: 4;
                }
                [${OVERLAY_ATTR}='true'] .nieweb-subpanel-outline[data-primary='true'] {
                    animation: nieweb-pulse 1.8s ease-in-out infinite;
                }
                [${OVERLAY_ATTR}='true'] .nieweb-component-highlight * {
                    fill: ${OVERLAY_RED} !important;
                    stroke: none !important;
                    opacity: 0.8;
                }
                [${OVERLAY_ATTR}='true'] .nieweb-fm-splat * {
                    fill: ${FM_YELLOW};
                    stroke: none;
                }
                [${OVERLAY_ATTR}='true'] .nieweb-crosshair line {
                    stroke: ${CROSSHAIR_COLOR};
                    stroke-opacity: 0.95;
                }
                @keyframes nieweb-pulse {
                    0%, 100% { stroke-opacity: 0.75; stroke-width: 4; }
                    50%      { stroke-opacity: 1;    stroke-width: 6; }
                }
            `}</style>
            <div
                style={{
                    transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})`,
                    transformOrigin: "0 0",
                    width: "100%",
                    height: "100%",
                    willChange: "transform",
                }}
            >
                <StaticSvgHost svgText={svgText} />
            </div>

            <Badge
                color="red"
                variant="filled"
                data-testid="board-viewer-stage-badge"
                style={{
                    position: "absolute",
                    top: 8,
                    left: 8,
                    letterSpacing: 0.5,
                    pointerEvents: "none",
                }}
            >
                {stageLabel}
            </Badge>

            <Group
                gap="xs"
                style={{
                    position: "absolute",
                    top: 8,
                    right: 8,
                }}
            >
                <Tooltip label={t("boardViewer.panZoomHint")} position="left">
                    <Badge
                        color="dark"
                        variant="filled"
                        style={{ pointerEvents: "none" }}
                        data-testid="board-viewer-zoom-level"
                    >
                        {Math.round(zoom * 100)}%
                    </Badge>
                </Tooltip>
                <Tooltip label={t("boardViewer.zoomReset")} position="left">
                    <ActionIcon
                        variant="filled"
                        color="dark"
                        onClick={resetView}
                        aria-label={t("boardViewer.zoomReset")}
                        data-testid="board-viewer-zoom-reset"
                    >
                        <IconZoomReset size={16} />
                    </ActionIcon>
                </Tooltip>
            </Group>
        </Box>
    );
}

// ---------------------------------------------------------------------
// Overlay layer builder
// ---------------------------------------------------------------------

type OverlayInputs = {
    svgEl: SVGSVGElement;
    centroids: ReadonlyMap<string, ComponentCentroid>;
    subpanels: ReadonlyMap<number, SubpanelOutline>;
    highlights: readonly BoardHighlight[];
    primaryHighlight: BoardHighlight | null;
    onPrimaryChange: ((h: BoardHighlight | null) => void) | undefined;
    crosshair: boolean;
};

/**
 * Build the four overlay sub-layers (sub-panel outlines, component
 * fills, FM splats, crosshair) and append them to <code>svgEl</code>.
 * Idempotent: any prior overlay layer on the same SVG is removed
 * first so consecutive calls (from useEffect re-runs, React strict-
 * mode double-invocations, etc.) don't stack duplicates.
 */
function renderOverlay(inputs: OverlayInputs): void {
    const {
        svgEl,
        centroids,
        subpanels,
        highlights,
        primaryHighlight,
        onPrimaryChange,
        crosshair,
    } = inputs;
    const NS = "http://www.w3.org/2000/svg";

    // Nuke any prior overlay owned by us.
    svgEl
        .querySelectorAll(`g[${OVERLAY_ATTR}='true']`)
        .forEach((el) => el.remove());

    if (highlights.length === 0) return;

    const doc = svgEl.ownerDocument!;
    const overlay = doc.createElementNS(NS, "g");
    overlay.setAttribute(OVERLAY_ATTR, "true");
    overlay.setAttribute("class", "nieweb-overlay");
    overlay.setAttribute("pointer-events", "auto");

    // Layer 1: sub-panel outlines (unique indices only). All get
    // the static red border; only the one carrying the primary
    // highlight gets the CSS pulse via data-primary="true".
    const subpanelIndices = new Set(highlights.map((h) => h.subpanelIndex));
    const primarySubpanel = primaryHighlight?.subpanelIndex;
    const subpanelLayer = doc.createElementNS(NS, "g");
    subpanelLayer.setAttribute("class", "nieweb-subpanel-layer");
    subpanelIndices.forEach((idx) => {
        const outline = subpanels.get(idx);
        if (!outline) return;
        const path = doc.createElementNS(NS, "path");
        path.setAttribute("class", "nieweb-subpanel-outline");
        path.setAttribute("d", outline.pathD);
        path.setAttribute("vector-effect", "non-scaling-stroke");
        path.setAttribute("data-subpanel", String(idx));
        if (primarySubpanel !== undefined && idx === primarySubpanel) {
            path.setAttribute("data-primary", "true");
        }
        subpanelLayer.appendChild(path);
    });
    overlay.appendChild(subpanelLayer);

    // Layer 2 + 3: component clones (red) and FM splats (yellow).
    // FM splats need the panel viewBox to mirror the AOI machine's
    // LOWER_LEFT (Y-up) coord into SVG's UPPER_LEFT (Y-down) coord.
    // We compute the flip base once here so buildFmSplat /
    // resolvePrimaryCoord stay coord-system agnostic.
    const viewBoxForY = svgEl.viewBox?.baseVal;
    const yFlipBase =
        viewBoxForY && Number.isFinite(viewBoxForY.height) && viewBoxForY.height > 0
            ? viewBoxForY.y + viewBoxForY.height
            : null;
    const componentLayer = doc.createElementNS(NS, "g");
    componentLayer.setAttribute("class", "nieweb-component-layer");
    const splatLayer = doc.createElementNS(NS, "g");
    splatLayer.setAttribute("class", "nieweb-splat-layer");
    for (const h of highlights) {
        if (isForeignMaterial(h)) {
            const splat = buildFmSplat(doc, h, onPrimaryChange, primaryHighlight, yFlipBase);
            if (splat) splatLayer.appendChild(splat);
            continue;
        }
        const original = svgEl.querySelector<SVGGElement>(
            `g.component[sub-panel-index="${h.subpanelIndex}"][reference="${cssEscape(h.reference)}"]`,
        );
        if (!original) continue;
        const clone = original.cloneNode(true) as SVGGElement;
        clone.setAttribute("class", "nieweb-component-highlight");
        clone.setAttribute("data-subpanel", String(h.subpanelIndex));
        clone.setAttribute("data-reference", h.reference);
        const isPrimary = samePoint(h, primaryHighlight);
        if (isPrimary) {
            clone.setAttribute("data-primary", "true");
        }
        if (onPrimaryChange) {
            (clone as unknown as SVGGraphicsElement).style.cursor = "pointer";
            clone.addEventListener("click", (evt) => {
                evt.stopPropagation();
                onPrimaryChange(
                    isPrimary
                        ? null
                        : {
                            subpanelIndex: h.subpanelIndex,
                            reference: h.reference,
                            objectTypeId: h.objectTypeId,
                            xUm: h.xUm,
                            yUm: h.yUm,
                        },
                );
            });
        }
        componentLayer.appendChild(clone);
    }
    overlay.appendChild(componentLayer);
    overlay.appendChild(splatLayer);

    // Layer 4: crosshair on the primary highlight (opt-in via
    // header switch, default off, preference persisted in
    // localStorage).
    if (crosshair && primaryHighlight) {
        const viewBox = svgEl.viewBox?.baseVal;
        const primaryCoord = resolvePrimaryCoord(
            primaryHighlight,
            centroids,
            yFlipBase,
        );
        if (
            primaryCoord &&
            viewBox &&
            (viewBox.width > 0 || viewBox.height > 0)
        ) {
            // Dash lengths are SVG user units (microns). With
            // vector-effect:non-scaling-stroke, stroke-*width* is
            // device pixels but dasharray stays in user space — so
            // a CSS value like "400 300" renders as ~1 px speckles
            // on a full-panel view and looks like "no crosshair".
            // Size dashes from the viewBox so they read at low zoom.
            const boardSpan = Math.max(viewBox.width, viewBox.height);
            const dashOn = Math.max(2_000, Math.round(boardSpan * 0.02));
            const dashOff = Math.max(1_500, Math.round(dashOn * 0.7));
            const dasharray = `${dashOn} ${dashOff}`;

            const crosshairLayer = doc.createElementNS(NS, "g");
            crosshairLayer.setAttribute("class", "nieweb-crosshair");
            crosshairLayer.setAttribute("pointer-events", "none");

            const paintLine = (
                line: SVGLineElement,
                x1: number,
                y1: number,
                x2: number,
                y2: number,
            ) => {
                line.setAttribute("x1", String(x1));
                line.setAttribute("y1", String(y1));
                line.setAttribute("x2", String(x2));
                line.setAttribute("y2", String(y2));
                line.setAttribute("vector-effect", "non-scaling-stroke");
                line.setAttribute("stroke", CROSSHAIR_COLOR);
                line.setAttribute("stroke-opacity", "0.95");
                line.setAttribute("stroke-width", String(CROSSHAIR_STROKE_PX));
                line.setAttribute("stroke-dasharray", dasharray);
            };

            const hLine = doc.createElementNS(NS, "line");
            paintLine(
                hLine,
                viewBox.x,
                primaryCoord.y,
                viewBox.x + viewBox.width,
                primaryCoord.y,
            );
            crosshairLayer.appendChild(hLine);

            const vLine = doc.createElementNS(NS, "line");
            paintLine(
                vLine,
                primaryCoord.x,
                viewBox.y,
                primaryCoord.x,
                viewBox.y + viewBox.height,
            );
            crosshairLayer.appendChild(vLine);

            overlay.appendChild(crosshairLayer);
        }
    }

    svgEl.appendChild(overlay);
}

/**
 * Resolve the (x, y) micron coordinate of a highlight for the
 * crosshair. Regular components use the CAD centroid; Foreign
 * Material rows use the absolute xUm/yUm the AOI reported,
 * mirrored into SVG's Y-down coord system when a valid viewBox
 * height was passed as <code>yFlipBase</code>.
 */
function resolvePrimaryCoord(
    h: BoardHighlight,
    centroids: ReadonlyMap<string, ComponentCentroid>,
    yFlipBase: number | null,
): { x: number; y: number } | null {
    if (isForeignMaterial(h)) {
        if (
            typeof h.xUm === "number" &&
            typeof h.yUm === "number" &&
            Number.isFinite(h.xUm) &&
            Number.isFinite(h.yUm)
        ) {
            const y = yFlipBase !== null ? yFlipBase - h.yUm : h.yUm;
            return { x: h.xUm, y };
        }
        return null;
    }
    const centroid = centroids.get(`${h.subpanelIndex}:${h.reference}`);
    return centroid ? { x: centroid.cx, y: centroid.cy } : null;
}

/**
 * Build the yellow paint-splat overlay group for a Foreign
 * Material highlight. Composed from a large irregular blob path
 * plus a scatter of secondary blobs so the shape is organic rather
 * than a plain circle. Positioned by translate — no rotation
 * because operators don't need to see a "rotation" for FM defects.
 *
 * The AOI machine reports Delta_X / Delta_Y in its own Cartesian
 * coord system (origin at LOWER_LEFT of the panel, Y → up — see
 * Sigmalink's <code>AxisDirections</code> enum). The generated
 * panel SVG uses screen coords (origin at UPPER_LEFT, Y → down).
 * When <code>yFlipBase</code> is provided (= viewBox.y +
 * viewBox.height) we mirror Y so the splat lands where the
 * operator would expect it on the panel image. Falls back to the
 * raw coord when the viewBox is missing.
 *
 * Returns <code>null</code> if the row is missing coordinates.
 */
function buildFmSplat(
    doc: Document,
    h: BoardHighlight,
    onPrimaryChange: ((next: BoardHighlight | null) => void) | undefined,
    primary: BoardHighlight | null,
    yFlipBase: number | null,
): SVGGElement | null {
    if (
        typeof h.xUm !== "number" ||
        typeof h.yUm !== "number" ||
        !Number.isFinite(h.xUm) ||
        !Number.isFinite(h.yUm)
    ) {
        return null;
    }
    const yAdjusted = yFlipBase !== null ? yFlipBase - h.yUm : h.yUm;
    const NS = "http://www.w3.org/2000/svg";
    const group = doc.createElementNS(NS, "g");
    group.setAttribute("class", "nieweb-fm-splat");
    group.setAttribute("transform", `translate(${h.xUm} ${yAdjusted})`);
    group.setAttribute("data-subpanel", String(h.subpanelIndex));
    group.setAttribute("data-reference", h.reference);
    const isPrimary = samePoint(h, primary);
    if (isPrimary) {
        group.setAttribute("data-primary", "true");
    }

    // Main blob — an irregular splat outline hand-crafted in SVG
    // path syntax. Scaled to ≈ FM_SPLAT_RADIUS_UM microns per side.
    const R = FM_SPLAT_RADIUS_UM;
    const main = doc.createElementNS(NS, "path");
    main.setAttribute(
        "d",
        [
            `M ${-R * 0.9} ${-R * 0.3}`,
            `C ${-R * 0.6} ${-R * 1.0}, ${R * 0.2} ${-R * 1.05}, ${R * 0.55} ${-R * 0.5}`,
            `C ${R * 1.05} ${-R * 0.4}, ${R * 1.2} ${R * 0.25}, ${R * 0.75} ${R * 0.55}`,
            `C ${R * 0.9} ${R * 1.05}, ${R * 0.35} ${R * 1.2}, ${-R * 0.1} ${R * 0.95}`,
            `C ${-R * 0.55} ${R * 1.25}, ${-R * 1.0} ${R * 0.95}, ${-R * 0.95} ${R * 0.5}`,
            `C ${-R * 1.35} ${R * 0.4}, ${-R * 1.45} ${-R * 0.15}, ${-R * 1.15} ${-R * 0.25}`,
            "Z",
        ].join(" "),
    );
    main.setAttribute("opacity", "0.7");
    group.appendChild(main);

    // Secondary satellite blobs — scattered around the main splat
    // to sell the "paint spatter" look. Coordinates are hand-picked
    // multiples of R so the whole splat scales cleanly.
    const satellites: Array<[number, number, number, number]> = [
        [-R * 1.5, R * 0.85, R * 0.22, 0.7],
        [R * 1.35, -R * 0.75, R * 0.18, 0.65],
        [R * 0.9, R * 1.5, R * 0.13, 0.55],
        [-R * 0.95, -R * 1.0, R * 0.11, 0.6],
        [R * 1.75, R * 0.55, R * 0.09, 0.5],
    ];
    for (const [cx, cy, r, op] of satellites) {
        const dot = doc.createElementNS(NS, "circle");
        dot.setAttribute("cx", String(cx));
        dot.setAttribute("cy", String(cy));
        dot.setAttribute("r", String(r));
        dot.setAttribute("opacity", String(op));
        group.appendChild(dot);
    }

    if (onPrimaryChange) {
        (group as unknown as SVGGraphicsElement).style.cursor = "pointer";
        group.addEventListener("click", (evt) => {
            evt.stopPropagation();
            onPrimaryChange(
                isPrimary
                    ? null
                    : {
                        subpanelIndex: h.subpanelIndex,
                        reference: h.reference,
                        objectTypeId: h.objectTypeId,
                        xUm: h.xUm,
                        yUm: h.yUm,
                    },
            );
        });
    }

    return group;
}
