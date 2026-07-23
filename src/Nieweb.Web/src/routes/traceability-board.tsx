import { useMemo, useState, type FormEvent } from "react";
import {
    Alert,
    Badge,
    Button,
    Card,
    Group,
    Loader,
    SegmentedControl,
    SimpleGrid,
    Stack,
    Table,
    Text,
    TextInput,
    Title,
    Tooltip,
} from "@mantine/core";
import {
    IconAlertTriangle,
    IconBarcode,
    IconSearch,
    IconTable,
} from "@tabler/icons-react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { useQueries, useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import {
    fetchBoardByBarcode,
    fetchFailedObjectsForPanel,
    type BoardStageTrace,
    type FailedObjectsResponse,
} from "../api/traceability";
import { fetchProducts, type ProductOption } from "../api/sources";
import { ApiError } from "../api/client";
import { SavedViewsMenu } from "../components/SavedViewsMenu";
import {
    BoardViewer,
    type BoardHighlight,
    type BoardViewerStage,
} from "../components/BoardViewer/BoardViewer";
import { FailedObjectsTable } from "../components/FailedObjectsTable";
import type { TraceabilityBoardSearch } from "./traceability-board.search";

/**
 * TC3 board-trace route (`/traceability/board?barcode=X`). Consumes
 * the TC2 endpoint (`GET /api/traceability/boards/by-barcode`) and
 * renders one card per configured AOI stage side-by-side so operators
 * can compare pre-reflow vs post-reflow at a glance.
 *
 * The URL search-params double as the saved-view payload: users can
 * bookmark frequently-checked barcodes (golden samples, complaint
 * boards) via the shared {@link SavedViewsMenu}.
 *
 * <h3>TC5 Phase D — drill-down</h3>
 *
 * <p>Once a barcode has been resolved, each stage card exposes a
 * <b>View failures</b> action (and every sub-panel row acts as the
 * same trigger) that opens an inline drill-down section below the
 * stage cards. The drill-down contains:</p>
 *
 * <ul>
 *   <li>Stage selector — post-reflow (red) / pre-reflow (purple).
 *       Clicking a row in the inactive stage table auto-promotes it.</li>
 *   <li>{@link BoardViewer} — renders the cached product SVG for the
 *       active stage's panel, overlaid with per-object markers. The
 *       viewer degrades gracefully when the SVG or product name are
 *       missing (banner only, tables continue to render).</li>
 *   <li>Two {@link FailedObjectsTable} instances (one per stage) with
 *       the full 18-column enriched projection defined in
 *       <code>docs/phase-2.md</code> §7.5 TC5.</li>
 * </ul>
 *
 * <p>Row ↔ marker two-way binding: clicking a row highlights the
 * matching marker on the board; clicking a marker highlights the
 * matching row. The parent route owns the shared
 * <code>primaryHighlight</code> state so the binding is symmetric.</p> */
export function TraceabilityBoardRoute() {
    const { t } = useTranslation();
    // `strict: false` lets us read the search on any route the
    // component is mounted under. Production wiring goes through
    // validateTraceabilityBoardSearch (router.ts); tests mount the
    // component under a minimal tree with the same shape.
    const rawSearch = useSearch({ strict: false });
    const search = rawSearch as TraceabilityBoardSearch;
    const navigate = useNavigate();

    // Local input state — initialised from the URL, edited freely,
    // pushed back into the URL on submit. Same URL-drives-report
    // pattern as PanelYieldRoute.
    const [barcodeInput, setBarcodeInput] = useState(search.barcode ?? "");
    const [formError, setFormError] = useState<string | null>(null);

    // TC5 Phase D drill-down state. `null` = collapsed. When set to a
    // sourceId, the drill-down section appears below the stage cards
    // and that source becomes the initial active-viewer stage. The
    // primary highlight is shared across the two per-stage tables so
    // that the same marker on the viewer stays selected regardless of
    // which stage the user is currently focused on.
    const [drilldownSourceId, setDrilldownSourceId] = useState<string | null>(null);
    const [primaryHighlight, setPrimaryHighlight] = useState<BoardHighlight | null>(null);

    const boardQuery = useQuery({
        queryKey: ["traceability-board", search.barcode],
        queryFn: () => fetchBoardByBarcode(search.barcode!),
        enabled: Boolean(search.barcode),
        retry: false,
    });

    function openDrilldown(sourceId: string) {
        setDrilldownSourceId(sourceId);
        // Fresh drill-in → clear any stale primary marker from a
        // previous barcode. Cheap; avoids "phantom highlight" bugs
        // when the operator jumps between panels quickly.
        setPrimaryHighlight(null);
    }

    function closeDrilldown() {
        setDrilldownSourceId(null);
        setPrimaryHighlight(null);
    }

    function handleSubmit(evt: FormEvent<HTMLFormElement>) {
        evt.preventDefault();
        const trimmed = barcodeInput.trim();
        if (trimmed.length === 0) {
            setFormError(t("traceability.board.barcodeRequired"));
            return;
        }
        if (trimmed.length > 64) {
            setFormError(t("traceability.board.barcodeTooLong"));
            return;
        }
        setFormError(null);
        void navigate({
            to: "/traceability/board",
            search: { barcode: trimmed } satisfies TraceabilityBoardSearch,
        });
    }

    function applySavedFilter(filter: TraceabilityBoardSearch) {
        setBarcodeInput(filter.barcode ?? "");
        void navigate({
            to: "/traceability/board",
            search: filter,
        });
    }

    // 404 from the API → "Barcode not found on any stage". Any other
    // error → generic error alert.
    const notFound = boardQuery.error instanceof ApiError && boardQuery.error.status === 404;
    const otherError =
        boardQuery.error !== null &&
        boardQuery.error !== undefined &&
        !(boardQuery.error instanceof ApiError && boardQuery.error.status === 404);

    const canSave = useMemo(() => Boolean(search.barcode), [search.barcode]);

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("traceability.board.title")}</Title>
                <Text c="dimmed">{t("traceability.board.subtitle")}</Text>
            </Stack>

            <Card
                withBorder
                padding="lg"
                radius="md"
                component="form"
                onSubmit={handleSubmit}
                data-testid="traceability-board-form"
            >
                <Stack gap="sm">
                    <Group gap="xs">
                        <IconBarcode size={18} />
                        <Title order={4}>{t("traceability.board.barcodeLabel")}</Title>
                    </Group>
                    <Group align="flex-end" wrap="nowrap">
                        <TextInput
                            aria-label={t("traceability.board.barcodeLabel")}
                            placeholder={t("traceability.board.barcodePlaceholder")}
                            description={t("traceability.board.barcodeHint")}
                            value={barcodeInput}
                            onChange={(e) => setBarcodeInput(e.currentTarget.value)}
                            error={formError ?? undefined}
                            maxLength={64}
                            autoComplete="off"
                            spellCheck={false}
                            style={{ flex: 1 }}
                            data-testid="traceability-board-input"
                        />
                        <Button
                            type="submit"
                            leftSection={<IconSearch size={16} />}
                            data-testid="traceability-board-submit"
                        >
                            {t("traceability.board.submit")}
                        </Button>
                        <SavedViewsMenu<TraceabilityBoardSearch>
                            reportKey="traceability-board"
                            currentFilter={search}
                            onApply={applySavedFilter}
                            canSave={canSave}
                        />
                    </Group>
                </Stack>
            </Card>

            {!search.barcode && (
                <Card withBorder padding="lg" radius="md">
                    <Text c="dimmed">{t("traceability.board.emptyPrompt")}</Text>
                </Card>
            )}

            {search.barcode && boardQuery.isPending && (
                <Card withBorder padding="lg" radius="md" data-testid="traceability-board-loading">
                    <Group gap="xs">
                        <Loader size="sm" />
                        <Text c="dimmed">{t("traceability.board.loading")}</Text>
                    </Group>
                </Card>
            )}

            {notFound && (
                <Alert
                    color="yellow"
                    icon={<IconAlertTriangle size={18} />}
                    title={t("traceability.board.notFoundTitle")}
                    role="alert"
                >
                    {t("traceability.board.notFoundBody")}
                </Alert>
            )}

            {otherError && (
                <Alert
                    color="red"
                    icon={<IconAlertTriangle size={18} />}
                    title={t("traceability.board.errorTitle")}
                    role="alert"
                >
                    {boardQuery.error instanceof Error
                        ? boardQuery.error.message
                        : String(boardQuery.error)}
                </Alert>
            )}

            {boardQuery.data && (
                <Stack gap="sm">
                    <Text>
                        <Text component="span" c="dimmed">
                            {t("traceability.board.barcodeLabelResult")}:{" "}
                        </Text>
                        <Text component="span" fw={600} data-testid="traceability-board-barcode">
                            {boardQuery.data.barcode}
                        </Text>
                    </Text>
                    <SimpleGrid cols={{ base: 1, md: boardQuery.data.stages.length }} spacing="md">
                        {boardQuery.data.stages.map((stage) => (
                            <StageCard
                                key={stage.sourceId}
                                stage={stage}
                                onOpenDrilldown={() => openDrilldown(stage.sourceId)}
                            />
                        ))}
                    </SimpleGrid>

                    {drilldownSourceId && (
                        <FailureDrilldown
                            stages={boardQuery.data.stages}
                            activeSourceId={drilldownSourceId}
                            onActiveSourceChange={setDrilldownSourceId}
                            primaryHighlight={primaryHighlight}
                            onPrimaryChange={setPrimaryHighlight}
                            onClose={closeDrilldown}
                        />
                    )}
                </Stack>
            )}
        </Stack>
    );
}

/**
 * Card rendering one `BoardStageTrace`. Handles three visual states:
 * error (per-stage `Error` from TC2), not-found (`Panel === null`
 * with no error), and found (panel meta + sub-panel table).
 *
 * TC5 Phase D: when `onOpenDrilldown` is provided and the panel is
 * found with at least one failing tested-object, the card exposes a
 * <b>View failures</b> action; each sub-panel row also becomes
 * clickable and fires the same callback.
 */
function StageCard(props: {
    stage: BoardStageTrace;
    onOpenDrilldown?: () => void;
}) {
    const { stage, onOpenDrilldown } = props;
    const { t } = useTranslation();

    const found = stage.panel !== null;
    const hasError = stage.error !== null && stage.error !== undefined;
    const hasFailures =
        found &&
        stage.panel !== null &&
        stage.panel.panel.nbOfErrorObject > 0;
    const canDrill = Boolean(onOpenDrilldown) && hasFailures;

    return (
        <Card
            withBorder
            padding="md"
            radius="md"
            data-testid={`traceability-board-stage-${stage.sourceId}`}
        >
            <Stack gap="sm">
                <Group justify="space-between" wrap="nowrap">
                    <Group gap="xs" wrap="nowrap" style={{ minWidth: 0 }}>
                        <Title order={5} lineClamp={1}>
                            {stage.sourceName}
                        </Title>
                        <Badge size="sm" variant="light">{stage.sourceId}</Badge>
                    </Group>
                    {hasError ? (
                        <Badge color="red" variant="light">
                            {t("traceability.board.stageErrorTitle")}
                        </Badge>
                    ) : found ? (
                        <Badge color="green" variant="light">
                            {t("traceability.board.stageFound")}
                        </Badge>
                    ) : (
                        <Badge color="gray" variant="light">
                            {t("traceability.board.stageNotFound")}
                        </Badge>
                    )}
                </Group>

                <Tooltip
                    label={
                        stage.pinsAvailable
                            ? t("traceability.board.capabilityPinLevel")
                            : t("traceability.board.capabilityNoPinLevel")
                    }
                    withArrow
                >
                    <Badge
                        variant="outline"
                        color={stage.pinsAvailable ? "blue" : "gray"}
                        size="xs"
                    >
                        {stage.pinsAvailable
                            ? t("traceability.board.capabilityPinLevel")
                            : t("traceability.board.capabilityNoPinLevel")}
                    </Badge>
                </Tooltip>

                {hasError && (
                    <Alert
                        color="red"
                        icon={<IconAlertTriangle size={16} />}
                        role="alert"
                        title={t("traceability.board.stageErrorTitle")}
                    >
                        {stage.error}
                    </Alert>
                )}

                {!hasError && !found && (
                    <Text c="dimmed" size="sm">
                        {t("traceability.board.stageNotFound")}
                    </Text>
                )}

                {found && stage.panel && (
                    <PanelSummary
                        stage={stage}
                        onOpenDrilldown={canDrill ? onOpenDrilldown : undefined}
                    />
                )}

                {canDrill && onOpenDrilldown && (
                    <Group justify="flex-end">
                        <Button
                            size="xs"
                            variant="light"
                            leftSection={<IconTable size={14} />}
                            onClick={onOpenDrilldown}
                            data-testid={`traceability-board-open-drilldown-${stage.sourceId}`}
                        >
                            {t("traceability.board.drilldown.open")}
                        </Button>
                    </Group>
                )}
            </Stack>
        </Card>
    );
}

function PanelSummary(props: {
    stage: BoardStageTrace;
    onOpenDrilldown?: () => void;
}) {
    const { stage, onOpenDrilldown } = props;
    const { t } = useTranslation();
    const panel = stage.panel!;
    return (
        <Stack gap="xs">
            <MetaRow label={t("traceability.board.panelIdLabel")} value={String(panel.panel.panelId)} />
            <MetaRow
                label={t("traceability.board.panelDateLabel")}
                value={new Date(panel.panelUtc).toISOString().replace("T", " ").replace("Z", " UTC")}
            />
            <MetaRow label={t("traceability.board.panelStatusLabel")} value={String(panel.panel.panelStatus)} />
            <MetaRow label={t("traceability.board.productLabel")} value={panel.productName ?? String(panel.panel.productId)} />
            <MetaRow label={t("traceability.board.machineLabel")} value={String(panel.panel.machineId)} />
            <MetaRow
                label={t("traceability.board.reviewedLabel")}
                value={
                    panel.panel.hasBeenReviewed
                        ? t("traceability.board.reviewedYes")
                        : t("traceability.board.reviewedNo")
                }
            />

            <Title order={6} mt="sm">{t("traceability.board.subpanelsHeading")}</Title>
            {stage.cards.length === 0 ? (
                <Text c="dimmed" size="sm">{t("traceability.board.subpanelsEmpty")}</Text>
            ) : (
                <Table striped withTableBorder highlightOnHover data-testid={`traceability-board-cards-${stage.sourceId}`}>
                    <Table.Thead>
                        <Table.Tr>
                            <Table.Th>{t("traceability.board.subpanelsColCardId")}</Table.Th>
                            <Table.Th>{t("traceability.board.subpanelsColStatus")}</Table.Th>
                            <Table.Th>{t("traceability.board.subpanelsColObjectCount")}</Table.Th>
                            <Table.Th>{t("traceability.board.subpanelsColErrorCount")}</Table.Th>
                        </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                        {stage.cards.map((c) => (
                            <Table.Tr
                                key={c.cardIdOnPanel}
                                onClick={onOpenDrilldown}
                                style={onOpenDrilldown ? { cursor: "pointer" } : undefined}
                                data-testid={`traceability-board-cards-${stage.sourceId}-row-${c.cardIdOnPanel}`}
                            >
                                <Table.Td>{c.cardIdOnPanel}</Table.Td>
                                <Table.Td>{c.cardStatus}</Table.Td>
                                <Table.Td>{c.nbOfTestedObject}</Table.Td>
                                <Table.Td>{c.nbOfErrorObject}</Table.Td>
                            </Table.Tr>
                        ))}
                    </Table.Tbody>
                </Table>
            )}
        </Stack>
    );
}

function MetaRow(props: { label: string; value: string }) {
    return (
        <Group gap="xs" wrap="nowrap">
            <Text size="sm" c="dimmed" style={{ minWidth: 140 }}>
                {props.label}
            </Text>
            <Text size="sm" fw={500}>{props.value}</Text>
        </Group>
    );
}

/**
 * Infer the visual {@link BoardViewerStage} colour from a stage's
 * source id. Kept intentionally forgiving: anything containing
 * "pre" (case-insensitive) is pre-reflow; anything containing
 * "post" is post-reflow; everything else defaults to post so the
 * viewer never renders in an unstyled state. If new stages are
 * added later (AXI, ICT), extend this mapper.
 */
function inferViewerStage(sourceId: string): BoardViewerStage {
    const s = sourceId.toLowerCase();
    if (s.includes("pre")) return "pre";
    return "post";
}

/**
 * TC5 Phase D — drill-down section rendered inline below the stage
 * cards. Owns two failed-objects queries (one per stage that has a
 * panel) and two product-name lookups so the {@link BoardViewer}
 * can be fed a resolved product name. The active-stage selector
 * drives which stage's SVG + highlights the viewer renders; both
 * tables remain visible so the operator can compare pre / post
 * failures side-by-side.
 */
function FailureDrilldown(props: {
    stages: readonly BoardStageTrace[];
    activeSourceId: string;
    onActiveSourceChange: (sourceId: string) => void;
    primaryHighlight: BoardHighlight | null;
    onPrimaryChange: (h: BoardHighlight | null) => void;
    onClose: () => void;
}) {
    const {
        stages,
        activeSourceId,
        onActiveSourceChange,
        primaryHighlight,
        onPrimaryChange,
        onClose,
    } = props;
    const { t } = useTranslation();

    // We only drill into stages that have a resolved panel; stages
    // without a panel are shown as an unavailable-hint alongside so
    // the operator understands why the table is missing.
    const drillableStages = useMemo(
        () => stages.filter((s) => s.panel !== null),
        [stages],
    );

    // Per-stage failed-objects fetches. Keyed by (sourceId, panelId)
    // so switching barcodes invalidates cleanly. `enabled` guards
    // against stages that lack a panel.
    const failedQueries = useQueries({
        queries: drillableStages.map((stage) => ({
            queryKey: [
                "traceability-failed-objects",
                stage.sourceId,
                stage.panel?.panel.panelId,
            ],
            queryFn: () =>
                fetchFailedObjectsForPanel(
                    stage.sourceId,
                    stage.panel!.panel.panelId,
                ),
            enabled: stage.panel !== null,
            retry: false,
        })),
    });

    // Per-stage product-name lookups. We fan out over the sources
    // that host a resolved panel; the resulting ProductOption list
    // is scanned for the panel's productId. Cheap: product lists
    // are small and cached by TanStack Query for the session.
    const productQueries = useQueries({
        queries: drillableStages.map((stage) => ({
            queryKey: ["sources-products", stage.sourceId],
            queryFn: () => fetchProducts(stage.sourceId),
            enabled: stage.panel !== null,
            retry: false,
            staleTime: 5 * 60 * 1000,
        })),
    });

    // Map sourceId → resolved data for O(1) lookup inside the render.
    const perStage = useMemo(() => {
        const map = new Map<
            string,
            {
                stage: BoardStageTrace;
                failed: FailedObjectsResponse | undefined;
                failedIsLoading: boolean;
                failedError: string | null;
                productName: string | null;
            }
        >();
        drillableStages.forEach((stage, idx) => {
            const fq = failedQueries[idx];
            const pq = productQueries[idx];
            const products: ProductOption[] | undefined = pq?.data;
            const productName =
                products && stage.panel
                    ? products.find((p) => p.id === stage.panel!.panel.productId)?.name
                        ?? null
                    : null;
            map.set(stage.sourceId, {
                stage,
                failed: fq?.data,
                failedIsLoading: fq?.isPending ?? false,
                failedError:
                    fq?.error instanceof Error
                        ? fq.error.message
                        : fq?.error != null
                            ? String(fq.error)
                            : null,
                productName,
            });
        });
        return map;
    }, [drillableStages, failedQueries, productQueries]);

    const activeEntry = perStage.get(activeSourceId);
    const activeStageVisual: BoardViewerStage = inferViewerStage(activeSourceId);
    const activeHighlights: BoardHighlight[] = useMemo(() => {
        const list = activeEntry?.failed?.objects ?? [];
        const out: BoardHighlight[] = [];
        for (const row of list) {
            const ref = row.topology?.trim();
            if (!ref) continue;
            out.push({ subpanelIndex: row.cardIdOnPanel, reference: ref });
        }
        return out;
    }, [activeEntry]);

    return (
        <Card
            withBorder
            padding="md"
            radius="md"
            data-testid="traceability-board-drilldown"
        >
            <Stack gap="sm">
                <Group justify="space-between" wrap="wrap">
                    <Group gap="xs">
                        <IconTable size={18} />
                        <Title order={5}>{t("traceability.board.drilldown.title")}</Title>
                    </Group>
                    <Group gap="sm">
                        {drillableStages.length > 1 && (
                            <SegmentedControl
                                size="xs"
                                value={activeSourceId}
                                onChange={onActiveSourceChange}
                                data={drillableStages.map((s) => ({
                                    value: s.sourceId,
                                    label: s.sourceName,
                                }))}
                                aria-label={t("traceability.board.drilldown.activeStageLabel")}
                                data-testid="traceability-board-drilldown-stage-selector"
                            />
                        )}
                        <Button
                            size="xs"
                            variant="subtle"
                            onClick={onClose}
                            data-testid="traceability-board-drilldown-close"
                        >
                            {t("traceability.board.drilldown.close")}
                        </Button>
                    </Group>
                </Group>

                {activeEntry?.productName ? (
                    <BoardViewer
                        productName={activeEntry.productName}
                        stage={activeStageVisual}
                        highlights={activeHighlights}
                        primaryHighlight={primaryHighlight}
                        onPrimaryChange={onPrimaryChange}
                    />
                ) : (
                    <Alert
                        color="yellow"
                        icon={<IconAlertTriangle size={16} />}
                        role="alert"
                        data-testid="traceability-board-drilldown-no-product"
                    >
                        {t("traceability.board.drilldown.missingProductName")}
                    </Alert>
                )}

                <SimpleGrid
                    cols={{ base: 1, xl: Math.max(1, drillableStages.length) }}
                    spacing="md"
                >
                    {drillableStages.map((stage) => {
                        const entry = perStage.get(stage.sourceId);
                        const isActive = stage.sourceId === activeSourceId;
                        return (
                            <Card
                                key={stage.sourceId}
                                withBorder
                                padding="sm"
                                radius="sm"
                                data-testid={`traceability-board-drilldown-table-${stage.sourceId}`}
                                data-active={isActive ? "true" : undefined}
                                style={{
                                    borderColor: isActive
                                        ? inferViewerStage(stage.sourceId) === "pre"
                                            ? "var(--mantine-color-grape-6)"
                                            : "var(--mantine-color-red-6)"
                                        : undefined,
                                }}
                            >
                                <FailedObjectsTable
                                    objects={entry?.failed?.objects ?? []}
                                    heading={stage.sourceName}
                                    stageTint={inferViewerStage(stage.sourceId)}
                                    isLoading={entry?.failedIsLoading ?? false}
                                    error={entry?.failedError ?? null}
                                    primaryHighlight={
                                        isActive ? primaryHighlight : null
                                    }
                                    onRowClick={(h) => {
                                        // Clicking a row on an inactive stage
                                        // promotes that stage to active so the
                                        // matching marker becomes visible on
                                        // the shared viewer.
                                        if (!isActive) {
                                            onActiveSourceChange(stage.sourceId);
                                        }
                                        onPrimaryChange(h);
                                    }}
                                    testIdRoot={`traceability-board-failed-${stage.sourceId}`}
                                />
                            </Card>
                        );
                    })}
                </SimpleGrid>
            </Stack>
        </Card>
    );
}
