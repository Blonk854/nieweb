import { useEffect, useMemo, useState, lazy, Suspense } from "react";
import {
    Alert,
    Anchor,
    Badge,
    Button,
    Card,
    Group,
    Loader,
    MultiSelect,
    NumberInput,
    SegmentedControl,
    Select,
    Stack,
    Switch,
    Text,
    Title,
} from "@mantine/core";
import { DateTimePicker } from "@mantine/dates";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import {
    IconAlertTriangle,
    IconDownload,
    IconEye,
    IconPrinter,
    IconX,
} from "@tabler/icons-react";
import "@mantine/dates/styles.css";
import {
    fetchActiveFilters,
    fetchMachines,
    fetchProducts,
    fetchSources,
    type SourceInfo,
} from "../api/sources";
import {
    paretoExportUrl,
    runParetoReport,
    type ParetoResult,
    type ParetoRow,
} from "../api/pareto";
import {
    PARETO_AXES,
    PARETO_NUMERATORS,
    PARETO_OPPORTUNITIES,
    PARETO_WEIGHTS,
    SKIP_EXCLUSIONS,
    SKIP_STATUS_VALUES,
    pickDefaultSourceId,
    paretoDrillInto,
    withoutDefectBit,
    withoutNumericFilter,
    withoutStringFilter,
    type ParetoAxis,
    type ParetoNumerator,
    type ParetoOpportunity,
    type ParetoWeight,
    type ParetoSearch,
    type SkipExclusion,
    type SkipStatus,
} from "./pareto.search";
import { DataTable, type Column } from "../components/DataTable";
import { downloadCsv, rowsToCsv } from "../components/csvExport";
import { downloadWithAuth } from "../api/download";
import { PdfPreviewModal } from "../components/PdfPreviewModal";
import {
    instantIsoToWallClock,
    wallClockToInstantIso,
} from "../i18n/zoneConverters";
import { resolveTimeZone, usePreferencesStore } from "../state/preferences";

// Chart is loaded on-demand (echarts is ~1.1 MB gzipped). Splitting it
// out keeps the initial bundle small; the chunk is only fetched when
// a user actually runs a Pareto report.
const ParetoChart = lazy(() =>
    import("../charts/ParetoChart").then((m) => ({ default: m.ParetoChart })),
);

/**
 * Pareto report route. Renders a filter form (source, window, axis,
 * numerator, opportunity, top-N, defect-bit chips) and, once the
 * filter is complete, a bar + cumulative-percent chart plus a table
 * of the underlying rows.
 *
 * URL-first design: every filter lives in the search params (see
 * `router.ts::paretoRoute.validateSearch`) so the whole report is
 * shareable / bookmarkable / drill-in-reversible via the browser
 * back button. Clicking a bar on the Defect axis appends the bit to
 * `defectBits` and re-fetches — the user typically then switches
 * axis to Product / PartNumber to see which parts contain that bit.
 */
export function ParetoRoute() {
    const { t } = useTranslation();
    const rawSearch = useSearch({ strict: false });
    const search = rawSearch as ParetoSearch;
    const navigate = useNavigate();

    const sourcesQuery = useQuery({ queryKey: ["sources"], queryFn: fetchSources });
    const sources = useMemo(() => sourcesQuery.data ?? [], [sourcesQuery.data]);

    // Interpret naive wall-clock pickers in the user's configured time
    // zone (Settings -> Timezone) rather than UTC.
    const timeZone = resolveTimeZone(
        usePreferencesStore((s) => s.timeZone),
    );

    const [form, setForm] = useState<FormState>(() => searchToForm(search, timeZone));

    const effectiveSourceId = form.sourceId ?? pickDefaultSourceId(sources);

    const machinesQuery = useQuery({
        queryKey: ["machines", effectiveSourceId],
        queryFn: () => fetchMachines(effectiveSourceId!),
        enabled: Boolean(effectiveSourceId),
    });
    const productsQuery = useQuery({
        queryKey: ["products", effectiveSourceId],
        queryFn: () => fetchProducts(effectiveSourceId!),
        enabled: Boolean(effectiveSourceId),
    });

    // Cascading filters: as the user picks a From/To window, fetch the
    // distinct (machine, product) pairs that actually ran in it. The
    // Machines dropdown then lists only machines active in the window, and
    // the Products dropdown only products that ran on the selected
    // machine(s). Debounced so editing the date pickers doesn't hammer the
    // production DB — one query per settled window, then all narrowing is
    // client-side.
    const pendingWindow = useMemo(() => {
        if (!effectiveSourceId || !form.from || !form.to) return null;
        const startUtc = wallClockToInstantIso(form.from, timeZone) ?? undefined;
        const endUtc = wallClockToInstantIso(form.to, timeZone) ?? undefined;
        if (!startUtc || !endUtc || startUtc >= endUtc) return null;
        return { sourceId: effectiveSourceId, startUtc, endUtc };
    }, [effectiveSourceId, form.from, form.to, timeZone]);

    const [debouncedWindow, setDebouncedWindow] = useState(pendingWindow);
    useEffect(() => {
        const handle = setTimeout(() => setDebouncedWindow(pendingWindow), 400);
        return () => clearTimeout(handle);
    }, [pendingWindow]);

    const activeFiltersQuery = useQuery({
        queryKey: [
            "active-filters",
            debouncedWindow?.sourceId,
            debouncedWindow?.startUtc,
            debouncedWindow?.endUtc,
        ],
        queryFn: () =>
            fetchActiveFilters(
                debouncedWindow!.sourceId,
                debouncedWindow!.startUtc,
                debouncedWindow!.endUtc,
            ),
        enabled: Boolean(debouncedWindow),
        staleTime: 60_000,
    });

    // Distinct machines that ran in the window. `null` = no window settled
    // yet, so the dropdown falls back to the full catalogue.
    const activeMachineIds = useMemo(() => {
        const pairs = activeFiltersQuery.data?.pairs;
        return pairs ? new Set(pairs.map((p) => p.machineId)) : null;
    }, [activeFiltersQuery.data]);

    // Products that ran in the window, narrowed to the selected machine(s).
    // Recomputes client-side when the machine selection changes — no fetch.
    const activeProductIds = useMemo(() => {
        const pairs = activeFiltersQuery.data?.pairs;
        if (!pairs) return null;
        const relevant =
            form.machineIds.length > 0
                ? pairs.filter((p) => form.machineIds.includes(p.machineId))
                : pairs;
        return new Set(relevant.map((p) => p.productId));
    }, [activeFiltersQuery.data, form.machineIds]);

    const reportEnabled = Boolean(
        search.sourceId && search.startUtc && search.endUtc && search.axis,
    );
    const reportQuery = useQuery({
        queryKey: ["pareto", search],
        queryFn: () => runParetoReport(search),
        enabled: reportEnabled,
    });

    const activeSource = useMemo<SourceInfo | undefined>(
        () => sources.find((s) => s.id === search.sourceId),
        [sources, search.sourceId],
    );

    // F15 - PDF preview modal state.
    const [pdfPreviewOpen, setPdfPreviewOpen] = useState(false);
    const pdfPreviewUrl = reportEnabled ? paretoExportUrl(search, "pdf") : null;
    const pdfFallbackFilename = `pareto-${search.sourceId ?? "source"}.pdf`;

    const canSubmit = Boolean(effectiveSourceId && form.from && form.to && form.axis);

    // Exports must carry the bearer token, so a plain <a href> 401s.
    // Fetch the file with auth and trigger a blob download instead.
    async function downloadExport(format: "csv" | "xlsx" | "pdf") {
        if (!reportEnabled) return;
        const stem = `pareto-${search.sourceId ?? "source"}-${search.startUtc?.slice(0, 10) ?? ""}`;
        try {
            await downloadWithAuth(paretoExportUrl(search, format), `${stem}.${format}`);
        } catch {
            // downloadWithAuth clears the session on 401; other errors are
            // transient. The report card already surfaces API failures.
        }
    }

    function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault();
        if (!canSubmit) return;
        const next: ParetoSearch = formToSearch({
            ...form,
            sourceId: effectiveSourceId,
        }, timeZone);
        void navigate({
            to: "/report/pareto",
            search: next,
            replace: false,
        });
    }

    function handleReset() {
        setForm(emptyForm());
        void navigate({
            to: "/report/pareto",
            search: {} as ParetoSearch,
            replace: false,
        });
    }

    // Chart bar click = drill-down. On the Defect axis we append the
    // clicked bit and stay; on a category axis (Product / AOI machine /
    // reference designator / part number / JEDEC) we add the clicked
    // bucket to the matching narrowing filter and advance to the Defect
    // axis so the user immediately sees the defect breakdown inside that
    // bucket. Day / Shift bars and the Others bucket are not drillable.
    function handleBarClick(row: ParetoRow) {
        if (!row.groupKey) return;
        const next = paretoDrillInto(search, row.groupKey);
        if (next === search) return;
        // Keep local form state in sync so the axis selector and filter
        // chips update immediately (the URL is the source of truth).
        setForm((prev) => ({
            ...prev,
            axis: next.axis ?? "Defect",
            defectBits: next.defectBits ?? [],
            productIds: next.productIds ?? [],
            machineIds: next.machineIds ?? [],
            topologies: next.topologies ?? [],
            partNumbers: next.partNumbers ?? [],
            jedecNames: next.jedecNames ?? [],
        }));
        void navigate({ to: "/report/pareto", search: next, replace: false });
    }

    function removeDefectBit(bit: number) {
        const next = withoutDefectBit(search, bit);
        setForm((prev) => ({ ...prev, defectBits: next.defectBits ?? [] }));
        void navigate({ to: "/report/pareto", search: next, replace: false });
    }

    function removeNumericFilter(key: "productIds" | "machineIds", value: number) {
        const next = withoutNumericFilter(search, key, value);
        setForm((prev) => ({ ...prev, [key]: next[key] ?? [] }));
        void navigate({ to: "/report/pareto", search: next, replace: false });
    }

    function removeStringFilter(
        key: "topologies" | "partNumbers" | "jedecNames",
        value: string,
    ) {
        const next = withoutStringFilter(search, key, value);
        setForm((prev) => ({ ...prev, [key]: next[key] ?? [] }));
        void navigate({ to: "/report/pareto", search: next, replace: false });
    }

    const productLabel = (id: number) =>
        productsQuery.data?.find((p) => p.id === id)?.name || `#${id}`;
    const machineLabel = (id: number) =>
        machinesQuery.data?.find((m) => m.id === id)?.name || `#${id}`;

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("pareto.title")}</Title>
                <Text c="dimmed">{t("pareto.subtitle")}</Text>
            </Stack>

            <Card
                withBorder
                padding="lg"
                radius="md"
                component="form"
                onSubmit={handleSubmit}
            >
                <Title order={4} mb="sm">
                    {t("pareto.filters.heading")}
                </Title>

                <Stack gap="md">
                    <Group grow align="flex-end">
                        <DateTimePicker
                            label={t("pareto.filters.from")}
                            value={form.from}
                            onChange={(value) =>
                                setForm((prev) => ({ ...prev, from: value }))
                            }
                            valueFormat="YYYY-MM-DD HH:mm"
                            clearable
                            required
                        />
                        <DateTimePicker
                            label={t("pareto.filters.to")}
                            value={form.to}
                            onChange={(value) =>
                                setForm((prev) => ({ ...prev, to: value }))
                            }
                            valueFormat="YYYY-MM-DD HH:mm"
                            clearable
                            required
                        />
                        <Select
                            label={t("pareto.filters.source")}
                            placeholder={t("pareto.filters.sourcePlaceholder")}
                            data={sources.map((s) => ({
                                value: s.id,
                                label: s.available
                                    ? s.displayName
                                    : `${s.displayName} (offline)`,
                            }))}
                            value={effectiveSourceId ?? null}
                            onChange={(value) =>
                                setForm((prev) => ({
                                    ...prev,
                                    sourceId: value ?? undefined,
                                    machineIds: [],
                                    productIds: [],
                                }))
                            }
                            required
                            allowDeselect={false}
                            searchable
                        />
                        <Select
                            label={t("pareto.filters.axis")}
                            data={PARETO_AXES.map((a) => ({
                                value: a,
                                label: t(`pareto.axis.${a}`),
                            }))}
                            value={form.axis}
                            onChange={(value) =>
                                setForm((prev) => ({
                                    ...prev,
                                    axis: (value ?? "Defect") as ParetoAxis,
                                }))
                            }
                            required
                            allowDeselect={false}
                        />
                    </Group>

                    <Group grow align="flex-end">
                        <MultiSelect
                            label={t("pareto.filters.machines")}
                            placeholder={t("pareto.filters.machinesPlaceholder")}
                            data={(machinesQuery.data ?? [])
                                .filter(
                                    (m) =>
                                        !activeMachineIds ||
                                        activeMachineIds.has(m.id) ||
                                        form.machineIds.includes(m.id),
                                )
                                .map((m) => ({
                                    value: String(m.id),
                                    label: `${m.name} (${m.typeName})`,
                                }))}
                            value={(form.machineIds ?? []).map(String)}
                            onChange={(vals) =>
                                setForm((prev) => ({
                                    ...prev,
                                    machineIds: vals.map(Number).filter(Number.isFinite),
                                }))
                            }
                            disabled={!effectiveSourceId || machinesQuery.isPending}
                            searchable
                            clearable
                        />
                        <MultiSelect
                            label={t("pareto.filters.products")}
                            placeholder={t("pareto.filters.productsPlaceholder")}
                            data={(productsQuery.data ?? [])
                                .filter(
                                    (p) =>
                                        !activeProductIds ||
                                        activeProductIds.has(p.id) ||
                                        form.productIds.includes(p.id),
                                )
                                .map((p) => ({
                                    value: String(p.id),
                                    label: p.revision
                                        ? `${p.name || `#${p.id}`} — ${p.revision}`
                                        : p.name || `#${p.id}`,
                                }))}
                            value={(form.productIds ?? []).map(String)}
                            onChange={(vals) =>
                                setForm((prev) => ({
                                    ...prev,
                                    productIds: vals.map(Number).filter(Number.isFinite),
                                }))
                            }
                            disabled={!effectiveSourceId || productsQuery.isPending}
                            searchable
                            clearable
                        />
                    </Group>

                    <Group grow align="flex-start">
                        <Select
                            label={t("pareto.filters.numerator")}
                            data={PARETO_NUMERATORS.map((n) => ({
                                value: n,
                                label: t(`pareto.numerator.${n}`),
                            }))}
                            value={form.numerator}
                            onChange={(value) =>
                                setForm((prev) => ({
                                    ...prev,
                                    numerator: (value ?? "Real") as ParetoNumerator,
                                }))
                            }
                            allowDeselect={false}
                        />
                        <Select
                            label={t("pareto.filters.opportunity")}
                            data={PARETO_OPPORTUNITIES.map((o) => ({
                                value: o,
                                label: t(`pareto.opportunity.${o}`),
                            }))}
                            value={form.opportunity}
                            onChange={(value) =>
                                setForm((prev) => ({
                                    ...prev,
                                    opportunity: (value ?? "All") as ParetoOpportunity,
                                }))
                            }
                            allowDeselect={false}
                        />
                        <Select
                            label={t("pareto.filters.weight")}
                            description={t("pareto.filters.weightHint")}
                            inputWrapperOrder={["label", "input", "description", "error"]}
                            data={PARETO_WEIGHTS.map((w) => ({
                                value: w,
                                label: t(`pareto.weight.${w}`),
                            }))}
                            value={form.weight}
                            onChange={(value) =>
                                setForm((prev) => ({
                                    ...prev,
                                    weight: (value ?? "Count") as ParetoWeight,
                                }))
                            }
                            allowDeselect={false}
                        />
                        <NumberInput
                            label={t("pareto.filters.topN")}
                            description={t("pareto.filters.topNHint")}
                            inputWrapperOrder={["label", "input", "description", "error"]}
                            min={1}
                            max={100}
                            value={form.topN ?? ""}
                            onChange={(value) =>
                                setForm((prev) => ({
                                    ...prev,
                                    topN: typeof value === "number" && value > 0 ? value : undefined,
                                }))
                            }
                        />
                        <NumberInput
                            label={t("pareto.filters.vitalFewThreshold")}
                            description={t("pareto.filters.vitalFewThresholdHint")}
                            inputWrapperOrder={["label", "input", "description", "error"]}
                            min={0}
                            max={100}
                            step={1}
                            value={form.vitalFewThreshold ?? ""}
                            onChange={(value) =>
                                setForm((prev) => ({
                                    ...prev,
                                    vitalFewThreshold:
                                        typeof value === "number" ? value : undefined,
                                }))
                            }
                        />
                    </Group>

                    <Group align="flex-start" gap="lg">
                        <Stack gap={4}>
                            <Text size="sm" fw={500}>
                                {t("pareto.filters.skipExclusion")}
                            </Text>
                            <SegmentedControl
                                data={SKIP_EXCLUSIONS.map((s) => ({
                                    value: s,
                                    label: t(`pareto.skipExclusion.${s}`),
                                }))}
                                value={form.skipExclusion}
                                onChange={(value) =>
                                    setForm((prev) => ({
                                        ...prev,
                                        skipExclusion: value as SkipExclusion,
                                    }))
                                }
                            />
                            <Text size="xs" c="dimmed" maw={320}>
                                {t("pareto.filters.skipExclusionHint")}
                            </Text>
                        </Stack>
                        <Stack gap="sm">
                            <MultiSelect
                                label={t("pareto.filters.skipStatuses")}
                                description={t("pareto.filters.skipStatusesHint")}
                                inputWrapperOrder={["label", "input", "description", "error"]}
                                placeholder={t("pareto.filters.skipStatusesPlaceholder")}
                                data={SKIP_STATUS_VALUES.map((c) => ({
                                    value: c,
                                    label: t(`skipSummary.classLabel.${c}`),
                                }))}
                                value={form.skipStatuses}
                                onChange={(vals) =>
                                    setForm((prev) => ({
                                        ...prev,
                                        skipStatuses: vals as SkipStatus[],
                                    }))
                                }
                                clearable
                                style={{ minWidth: 260 }}
                            />
                        </Stack>
                    </Group>

                    <Switch
                        label={t("pareto.filters.excludeNogo")}
                        checked={form.excludeNogo}
                        onChange={(e) =>
                            setForm((prev) => ({
                                ...prev,
                                excludeNogo: e.currentTarget.checked,
                            }))
                        }
                    />

                    {(() => {
                        const chips: {
                            key: string;
                            label: string;
                            color: string;
                            onRemove: () => void;
                        }[] = [
                            ...(search.defectBits ?? []).map((bit) => ({
                                key: `d-${bit}`,
                                label: t("pareto.filters.defectBitChip", { bit }),
                                color: "red",
                                onRemove: () => removeDefectBit(bit),
                            })),
                            ...(search.productIds ?? []).map((id) => ({
                                key: `p-${id}`,
                                label: `${t("pareto.axis.Product")}: ${productLabel(id)}`,
                                color: "blue",
                                onRemove: () => removeNumericFilter("productIds", id),
                            })),
                            ...(search.machineIds ?? []).map((id) => ({
                                key: `m-${id}`,
                                label: `${t("pareto.axis.AoiMachine")}: ${machineLabel(id)}`,
                                color: "blue",
                                onRemove: () => removeNumericFilter("machineIds", id),
                            })),
                            ...(search.topologies ?? []).map((v) => ({
                                key: `t-${v}`,
                                label: `${t("pareto.axis.ReferenceDesignator")}: ${v}`,
                                color: "grape",
                                onRemove: () => removeStringFilter("topologies", v),
                            })),
                            ...(search.partNumbers ?? []).map((v) => ({
                                key: `pn-${v}`,
                                label: `${t("pareto.axis.PartNumber")}: ${v}`,
                                color: "grape",
                                onRemove: () => removeStringFilter("partNumbers", v),
                            })),
                            ...(search.jedecNames ?? []).map((v) => ({
                                key: `j-${v}`,
                                label: `${t("pareto.axis.Jedec")}: ${v}`,
                                color: "grape",
                                onRemove: () => removeStringFilter("jedecNames", v),
                            })),
                        ];
                        if (chips.length === 0) return null;
                        return (
                            <Group gap="xs" align="center">
                                <Text size="sm" fw={500}>
                                    {t("pareto.filters.activeFiltersLabel")}:
                                </Text>
                                {chips.map((c) => (
                                    <Badge
                                        key={c.key}
                                        variant="light"
                                        color={c.color}
                                        rightSection={
                                            <IconX
                                                size={12}
                                                role="button"
                                                aria-label={t("pareto.filters.removeFilter", {
                                                    label: c.label,
                                                })}
                                                style={{ cursor: "pointer" }}
                                                onClick={c.onRemove}
                                            />
                                        }
                                    >
                                        {c.label}
                                    </Badge>
                                ))}
                            </Group>
                        );
                    })()}

                    <Group justify="space-between" className="no-print">
                        <Group>
                            <Button type="submit" disabled={!canSubmit}>
                                {t("pareto.filters.submit")}
                            </Button>
                            <Button variant="subtle" onClick={handleReset} type="button">
                                {t("pareto.filters.reset")}
                            </Button>
                            <Button
                                variant="default"
                                leftSection={<IconPrinter size={16} />}
                                onClick={() => window.print()}
                                type="button"
                                disabled={!reportEnabled}
                            >
                                {t("pareto.filters.print")}
                            </Button>
                        </Group>
                        <Group>
                            <Anchor
                                component="button"
                                type="button"
                                onClick={() => void downloadExport("csv")}
                                aria-disabled={!reportEnabled}
                                data-disabled={!reportEnabled || undefined}
                                disabled={!reportEnabled}
                            >
                                <Group gap={4}>
                                    <IconDownload size={16} />
                                    <Text size="sm">
                                        {t("pareto.filters.exportCsv")}
                                    </Text>
                                </Group>
                            </Anchor>
                            <Anchor
                                component="button"
                                type="button"
                                onClick={() => void downloadExport("xlsx")}
                                aria-disabled={!reportEnabled}
                                data-disabled={!reportEnabled || undefined}
                                disabled={!reportEnabled}
                            >
                                <Group gap={4}>
                                    <IconDownload size={16} />
                                    <Text size="sm">
                                        {t("pareto.filters.exportXlsx")}
                                    </Text>
                                </Group>
                            </Anchor>
                            <Anchor
                                component="button"
                                type="button"
                                onClick={() => void downloadExport("pdf")}
                                aria-disabled={!reportEnabled}
                                data-disabled={!reportEnabled || undefined}
                                disabled={!reportEnabled}
                            >
                                <Group gap={4}>
                                    <IconDownload size={16} />
                                    <Text size="sm">
                                        {t("pareto.filters.exportPdf")}
                                    </Text>
                                </Group>
                            </Anchor>
                            <Anchor
                                component="button"
                                type="button"
                                onClick={() => setPdfPreviewOpen(true)}
                                aria-disabled={!reportEnabled}
                                data-disabled={!reportEnabled || undefined}
                                disabled={!reportEnabled}
                                data-testid="pareto-preview-pdf"
                            >
                                <Group gap={4}>
                                    <IconEye size={16} />
                                    <Text size="sm">
                                        {t("common.pdfPreview.openAction")}
                                    </Text>
                                </Group>
                            </Anchor>
                        </Group>
                    </Group>
                </Stack>
            </Card>

            <ResultsCard
                enabled={reportEnabled}
                isPending={reportQuery.isPending}
                isFetching={reportQuery.isFetching}
                data={reportQuery.data}
                error={reportQuery.error}
                source={activeSource}
                axis={search.axis ?? "Defect"}
                vitalFewThresholdPercent={search.vitalFewThreshold ?? 80}
                onBarClick={handleBarClick}
            />

            <PdfPreviewModal
                opened={pdfPreviewOpen}
                onClose={() => setPdfPreviewOpen(false)}
                pdfUrl={pdfPreviewUrl}
                fallbackFilename={pdfFallbackFilename}
            />
        </Stack>
    );
}

// ---------------------------------------------------------------
// Local form <-> URL search shape converters.
// ---------------------------------------------------------------

type FormState = {
    sourceId: string | undefined;
    from: string | null;
    to: string | null;
    axis: ParetoAxis;
    numerator: ParetoNumerator;
    opportunity: ParetoOpportunity;
    weight: ParetoWeight;
    topN: number | undefined;
    vitalFewThreshold: number | undefined;
    machineIds: number[];
    productIds: number[];
    defectBits: number[];
    topologies: string[];
    partNumbers: string[];
    jedecNames: string[];
    skipExclusion: SkipExclusion;
    skipStatuses: SkipStatus[];
    excludeNogo: boolean;
};

function emptyForm(): FormState {
    return {
        sourceId: undefined,
        from: null,
        to: null,
        axis: "Defect",
        numerator: "Real",
        opportunity: "All",
        weight: "Count",
        topN: undefined,
        vitalFewThreshold: undefined,
        machineIds: [],
        productIds: [],
        defectBits: [],
        topologies: [],
        partNumbers: [],
        jedecNames: [],
        skipExclusion: "Raw",
        skipStatuses: [],
        excludeNogo: false,
    };
}

function searchToForm(s: ParetoSearch, timeZone: string): FormState {
    return {
        sourceId: s.sourceId,
        from: s.startUtc ? instantIsoToWallClock(s.startUtc, timeZone) : null,
        to: s.endUtc ? instantIsoToWallClock(s.endUtc, timeZone) : null,
        axis: s.axis ?? "Defect",
        numerator: s.numerator ?? "Real",
        opportunity: s.opportunity ?? "All",
        weight: s.weight ?? "Count",
        topN: s.topN,
        vitalFewThreshold: s.vitalFewThreshold,
        machineIds: s.machineIds ?? [],
        productIds: s.productIds ?? [],
        defectBits: s.defectBits ?? [],
        topologies: s.topologies ?? [],
        partNumbers: s.partNumbers ?? [],
        jedecNames: s.jedecNames ?? [],
        skipExclusion: s.skipExclusion ?? "Raw",
        skipStatuses: s.skipStatuses ?? [],
        excludeNogo: s.excludeNogo ?? false,
    };
}

function formToSearch(f: FormState, timeZone: string): ParetoSearch {
    return {
        sourceId: f.sourceId,
        startUtc: f.from
            ? (wallClockToInstantIso(f.from, timeZone) ?? undefined)
            : undefined,
        endUtc: f.to
            ? (wallClockToInstantIso(f.to, timeZone) ?? undefined)
            : undefined,
        axis: f.axis,
        numerator: f.numerator,
        opportunity: f.opportunity,
        weight: f.weight,
        topN: f.topN,
        vitalFewThreshold: f.vitalFewThreshold,
        machineIds: f.machineIds.length > 0 ? f.machineIds : undefined,
        productIds: f.productIds.length > 0 ? f.productIds : undefined,
        defectBits: f.defectBits.length > 0 ? f.defectBits : undefined,
        topologies: f.topologies.length > 0 ? f.topologies : undefined,
        partNumbers: f.partNumbers.length > 0 ? f.partNumbers : undefined,
        jedecNames: f.jedecNames.length > 0 ? f.jedecNames : undefined,
        skipExclusion: f.skipExclusion === "Clean" ? "Clean" : undefined,
        skipStatuses: f.skipStatuses.length > 0 ? f.skipStatuses : undefined,
        excludeNogo: f.excludeNogo ? true : undefined,
    };
}

// ---------------------------------------------------------------
// Results panel.
// ---------------------------------------------------------------

// Axes whose bars support click-to-drill. Day / Shift bars map to a
// time bucket, not a narrowing filter, so they stay non-interactive.
const DRILLABLE_AXES: ReadonlySet<ParetoAxis> = new Set<ParetoAxis>([
    "Defect",
    "Product",
    "AoiMachine",
    "ReferenceDesignator",
    "PartNumber",
    "Jedec",
]);

function ResultsCard(props: {
    enabled: boolean;
    isPending: boolean;
    isFetching: boolean;
    data: ParetoResult | undefined;
    error: unknown;
    source: SourceInfo | undefined;
    axis: ParetoAxis;
    vitalFewThresholdPercent: number;
    onBarClick: (row: ParetoRow) => void;
}) {
    const { t } = useTranslation();
    const {
        enabled,
        isPending,
        isFetching,
        data,
        error,
        source,
        axis,
        vitalFewThresholdPercent,
        onBarClick,
    } = props;

    if (!enabled) {
        return null;
    }

    return (
        <Card withBorder padding="lg" radius="md">
            <Group justify="space-between" mb="sm">
                <Title order={4}>{t("pareto.results.heading")}</Title>
                {isFetching && <Loader size="xs" />}
            </Group>

            {error ? (
                <Alert
                    color="red"
                    icon={<IconAlertTriangle size={18} />}
                    title={t("pareto.results.errorTitle")}
                    role="alert"
                >
                    {error instanceof Error ? error.message : String(error)}
                </Alert>
            ) : null}

            {isPending && !error && <Loader />}

            {data && (
                <Stack gap="md">
                    <Group gap="lg">
                        <Text>
                            <strong>{t("pareto.results.source")}:</strong>{" "}
                            {source?.displayName ?? data.source.displayName}
                        </Text>
                        <Text>
                            <strong>{t("pareto.results.window")}:</strong>{" "}
                            {formatWindow(data.window.startUtc, data.window.endUtcExclusive)}
                        </Text>
                        <Text>
                            <strong>{t("pareto.results.axis")}:</strong>{" "}
                            {t(`pareto.axis.${data.axis}`)}
                        </Text>
                    </Group>

                    <Group gap="lg">
                        <Text size="sm">
                            <strong>{t("pareto.results.totalDefects")}:</strong>{" "}
                            {data.overall.defectBitCount.toLocaleString()}
                        </Text>
                        <Text size="sm">
                            <strong>{t("pareto.results.totalOpportunities")}:</strong>{" "}
                            {data.overall.opportunityCount.toLocaleString()}
                        </Text>
                        <Text size="sm">
                            <strong>{t("pareto.results.overallDpmoPpm")}:</strong>{" "}
                            {Math.round(data.overall.dpmoPpm).toLocaleString()}
                        </Text>
                    </Group>

                    {data.rows.length === 0 && !data.othersBucket ? (
                        <Text c="dimmed">{t("pareto.results.noRows")}</Text>
                    ) : (
                        <>
                            <Suspense fallback={<Loader size="sm" />}>
                                <ParetoChart
                                    rows={data.rows}
                                    othersBucket={data.othersBucket}
                                    axis={axis}
                                    vitalFewThresholdPercent={vitalFewThresholdPercent}
                                    onBarClick={
                                        DRILLABLE_AXES.has(axis) ? onBarClick : undefined
                                    }
                                />
                            </Suspense>
                            <ParetoTable rows={data.rows} othersBucket={data.othersBucket} />
                        </>
                    )}
                </Stack>
            )}
        </Card>
    );
}

function formatWindow(startIso: string, endIso: string): string {
    const start = new Date(startIso);
    const end = new Date(endIso);
    return `${start.toISOString().slice(0, 16).replace("T", " ")} → ${end
        .toISOString()
        .slice(0, 16)
        .replace("T", " ")} UTC`;
}

/**
 * Table view of the Pareto rows (server-sorted worst-first). The
 * Others bucket, when present, is appended as the final row so the
 * table shows the same eleven-row picture as the chart when
 * `TopN = 10`.
 */
function ParetoTable({
    rows,
    othersBucket,
}: {
    rows: ParetoRow[];
    othersBucket: ParetoRow | null;
}) {
    const { t } = useTranslation();
    const allRows = useMemo<ParetoRow[]>(
        () => (othersBucket ? [...rows, othersBucket] : rows),
        [rows, othersBucket],
    );
    const columns = useMemo<Column<ParetoRow>[]>(
        () => [
            {
                key: "groupName",
                header: t("pareto.results.groupName"),
                accessor: (r) => r.groupName ?? r.groupKey ?? "—",
                hideable: false,
            },
            {
                key: "defectCount",
                header: t("pareto.results.defectCount"),
                accessor: (r) => r.defectCount,
                align: "right",
            },
            {
                key: "opportunityCount",
                header: t("pareto.results.opportunityCount"),
                accessor: (r) => r.opportunityCount,
                align: "right",
            },
            {
                key: "dpmoPpm",
                header: t("pareto.results.dpmoPpm"),
                accessor: (r) => Math.round(r.dpmoPpm),
                align: "right",
            },
            {
                key: "defectSharePercent",
                header: t("pareto.results.defectSharePercent"),
                accessor: (r) => r.defectSharePercent,
                formatter: (v) => (typeof v === "number" ? `${v.toFixed(1)}%` : ""),
                csvFormatter: (v) => (typeof v === "number" ? v.toFixed(2) : ""),
                align: "right",
            },
            {
                key: "cumulativePercent",
                header: t("pareto.results.cumulativePercent"),
                accessor: (r) => r.cumulativePercent,
                formatter: (v) => (typeof v === "number" ? `${v.toFixed(1)}%` : ""),
                csvFormatter: (v) => (typeof v === "number" ? v.toFixed(2) : ""),
                align: "right",
            },
            {
                key: "isVitalFew",
                header: t("pareto.results.isVitalFew"),
                accessor: (r) => (r.isVitalFew ? "yes" : "no"),
                align: "center",
            },
        ],
        [t],
    );

    return (
        <DataTable
            columns={columns}
            rows={allRows}
            rowKey={(r) => r.groupKey ?? "OTHERS"}
            onExportCsv={(visibleRows, visibleColumns) => {
                downloadCsv(
                    `pareto-${new Date().toISOString().slice(0, 10)}.csv`,
                    rowsToCsv(visibleRows, visibleColumns),
                );
            }}
        />
    );
}
