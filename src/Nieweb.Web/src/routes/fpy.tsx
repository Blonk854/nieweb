import { useMemo, useState } from "react";
import {
    Alert,
    Anchor,
    Badge,
    Button,
    Card,
    Checkbox,
    Group,
    Loader,
    MultiSelect,
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
import { IconAlertTriangle, IconDownload, IconEye, IconPrinter } from "@tabler/icons-react";
import "@mantine/dates/styles.css";
import {
    fetchMachines,
    fetchProducts,
    fetchSources,
    type SourceInfo,
} from "../api/sources";
import {
    runFpyTableReport,
    fpyExportUrl,
    type FpyTableResult,
    type FpyTableRow,
} from "../api/fpy";
import {
    FPY_GRANULARITIES,
    FPY_GROUP_BYS,
    SKIP_EXCLUSIONS,
    SKIP_STATUS_VALUES,
    pickDefaultSourceId,
    type FpyGranularity,
    type FpyGroupBy,
    type FpySearch,
    type SkipExclusion,
    type SkipStatus,
} from "./fpy.search";
import { DataTable, type Column } from "../components/DataTable";
import { downloadCsv, rowsToCsv } from "../components/csvExport";
import { downloadWithAuth } from "../api/download";
import { PdfPreviewModal } from "../components/PdfPreviewModal";
import {
    instantIsoToWallClock,
    wallClockToInstantIso,
} from "../i18n/zoneConverters";
import { resolveTimeZone, usePreferencesStore } from "../state/preferences";

type FormState = {
    sourceId: string | undefined;
    from: string | null;
    to: string | null;
    granularity: FpyGranularity;
    groupBy: FpyGroupBy;
    machineIds: number[];
    productIds: number[];
    onlyLastInspection: boolean;
    skipExclusion: SkipExclusion;
    skipStatuses: SkipStatus[];
    excludeNogo: boolean;
};

/**
 * FPY table report route. Renders a filter form (source, window,
 * panel / board granularity, group-by axis, only-last-inspection,
 * skip toggle) and a table of per-group first-pass-yield rows plus a
 * grand-total, with the three Vieweb FPY flavours (AOI / diagnostic /
 * after-repair) side by side.
 *
 * The raw / clean skip toggle excludes skipped / empty boards; on
 * panel-level FPY it re-derives panel goodness from the surviving
 * non-skip boards (so an X-OUT'd empty board no longer drags FPY down).
 *
 * URL-first: the whole filter lives in the search params so the report
 * is shareable and bookmarkable.
 */
export function FpyRoute() {
    const { t } = useTranslation();
    const rawSearch = useSearch({ strict: false });
    const search = rawSearch as FpySearch;
    const navigate = useNavigate();

    const sourcesQuery = useQuery({ queryKey: ["sources"], queryFn: fetchSources });
    const sources = useMemo(() => sourcesQuery.data ?? [], [sourcesQuery.data]);

    const timeZone = resolveTimeZone(usePreferencesStore((s) => s.timeZone));

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

    const reportEnabled = Boolean(search.sourceId && search.startUtc && search.endUtc);
    const reportQuery = useQuery({
        queryKey: ["fpy-table", search],
        queryFn: () => runFpyTableReport(search),
        enabled: reportEnabled,
    });

    const activeSource = useMemo<SourceInfo | undefined>(
        () => sources.find((s) => s.id === search.sourceId),
        [sources, search.sourceId],
    );

    const [pdfPreviewOpen, setPdfPreviewOpen] = useState(false);
    const pdfPreviewUrl = reportEnabled ? fpyExportUrl(search, "pdf") : null;
    const pdfFallbackFilename = `fpy-${search.sourceId ?? "source"}.pdf`;

    const canSubmit = Boolean(effectiveSourceId && form.from && form.to);

    function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault();
        if (!canSubmit) return;
        const next = formToSearch({ ...form, sourceId: effectiveSourceId }, timeZone);
        void navigate({ to: "/report/fpy", search: next, replace: false });
    }

    function handleReset() {
        setForm(emptyForm());
        void navigate({ to: "/report/fpy", search: {} as FpySearch, replace: false });
    }

    async function downloadExport(format: "csv" | "xlsx" | "pdf") {
        if (!reportEnabled) return;
        const stem = `fpy-${search.sourceId ?? "source"}-${search.startUtc?.slice(0, 10) ?? ""}`;
        try {
            await downloadWithAuth(fpyExportUrl(search, format), `${stem}.${format}`);
        } catch {
            // downloadWithAuth clears the session on 401; the report card
            // already surfaces API failures.
        }
    }

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("fpy.title")}</Title>
                <Text c="dimmed">{t("fpy.subtitle")}</Text>
            </Stack>

            <Card withBorder padding="lg" radius="md" component="form" onSubmit={handleSubmit}>
                <Title order={4} mb="sm">
                    {t("fpy.filters.heading")}
                </Title>

                <Stack gap="md">
                    <Group grow align="flex-end">
                        <DateTimePicker
                            label={t("fpy.filters.from")}
                            value={form.from}
                            onChange={(value) => setForm((prev) => ({ ...prev, from: value }))}
                            valueFormat="YYYY-MM-DD HH:mm"
                            clearable
                            required
                        />
                        <DateTimePicker
                            label={t("fpy.filters.to")}
                            value={form.to}
                            onChange={(value) => setForm((prev) => ({ ...prev, to: value }))}
                            valueFormat="YYYY-MM-DD HH:mm"
                            clearable
                            required
                        />
                        <Select
                            label={t("fpy.filters.source")}
                            placeholder={t("fpy.filters.sourcePlaceholder")}
                            data={sources.map((s) => ({
                                value: s.id,
                                label: s.available ? s.displayName : `${s.displayName} (offline)`,
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
                            label={t("fpy.filters.groupBy")}
                            data={FPY_GROUP_BYS.map((g) => ({
                                value: g,
                                label: t(`fpy.groupBy.${g}`),
                            }))}
                            value={form.groupBy}
                            onChange={(value) =>
                                setForm((prev) => ({
                                    ...prev,
                                    groupBy: (value ?? "AoiMachine") as FpyGroupBy,
                                }))
                            }
                            required
                            allowDeselect={false}
                        />
                    </Group>

                    <Group grow align="flex-end">
                        <MultiSelect
                            label={t("fpy.filters.machines")}
                            placeholder={t("fpy.filters.machinesPlaceholder")}
                            data={(machinesQuery.data ?? []).map((m) => ({
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
                            label={t("fpy.filters.products")}
                            placeholder={t("fpy.filters.productsPlaceholder")}
                            data={(productsQuery.data ?? []).map((p) => ({
                                value: String(p.id),
                                label: p.revision ? `${p.name || `#${p.id}`} — ${p.revision}` : p.name || `#${p.id}`,
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

                    <Group align="flex-start" gap="lg">
                        <Stack gap={4}>
                            <Text size="sm" fw={500}>
                                {t("fpy.filters.granularity")}
                            </Text>
                            <SegmentedControl
                                data={FPY_GRANULARITIES.map((g) => ({
                                    value: g,
                                    label: t(`fpy.granularity.${g}`),
                                }))}
                                value={form.granularity}
                                onChange={(value) =>
                                    setForm((prev) => ({
                                        ...prev,
                                        granularity: value as FpyGranularity,
                                    }))
                                }
                            />
                        </Stack>
                        <Stack gap={4}>
                            <Text size="sm" fw={500}>
                                {t("fpy.filters.skipExclusion")}
                            </Text>
                            <SegmentedControl
                                data={SKIP_EXCLUSIONS.map((s) => ({
                                    value: s,
                                    label: t(`fpy.skipExclusion.${s}`),
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
                                {t("fpy.filters.skipExclusionHint")}
                            </Text>
                        </Stack>
                        <Stack gap="sm">
                            <MultiSelect
                                label={t("fpy.filters.skipStatuses")}
                                description={t("fpy.filters.skipStatusesHint")}
                                inputWrapperOrder={["label", "input", "description", "error"]}
                                placeholder={t("fpy.filters.skipStatusesPlaceholder")}
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

                    <Group gap="xl" align="flex-start">
                        <Switch
                            label={t("fpy.filters.excludeNogo")}
                            description={t("fpy.filters.excludeNogoHint")}
                            checked={form.excludeNogo}
                            onChange={(event) =>
                                setForm((prev) => ({
                                    ...prev,
                                    excludeNogo: event.currentTarget.checked,
                                }))
                            }
                        />
                        <Checkbox
                            label={t("fpy.filters.onlyLastInspection")}
                            description={t("fpy.filters.onlyLastInspectionHint")}
                            checked={form.onlyLastInspection}
                            onChange={(event) =>
                                setForm((prev) => ({
                                    ...prev,
                                    onlyLastInspection: event.currentTarget.checked,
                                }))
                            }
                        />
                    </Group>

                    <Group justify="space-between" className="no-print">
                        <Group>
                            <Button type="submit" disabled={!canSubmit}>
                                {t("fpy.filters.submit")}
                            </Button>
                            <Button variant="subtle" onClick={handleReset} type="button">
                                {t("fpy.filters.reset")}
                            </Button>
                            <Button
                                variant="default"
                                leftSection={<IconPrinter size={16} />}
                                onClick={() => window.print()}
                                type="button"
                                disabled={!reportEnabled}
                            >
                                {t("fpy.filters.print")}
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
                                    <Text size="sm">{t("fpy.filters.exportCsv")}</Text>
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
                                    <Text size="sm">{t("fpy.filters.exportXlsx")}</Text>
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
                                    <Text size="sm">{t("fpy.filters.exportPdf")}</Text>
                                </Group>
                            </Anchor>
                            <Anchor
                                component="button"
                                type="button"
                                onClick={() => setPdfPreviewOpen(true)}
                                aria-disabled={!reportEnabled}
                                data-disabled={!reportEnabled || undefined}
                                disabled={!reportEnabled}
                                data-testid="fpy-preview-pdf"
                            >
                                <Group gap={4}>
                                    <IconEye size={16} />
                                    <Text size="sm">{t("common.pdfPreview.openAction")}</Text>
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

function ResultsCard(props: {
    enabled: boolean;
    isPending: boolean;
    isFetching: boolean;
    data: FpyTableResult | undefined;
    error: unknown;
    source: SourceInfo | undefined;
}) {
    const { t } = useTranslation();
    const { enabled, isPending, isFetching, data, error, source } = props;

    const columns = useMemo<Column<FpyTableRow>[]>(
        () => [
            {
                key: "group",
                header: t("fpy.results.group"),
                accessor: (r) => r.groupName ?? String(r.groupKey),
                formatter: (v) => (v == null || v === "" ? t("fpy.results.unassigned") : String(v)),
                hideable: false,
            },
            {
                key: "fpyAoi",
                header: t("fpy.results.fpyAoi"),
                accessor: (r) => r.kpi.fpyAoiPercent,
                formatter: (v) => (typeof v === "number" ? `${v.toFixed(2)}%` : ""),
                csvFormatter: (v) => (typeof v === "number" ? v.toFixed(2) : ""),
                align: "right",
            },
            {
                key: "fpyDiagnostic",
                header: t("fpy.results.fpyDiagnostic"),
                accessor: (r) => r.kpi.fpyDiagnosticPercent,
                formatter: (v) => (typeof v === "number" ? `${v.toFixed(2)}%` : ""),
                csvFormatter: (v) => (typeof v === "number" ? v.toFixed(2) : ""),
                align: "right",
            },
            {
                key: "fpyAfterRepair",
                header: t("fpy.results.fpyAfterRepair"),
                accessor: (r) => r.kpi.fpyAfterRepairPercent,
                formatter: (v) => (typeof v === "number" ? `${v.toFixed(2)}%` : ""),
                csvFormatter: (v) => (typeof v === "number" ? v.toFixed(2) : ""),
                align: "right",
            },
            {
                key: "inspected",
                header: t("fpy.results.inspected"),
                accessor: (r) => r.kpi.inspectedCount,
                formatter: (v) => (typeof v === "number" ? v.toLocaleString() : ""),
                align: "right",
            },
            {
                key: "faulty",
                header: t("fpy.results.faulty"),
                accessor: (r) => r.kpi.faultyCount,
                formatter: (v) => (typeof v === "number" ? v.toLocaleString() : ""),
                align: "right",
            },
        ],
        [t],
    );

    if (!enabled) {
        return null;
    }

    return (
        <Card withBorder padding="lg" radius="md">
            <Group justify="space-between" mb="sm">
                <Title order={4}>{t("fpy.results.heading")}</Title>
                {isFetching && <Loader size="xs" />}
            </Group>

            {error ? (
                <Alert
                    color="red"
                    icon={<IconAlertTriangle size={18} />}
                    title={t("fpy.results.errorTitle")}
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
                            <strong>{t("fpy.results.source")}:</strong>{" "}
                            {source?.displayName ?? data.source.displayName}
                        </Text>
                        <Text>
                            <strong>{t("fpy.results.window")}:</strong>{" "}
                            {formatWindow(data.window.startUtc, data.window.endUtcExclusive)}
                        </Text>
                    </Group>

                    <Group gap="lg">
                        <Badge color="grape" variant="light" size="lg">
                            {t(`fpy.granularity.${data.granularity}`)}
                        </Badge>
                        <Badge
                            color={data.skipExclusion === "Clean" ? "teal" : "gray"}
                            variant="light"
                            size="lg"
                        >
                            {t(`fpy.skipExclusion.${data.skipExclusion}`)}
                        </Badge>
                        {data.skipExclusion === "Clean" && (
                            <Text size="sm" c="dimmed">
                                {t("fpy.results.skipExcludedRows", {
                                    count: data.skipExcludedRows,
                                })}
                            </Text>
                        )}
                        <Badge color="blue" variant="light" size="lg">
                            {t("fpy.results.overallAoi")}:{" "}
                            {data.overall.fpyAoiPercent.toFixed(2)}%
                        </Badge>
                    </Group>

                    <DataTable
                        columns={columns}
                        rows={data.rows}
                        rowKey={(r) => String(r.groupKey)}
                        onExportCsv={(visibleRows, visibleColumns) => {
                            downloadCsv(
                                `fpy-${data.source.id}-${data.window.startUtc.slice(0, 10)}.csv`,
                                rowsToCsv(visibleRows, visibleColumns),
                            );
                        }}
                    />
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

function emptyForm(): FormState {
    return {
        sourceId: undefined,
        from: null,
        to: null,
        granularity: "Panel",
        groupBy: "AoiMachine",
        machineIds: [],
        productIds: [],
        onlyLastInspection: true,
        skipExclusion: "Raw",
        skipStatuses: [],
        excludeNogo: false,
    };
}

function searchToForm(s: FpySearch, timeZone: string): FormState {
    return {
        sourceId: s.sourceId,
        from: s.startUtc ? instantIsoToWallClock(s.startUtc, timeZone) : null,
        to: s.endUtc ? instantIsoToWallClock(s.endUtc, timeZone) : null,
        granularity: s.granularity ?? "Panel",
        groupBy: s.groupBy ?? "AoiMachine",
        machineIds: s.machineIds ?? [],
        productIds: s.productIds ?? [],
        onlyLastInspection: s.onlyLastInspection ?? true,
        skipExclusion: s.skipExclusion ?? "Raw",
        skipStatuses: s.skipStatuses ?? [],
        excludeNogo: s.excludeNogo ?? false,
    };
}

function formToSearch(f: FormState, timeZone: string): FpySearch {
    return {
        sourceId: f.sourceId,
        startUtc: f.from ? (wallClockToInstantIso(f.from, timeZone) ?? undefined) : undefined,
        endUtc: f.to ? (wallClockToInstantIso(f.to, timeZone) ?? undefined) : undefined,
        granularity: f.granularity,
        groupBy: f.groupBy,
        machineIds: f.machineIds.length > 0 ? f.machineIds : undefined,
        productIds: f.productIds.length > 0 ? f.productIds : undefined,
        onlyLastInspection: f.onlyLastInspection ? undefined : false,
        skipExclusion: f.skipExclusion === "Clean" ? "Clean" : undefined,
        skipStatuses: f.skipStatuses.length > 0 ? f.skipStatuses : undefined,
        excludeNogo: f.excludeNogo ? true : undefined,
    };
}
