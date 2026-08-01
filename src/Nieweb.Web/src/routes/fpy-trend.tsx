import { lazy, Suspense, useMemo, useState } from "react";
import {
    Anchor,
    Badge,
    Button,
    Card,
    Group,
    Loader,
    SegmentedControl,
    SimpleGrid,
    Stack,
    Switch,
    Text,
    Title,
} from "@mantine/core";
import { DateTimePicker } from "@mantine/dates";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { IconDownload, IconEye, IconPrinter } from "@tabler/icons-react";
import "@mantine/dates/styles.css";
import {
    fpyTrendExportUrl,
    fpyPercentFor,
    runFpyTrendReport,
    type FpyTrendSourceResult,
} from "../api/fpyTrend";
import { downloadWithAuth } from "../api/download";
import { fetchMachines, fetchSources } from "../api/sources";
import { PdfPreviewModal } from "../components/PdfPreviewModal";
import { MultiSelectField } from "../components/MultiSelectField";
import { ApiErrorAlert } from "../components/ApiErrorAlert";
import { SavedViewsMenu } from "../components/SavedViewsMenu";
import {
    FPY_TREND_BUCKETS,
    FPY_TREND_FLAVORS,
    FPY_TREND_GRANULARITIES,
    SKIP_STATUS_VALUES,
    type FpyTrendBucketSize,
    type FpyTrendFlavor,
    type FpyTrendGranularity,
    type FpyTrendSearch,
    type SkipStatus,
    toApiQuery,
} from "./fpy-trend.search";
import {
    instantIsoToWallClock,
    wallClockToInstantIso,
} from "../i18n/zoneConverters";
import { resolveTimeZone, usePreferencesStore } from "../state/preferences";

// Chart is loaded on-demand (echarts is ~1.1 MB gzipped).
const FpyTrendChart = lazy(() =>
    import("../charts/FpyTrendChart").then((m) => ({ default: m.FpyTrendChart })),
);

/**
 * FPY Trend by Line report. Renders every AOI line on every source as a small
 * FPY-over-time card, bucketed by day or week. The bucket / granularity /
 * skip toggles refetch; the flavour toggle (Diagnostic / AOI) is display-only
 * and applies instantly because the API always returns all three flavours.
 *
 * URL-first: every filter lives in the search params so a report is
 * shareable / bookmarkable / reloadable verbatim.
 */
export function FpyTrendRoute() {
    const { t } = useTranslation();
    const rawSearch = useSearch({ strict: false });
    const search = rawSearch as FpyTrendSearch;
    const navigate = useNavigate();

    const timeZone = resolveTimeZone(usePreferencesStore((s) => s.timeZone));

    const [form, setForm] = useState<FormState>(() => searchToForm(search, timeZone));

    // The trend runs across every source. A production line spans a pre-reflow
    // AOI and a post-reflow AOI whose machine ids do NOT correspond across the
    // two DBs, so we filter by *line number* (parsed from the machine name,
    // e.g. L2PSTAOI -> line 2), not by machine id. Fetch machines from every
    // source and offer the distinct line numbers; the API resolves each line
    // back to the right machine ids per source.
    const sourcesQuery = useQuery({ queryKey: ["sources"], queryFn: fetchSources });
    const sourceIds = useMemo(
        () => (sourcesQuery.data ?? []).map((s) => s.id),
        [sourcesQuery.data],
    );
    const linesQuery = useQuery({
        queryKey: ["fpyTrendLines", sourceIds],
        queryFn: async () => {
            const perSource = await Promise.all(sourceIds.map((id) => fetchMachines(id)));
            const lines = new Set<number>();
            for (const list of perSource) {
                for (const m of list) {
                    const ln = parseLineNumber(m.name);
                    if (ln !== null) lines.add(ln);
                }
            }
            return [...lines].sort((a, b) => a - b);
        },
        enabled: sourceIds.length > 0,
    });

    // Refetch is keyed off the API query only (which omits `flavor`), so
    // flipping the flavour toggle never refetches.
    const reportEnabled = Boolean(search.startUtc && search.endUtc);
    const reportQuery = useQuery({
        queryKey: ["fpyTrend", toApiQuery(search)],
        queryFn: () => runFpyTrendReport(search),
        enabled: reportEnabled,
    });

    const flavor: FpyTrendFlavor = search.flavor ?? "Diagnostic";

    const [pdfPreviewOpen, setPdfPreviewOpen] = useState(false);
    const pdfPreviewUrl = reportEnabled ? fpyTrendExportUrl(search, "pdf") : null;

    const canSubmit = Boolean(form.from && form.to);

    function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault();
        if (!canSubmit) return;
        void navigate({
            to: "/report/fpy-trend",
            search: formToSearch(form, timeZone, flavor),
            replace: false,
        });
    }

    function handleReset() {
        setForm(emptyForm());
        void navigate({ to: "/report/fpy-trend", search: {} as FpyTrendSearch, replace: false });
    }

    // Flavour is display-only: update the URL immediately, no refetch.
    function setFlavor(next: FpyTrendFlavor) {
        void navigate({
            to: "/report/fpy-trend",
            search: { ...search, flavor: next },
            replace: true,
        });
    }

    function applySavedFilter(filter: FpyTrendSearch) {
        setForm(searchToForm(filter, timeZone));
        void navigate({ to: "/report/fpy-trend", search: filter, replace: false });
    }

    async function downloadExport(format: "csv" | "xlsx" | "pdf") {
        if (!reportEnabled) return;
        const stem = `fpy-trend-${search.bucket ?? "week"}-${search.startUtc?.slice(0, 10) ?? ""}`;
        try {
            await downloadWithAuth(fpyTrendExportUrl(search, format), `${stem}.${format}`);
        } catch {
            // downloadWithAuth clears the session on 401; other errors are surfaced by the card.
        }
    }

    const sources = reportQuery.data?.sources ?? [];
    const hasAnyLines = sources.some((s) => s.lines.length > 0);

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("fpyTrend.title")}</Title>
                <Text c="dimmed">{t("fpyTrend.subtitle")}</Text>
            </Stack>

            <Card withBorder padding="lg" radius="md" component="form" onSubmit={handleSubmit}>
                <Title order={4} mb="sm">
                    {t("fpyTrend.filters.heading")}
                </Title>

                <Stack gap="md">
                    <Group align="flex-end" wrap="wrap" gap="md">
                        <DateTimePicker
                            label={t("fpyTrend.filters.from")}
                            value={form.from}
                            onChange={(v) => setForm((p) => ({ ...p, from: v }))}
                            clearable
                            w={200}
                        />
                        <DateTimePicker
                            label={t("fpyTrend.filters.to")}
                            value={form.to}
                            onChange={(v) => setForm((p) => ({ ...p, to: v }))}
                            clearable
                            w={200}
                        />
                    </Group>

                    <Group align="flex-end" wrap="wrap" gap="md">
                        <Stack gap={2}>
                            <Text size="sm" fw={500}>{t("fpyTrend.filters.bucket")}</Text>
                            <SegmentedControl
                                value={form.bucket}
                                onChange={(v) => setForm((p) => ({ ...p, bucket: v as FpyTrendBucketSize }))}
                                data={FPY_TREND_BUCKETS.map((b) => ({
                                    value: b,
                                    label: b === "Week" ? t("fpyTrend.bucket.week") : t("fpyTrend.bucket.day"),
                                }))}
                            />
                        </Stack>
                        <Stack gap={2}>
                            <Text size="sm" fw={500}>{t("fpyTrend.filters.granularity")}</Text>
                            <SegmentedControl
                                value={form.granularity}
                                onChange={(v) => setForm((p) => ({ ...p, granularity: v as FpyTrendGranularity }))}
                                data={FPY_TREND_GRANULARITIES.map((g) => ({
                                    value: g,
                                    label: g === "Board" ? t("fpyTrend.granularity.board") : t("fpyTrend.granularity.panel"),
                                }))}
                            />
                        </Stack>
                        <Stack gap={2}>
                            <Text size="sm" fw={500}>{t("fpyTrend.filters.flavor")}</Text>
                            <SegmentedControl
                                value={flavor}
                                onChange={(v) => setFlavor(v as FpyTrendFlavor)}
                                data={FPY_TREND_FLAVORS.map((f) => ({
                                    value: f,
                                    label: f === "Diagnostic" ? t("fpyTrend.flavor.diagnostic") : t("fpyTrend.flavor.aoi"),
                                }))}
                            />
                        </Stack>
                    </Group>

                    <Group align="flex-end" wrap="wrap" gap="md">
                        <Switch
                            label={t("fpyTrend.filters.cleanSkips")}
                            checked={form.skipExclusion === "Clean"}
                            onChange={(e) =>
                                setForm((p) => ({
                                    ...p,
                                    skipExclusion: e.currentTarget.checked ? "Clean" : "Raw",
                                }))
                            }
                        />
                        <Switch
                            label={t("fpyTrend.filters.excludeNogo")}
                            checked={form.excludeNogo}
                            onChange={(e) => setForm((p) => ({ ...p, excludeNogo: e.currentTarget.checked }))}
                        />
                        <MultiSelectField
                            label={t("fpyTrend.filters.skipStatuses")}
                            placeholder={t("fpyTrend.filters.skipStatusesPlaceholder")}
                            data={SKIP_STATUS_VALUES.map((s) => ({ value: s, label: s }))}
                            value={form.skipStatuses}
                            onChange={(v) => setForm((p) => ({ ...p, skipStatuses: v as SkipStatus[] }))}
                            clearable
                            searchable
                            style={{ minWidth: 240 }}
                        />
                        <MultiSelectField
                            label={t("fpyTrend.filters.line")}
                            placeholder={t("fpyTrend.filters.linePlaceholder")}
                            data={(linesQuery.data ?? []).map((n) => ({
                                value: String(n),
                                label: t("fpyTrend.filters.lineOption", { number: n }),
                            }))}
                            value={(form.lines ?? []).map(String)}
                            onChange={(vals) =>
                                setForm((p) => ({
                                    ...p,
                                    lines: vals.map(Number).filter(Number.isFinite),
                                }))
                            }
                            disabled={linesQuery.isPending}
                            clearable
                            searchable
                            style={{ minWidth: 240 }}
                        />
                    </Group>

                    <Group justify="space-between" className="no-print">
                        <Group>
                            <Button type="submit" disabled={!canSubmit}>
                                {t("fpyTrend.filters.submit")}
                            </Button>
                            <Button variant="subtle" onClick={handleReset} type="button">
                                {t("fpyTrend.filters.reset")}
                            </Button>
                            <Button
                                variant="default"
                                leftSection={<IconPrinter size={16} />}
                                onClick={() => window.print()}
                                type="button"
                                disabled={!reportEnabled}
                            >
                                {t("fpyTrend.filters.print")}
                            </Button>
                            <SavedViewsMenu<FpyTrendSearch>
                                reportKey="fpy-trend"
                                currentFilter={search}
                                onApply={applySavedFilter}
                                canSave={reportEnabled}
                            />
                        </Group>
                        <Group>
                            <TrendExportLink label={t("fpyTrend.filters.exportCsv")} onClick={() => void downloadExport("csv")} disabled={!reportEnabled} />
                            <TrendExportLink label={t("fpyTrend.filters.exportXlsx")} onClick={() => void downloadExport("xlsx")} disabled={!reportEnabled} />
                            <TrendExportLink label={t("fpyTrend.filters.exportPdf")} onClick={() => void downloadExport("pdf")} disabled={!reportEnabled} />
                            <Anchor
                                component="button"
                                type="button"
                                onClick={() => setPdfPreviewOpen(true)}
                                aria-disabled={!reportEnabled}
                                data-disabled={!reportEnabled || undefined}
                                disabled={!reportEnabled}
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

            {reportQuery.error ? <ApiErrorAlert error={reportQuery.error} /> : null}

            {reportEnabled && reportQuery.isPending && <Loader />}

            {reportEnabled && !reportQuery.isPending && !reportQuery.error && !hasAnyLines && (
                <Card withBorder padding="lg" radius="md">
                    <Text c="dimmed">{t("fpyTrend.results.empty")}</Text>
                </Card>
            )}

            {sources.map((source) =>
                source.lines.length > 0 ? (
                    <SourceSection key={source.source.id} source={source} flavor={flavor} />
                ) : null,
            )}

            <PdfPreviewModal
                opened={pdfPreviewOpen}
                onClose={() => setPdfPreviewOpen(false)}
                pdfUrl={pdfPreviewUrl}
                fallbackFilename={`fpy-trend-${search.bucket ?? "week"}.pdf`}
            />
        </Stack>
    );
}

function TrendExportLink(props: { label: string; onClick: () => void; disabled: boolean }) {
    return (
        <Anchor
            component="button"
            type="button"
            onClick={props.onClick}
            aria-disabled={props.disabled}
            data-disabled={props.disabled || undefined}
            disabled={props.disabled}
        >
            <Group gap={4}>
                <IconDownload size={16} />
                <Text size="sm">{props.label}</Text>
            </Group>
        </Anchor>
    );
}

function SourceSection(props: { source: FpyTrendSourceResult; flavor: FpyTrendFlavor }) {
    const { t } = useTranslation();
    const { source, flavor } = props;
    return (
        <Stack gap="sm">
            <Group gap="xs">
                <Title order={3}>{source.source.displayName}</Title>
                <Badge variant="light">{t("fpyTrend.results.lineCount", { count: source.lines.length })}</Badge>
            </Group>
            <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
                {source.lines.map((line) => {
                    const overall = fpyPercentFor(line.overall, flavor);
                    return (
                        <Card key={line.machineId} withBorder padding="md" radius="md">
                            <Group justify="space-between" mb={4}>
                                <Text fw={600}>{line.machineName ?? `#${line.machineId}`}</Text>
                                <Badge variant="light">
                                    {t("fpyTrend.results.overallFpy")}: {overall.toFixed(2)}%
                                </Badge>
                            </Group>
                            <Text size="xs" c="dimmed" mb={6}>
                                {t("fpyTrend.results.inspected")}: {line.overall.inspectedCount.toLocaleString()}
                            </Text>
                            <Suspense fallback={<Loader size="sm" />}>
                                <FpyTrendChart buckets={source.buckets} line={line} flavor={flavor} height={200} />
                            </Suspense>
                        </Card>
                    );
                })}
            </SimpleGrid>
        </Stack>
    );
}

// ---------------------------------------------------------------
// Local form <-> URL search converters.
// ---------------------------------------------------------------

type FormState = {
    from: string | null;
    to: string | null;
    bucket: FpyTrendBucketSize;
    granularity: FpyTrendGranularity;
    skipExclusion: "Raw" | "Clean";
    skipStatuses: SkipStatus[];
    lines: number[];
    excludeNogo: boolean;
};

function emptyForm(): FormState {
    return {
        from: null,
        to: null,
        bucket: "Week",
        granularity: "Board",
        skipExclusion: "Clean",
        skipStatuses: [],
        lines: [],
        excludeNogo: false,
    };
}

function searchToForm(s: FpyTrendSearch, timeZone: string): FormState {
    return {
        from: s.startUtc ? instantIsoToWallClock(s.startUtc, timeZone) : null,
        to: s.endUtc ? instantIsoToWallClock(s.endUtc, timeZone) : null,
        bucket: s.bucket ?? "Week",
        granularity: s.granularity ?? "Board",
        skipExclusion: s.skipExclusion ?? "Clean",
        skipStatuses: s.skipStatuses ?? [],
        lines: s.lines ?? [],
        excludeNogo: s.excludeNogo ?? false,
    };
}

function formToSearch(f: FormState, timeZone: string, flavor: FpyTrendFlavor): FpyTrendSearch {
    return {
        startUtc: f.from ? (wallClockToInstantIso(f.from, timeZone) ?? undefined) : undefined,
        endUtc: f.to ? (wallClockToInstantIso(f.to, timeZone) ?? undefined) : undefined,
        bucket: f.bucket,
        granularity: f.granularity,
        flavor,
        skipExclusion: f.skipExclusion === "Clean" ? "Clean" : undefined,
        skipStatuses: f.skipStatuses.length > 0 ? f.skipStatuses : undefined,
        lines: f.lines.length > 0 ? f.lines : undefined,
        excludeNogo: f.excludeNogo ? true : undefined,
    };
}

/**
 * Parse the production line number from an AOI machine name. Names encode the
 * line as a leading `L{n}` (e.g. `L2PSTAOI` -> 2, `L7PREAOI` -> 7). Returns
 * `null` for names that do not follow the convention.
 */
function parseLineNumber(name: string | null | undefined): number | null {
    if (!name) return null;
    const m = /^L(\d+)/i.exec(name.trim());
    return m ? Number(m[1]) : null;
}
