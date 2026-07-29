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
import {
    IconAlertTriangle,
    IconDownload,
    IconEye,
    IconPrinter,
} from "@tabler/icons-react";
import "@mantine/dates/styles.css";
import {
    fetchMachines,
    fetchProducts,
    fetchSources,
    type SourceInfo,
} from "../api/sources";
import {
    dpmoExportUrl,
    runDpmoTableReport,
    type DpmoTableResult,
    type DpmoTableRow,
} from "../api/dpmo";
import {
    DPMO_GROUP_BYS,
    DPMO_NUMERATORS,
    DPMO_OPPORTUNITIES,
    SKIP_EXCLUSIONS,
    SKIP_STATUS_VALUES,
    pickDefaultSourceId,
    type DpmoGroupBy,
    type DpmoNumerator,
    type DpmoOpportunity,
    type DpmoSearch,
    type SkipExclusion,
    type SkipStatus,
} from "./dpmo.search";
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
    groupBy: DpmoGroupBy;
    numerator: DpmoNumerator;
    opportunity: DpmoOpportunity;
    machineIds: number[];
    productIds: number[];
    includeObsoleteBits: boolean;
    skipExclusion: SkipExclusion;
    skipStatuses: SkipStatus[];
    excludeNogo: boolean;
};

/**
 * DPMO table report route. Renders a filter form (source, window,
 * group-by axis, numerator, opportunity, skip toggle) and a table of
 * per-group DPMO (ppm) rows plus a grand-total.
 *
 * The raw / clean skip toggle is the headline feature: switching to
 * `Clean` excludes skipped / empty boards from both the opportunity
 * denominator and the defect numerator, which on real data collapses
 * the phantom "empty-board missing" DPMO (~51 ppm raw -> ~0.25 ppm
 * clean on the archive).
 *
 * URL-first: the whole filter lives in the search params so the report
 * is shareable and bookmarkable.
 */
export function DpmoRoute() {
    const { t } = useTranslation();
    const rawSearch = useSearch({ strict: false });
    const search = rawSearch as DpmoSearch;
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

    const reportEnabled = Boolean(
        search.sourceId && search.startUtc && search.endUtc && search.groupBy,
    );
    const reportQuery = useQuery({
        queryKey: ["dpmo-table", search],
        queryFn: () => runDpmoTableReport(search),
        enabled: reportEnabled,
    });

    const activeSource = useMemo<SourceInfo | undefined>(
        () => sources.find((s) => s.id === search.sourceId),
        [sources, search.sourceId],
    );

    const [pdfPreviewOpen, setPdfPreviewOpen] = useState(false);
    const pdfPreviewUrl = reportEnabled ? dpmoExportUrl(search, "pdf") : null;
    const pdfFallbackFilename = `dpmo-${search.sourceId ?? "source"}.pdf`;

    const canSubmit = Boolean(effectiveSourceId && form.from && form.to && form.groupBy);

    function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault();
        if (!canSubmit) return;
        const next = formToSearch({ ...form, sourceId: effectiveSourceId }, timeZone);
        void navigate({ to: "/report/dpmo", search: next, replace: false });
    }

    function handleReset() {
        setForm(emptyForm());
        void navigate({ to: "/report/dpmo", search: {} as DpmoSearch, replace: false });
    }

    async function downloadExport(format: "csv" | "xlsx" | "pdf") {
        if (!reportEnabled) return;
        const stem = `dpmo-${search.sourceId ?? "source"}-${search.startUtc?.slice(0, 10) ?? ""}`;
        try {
            await downloadWithAuth(dpmoExportUrl(search, format), `${stem}.${format}`);
        } catch {
            // downloadWithAuth clears the session on 401; other errors are
            // transient. The report card already surfaces API failures.
        }
    }

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("dpmo.title")}</Title>
                <Text c="dimmed">{t("dpmo.subtitle")}</Text>
            </Stack>

            <Card withBorder padding="lg" radius="md" component="form" onSubmit={handleSubmit}>
                <Title order={4} mb="sm">
                    {t("dpmo.filters.heading")}
                </Title>

                <Stack gap="md">
                    <Group grow align="flex-end">
                        <DateTimePicker
                            label={t("dpmo.filters.from")}
                            value={form.from}
                            onChange={(value) => setForm((prev) => ({ ...prev, from: value }))}
                            valueFormat="YYYY-MM-DD HH:mm"
                            clearable
                            required
                        />
                        <DateTimePicker
                            label={t("dpmo.filters.to")}
                            value={form.to}
                            onChange={(value) => setForm((prev) => ({ ...prev, to: value }))}
                            valueFormat="YYYY-MM-DD HH:mm"
                            clearable
                            required
                        />
                        <Select
                            label={t("dpmo.filters.source")}
                            placeholder={t("dpmo.filters.sourcePlaceholder")}
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
                            label={t("dpmo.filters.groupBy")}
                            data={DPMO_GROUP_BYS.map((g) => ({
                                value: g,
                                label: t(`dpmo.groupBy.${g}`),
                            }))}
                            value={form.groupBy}
                            onChange={(value) =>
                                setForm((prev) => ({
                                    ...prev,
                                    groupBy: (value ?? "AoiMachine") as DpmoGroupBy,
                                }))
                            }
                            required
                            allowDeselect={false}
                        />
                    </Group>

                    <Group grow align="flex-end">
                        <MultiSelect
                            label={t("dpmo.filters.machines")}
                            placeholder={t("dpmo.filters.machinesPlaceholder")}
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
                            label={t("dpmo.filters.products")}
                            placeholder={t("dpmo.filters.productsPlaceholder")}
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

                    <Group grow align="flex-start">
                        <Select
                            label={t("dpmo.filters.numerator")}
                            data={DPMO_NUMERATORS.map((n) => ({
                                value: n,
                                label: t(`dpmo.numerator.${n}`),
                            }))}
                            value={form.numerator}
                            onChange={(value) =>
                                setForm((prev) => ({
                                    ...prev,
                                    numerator: (value ?? "Real") as DpmoNumerator,
                                }))
                            }
                            allowDeselect={false}
                        />
                        <Select
                            label={t("dpmo.filters.opportunity")}
                            data={DPMO_OPPORTUNITIES.map((o) => ({
                                value: o,
                                label: t(`dpmo.opportunity.${o}`),
                            }))}
                            value={form.opportunity}
                            onChange={(value) =>
                                setForm((prev) => ({
                                    ...prev,
                                    opportunity: (value ?? "All") as DpmoOpportunity,
                                }))
                            }
                            allowDeselect={false}
                        />
                    </Group>

                    <Group align="flex-start" gap="lg">
                        <Stack gap={4}>
                            <Text size="sm" fw={500}>
                                {t("dpmo.filters.skipExclusion")}
                            </Text>
                            <SegmentedControl
                                data={SKIP_EXCLUSIONS.map((s) => ({
                                    value: s,
                                    label: t(`dpmo.skipExclusion.${s}`),
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
                                {t("dpmo.filters.skipExclusionHint")}
                            </Text>
                        </Stack>
                        <Stack gap="sm">
                            <MultiSelect
                                label={t("dpmo.filters.skipStatuses")}
                                description={t("dpmo.filters.skipStatusesHint")}
                                inputWrapperOrder={["label", "input", "description", "error"]}
                                placeholder={t("dpmo.filters.skipStatusesPlaceholder")}
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
                            label={t("dpmo.filters.excludeNogo")}
                            description={t("dpmo.filters.excludeNogoHint")}
                            checked={form.excludeNogo}
                            onChange={(event) =>
                                setForm((prev) => ({
                                    ...prev,
                                    excludeNogo: event.currentTarget.checked,
                                }))
                            }
                        />
                        {form.groupBy === "Defect" && (
                            <Checkbox
                                label={t("dpmo.filters.includeObsoleteBits")}
                                description={t("dpmo.filters.includeObsoleteBitsHint")}
                                checked={form.includeObsoleteBits}
                                onChange={(event) =>
                                    setForm((prev) => ({
                                        ...prev,
                                        includeObsoleteBits: event.currentTarget.checked,
                                    }))
                                }
                            />
                        )}
                    </Group>

                    <Group justify="space-between" className="no-print">
                        <Group>
                            <Button type="submit" disabled={!canSubmit}>
                                {t("dpmo.filters.submit")}
                            </Button>
                            <Button variant="subtle" onClick={handleReset} type="button">
                                {t("dpmo.filters.reset")}
                            </Button>
                            <Button
                                variant="default"
                                leftSection={<IconPrinter size={16} />}
                                onClick={() => window.print()}
                                type="button"
                                disabled={!reportEnabled}
                            >
                                {t("dpmo.filters.print")}
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
                                    <Text size="sm">{t("dpmo.filters.exportCsv")}</Text>
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
                                    <Text size="sm">{t("dpmo.filters.exportXlsx")}</Text>
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
                                    <Text size="sm">{t("dpmo.filters.exportPdf")}</Text>
                                </Group>
                            </Anchor>
                            <Anchor
                                component="button"
                                type="button"
                                onClick={() => setPdfPreviewOpen(true)}
                                aria-disabled={!reportEnabled}
                                data-disabled={!reportEnabled || undefined}
                                disabled={!reportEnabled}
                                data-testid="dpmo-preview-pdf"
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
    data: DpmoTableResult | undefined;
    error: unknown;
    source: SourceInfo | undefined;
}) {
    const { t } = useTranslation();
    const { enabled, isPending, isFetching, data, error, source } = props;

    const columns = useMemo<Column<DpmoTableRow>[]>(
        () => [
            {
                key: "group",
                header: t("dpmo.results.group"),
                accessor: (r) => r.groupName ?? r.groupKey ?? "",
                formatter: (v) => (v === "" || v == null ? t("dpmo.results.unassigned") : String(v)),
                hideable: false,
            },
            {
                key: "dpmoPpm",
                header: t("dpmo.results.dpmoPpm"),
                accessor: (r) => r.kpi.dpmoPpm,
                formatter: (v) =>
                    typeof v === "number" ? v.toLocaleString(undefined, { maximumFractionDigits: 2 }) : "",
                csvFormatter: (v) => (typeof v === "number" ? v.toFixed(2) : ""),
                align: "right",
            },
            {
                key: "defectBitCount",
                header: t("dpmo.results.defects"),
                accessor: (r) => r.kpi.defectBitCount,
                formatter: (v) => (typeof v === "number" ? v.toLocaleString() : ""),
                align: "right",
            },
            {
                key: "opportunityCount",
                header: t("dpmo.results.opportunities"),
                accessor: (r) => r.kpi.opportunityCount,
                formatter: (v) => (typeof v === "number" ? v.toLocaleString() : ""),
                align: "right",
            },
            {
                key: "testedObjectCount",
                header: t("dpmo.results.testedObjects"),
                accessor: (r) => r.kpi.testedObjectCount,
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
                <Title order={4}>{t("dpmo.results.heading")}</Title>
                {isFetching && <Loader size="xs" />}
            </Group>

            {error ? (
                <Alert
                    color="red"
                    icon={<IconAlertTriangle size={18} />}
                    title={t("dpmo.results.errorTitle")}
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
                            <strong>{t("dpmo.results.source")}:</strong>{" "}
                            {source?.displayName ?? data.source.displayName}
                        </Text>
                        <Text>
                            <strong>{t("dpmo.results.window")}:</strong>{" "}
                            {formatWindow(data.window.startUtc, data.window.endUtcExclusive)}
                        </Text>
                    </Group>

                    <Group gap="lg">
                        <Badge
                            color={data.skipExclusion === "Clean" ? "teal" : "gray"}
                            variant="light"
                            size="lg"
                        >
                            {t(`dpmo.skipExclusion.${data.skipExclusion}`)}
                        </Badge>
                        {data.skipExclusion === "Clean" && (
                            <Text size="sm" c="dimmed">
                                {t("dpmo.results.skipExcludedCards", {
                                    count: data.skipExcludedCards,
                                })}
                            </Text>
                        )}
                        <Badge color="blue" variant="light" size="lg">
                            {t("dpmo.results.overallPpm")}:{" "}
                            {data.overall.dpmoPpm.toLocaleString(undefined, {
                                maximumFractionDigits: 2,
                            })}
                        </Badge>
                    </Group>

                    <DataTable
                        columns={columns}
                        rows={data.rows}
                        rowKey={(r) => r.groupKey ?? "∅"}
                        onExportCsv={(visibleRows, visibleColumns) => {
                            downloadCsv(
                                `dpmo-${data.source.id}-${data.window.startUtc.slice(0, 10)}.csv`,
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
        groupBy: "AoiMachine",
        numerator: "Real",
        opportunity: "All",
        machineIds: [],
        productIds: [],
        includeObsoleteBits: false,
        skipExclusion: "Raw",
        skipStatuses: [],
        excludeNogo: false,
    };
}

function searchToForm(s: DpmoSearch, timeZone: string): FormState {
    return {
        sourceId: s.sourceId,
        from: s.startUtc ? instantIsoToWallClock(s.startUtc, timeZone) : null,
        to: s.endUtc ? instantIsoToWallClock(s.endUtc, timeZone) : null,
        groupBy: s.groupBy ?? "AoiMachine",
        numerator: s.numerator ?? "Real",
        opportunity: s.opportunity ?? "All",
        machineIds: s.machineIds ?? [],
        productIds: s.productIds ?? [],
        includeObsoleteBits: s.includeObsoleteBits ?? false,
        skipExclusion: s.skipExclusion ?? "Raw",
        skipStatuses: s.skipStatuses ?? [],
        excludeNogo: s.excludeNogo ?? false,
    };
}

function formToSearch(f: FormState, timeZone: string): DpmoSearch {
    return {
        sourceId: f.sourceId,
        startUtc: f.from ? (wallClockToInstantIso(f.from, timeZone) ?? undefined) : undefined,
        endUtc: f.to ? (wallClockToInstantIso(f.to, timeZone) ?? undefined) : undefined,
        groupBy: f.groupBy,
        numerator: f.numerator,
        opportunity: f.opportunity,
        machineIds: f.machineIds.length > 0 ? f.machineIds : undefined,
        productIds: f.productIds.length > 0 ? f.productIds : undefined,
        includeObsoleteBits: f.includeObsoleteBits ? true : undefined,
        skipExclusion: f.skipExclusion === "Clean" ? "Clean" : undefined,
        skipStatuses: f.skipStatuses.length > 0 ? f.skipStatuses : undefined,
        excludeNogo: f.excludeNogo ? true : undefined,
    };
}
