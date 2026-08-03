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
    dpmoFor,
    dpmoTrendExportUrl,
    runDpmoTrendReport,
    type DpmoTrendSourceResult,
} from "../api/dpmoTrend";
import { downloadWithAuth } from "../api/download";
import { fetchMachines, fetchSources } from "../api/sources";
import { PdfPreviewModal } from "../components/PdfPreviewModal";
import { MultiSelectField } from "../components/MultiSelectField";
import { ApiErrorAlert } from "../components/ApiErrorAlert";
import { SavedViewsMenu } from "../components/SavedViewsMenu";
import {
    DPMO_NUMERATORS,
    DPMO_TREND_BUCKETS,
    DPMO_TREND_OPPORTUNITIES,
    SKIP_STATUS_VALUES,
    toApiQuery,
    type DpmoNumerator,
    type DpmoOpportunity,
    type DpmoTrendBucketSize,
    type DpmoTrendSearch,
    type SkipStatus,
} from "./dpmo-trend.search";
import {
    instantIsoToWallClock,
    wallClockToInstantIso,
} from "../i18n/zoneConverters";
import { resolveTimeZone, usePreferencesStore } from "../state/preferences";

// Chart is loaded on-demand (echarts is ~1.1 MB gzipped).
const DpmoTrendChart = lazy(() =>
    import("../charts/DpmoTrendChart").then((m) => ({ default: m.DpmoTrendChart })),
);

/**
 * DPMO Trend by Line report. Renders every AOI line on every source as a
 * small DPMO-over-time card, bucketed by day or week.
 *
 * Two toggles with deliberately different costs:
 *   - `opportunity` REFETCHES. It changes the denominator (which CARDS test
 *     counts are summed) and which tested objects contribute defects.
 *   - `numerator` is DISPLAY-ONLY and applies instantly, because the API
 *     returns AOI / Real / Dummy on every cell.
 *
 * URL-first: every filter lives in the search params so a report is
 * shareable / bookmarkable / reloadable verbatim.
 */
export function DpmoTrendRoute() {
    const { t } = useTranslation();
    const rawSearch = useSearch({ strict: false });
    const search = rawSearch as DpmoTrendSearch;
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
        queryKey: ["dpmoTrendLines", sourceIds],
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

    // Refetch is keyed off the API query only (which omits `numerator`), so
    // flipping the numerator toggle never refetches.
    const reportEnabled = Boolean(search.startUtc && search.endUtc);
    const reportQuery = useQuery({
        queryKey: ["dpmoTrend", toApiQuery(search)],
        queryFn: () => runDpmoTrendReport(search),
        enabled: reportEnabled,
    });

    const numerator: DpmoNumerator = search.numerator ?? "Real";

    const [pdfPreviewOpen, setPdfPreviewOpen] = useState(false);
    const pdfPreviewUrl = reportEnabled ? dpmoTrendExportUrl(search, "pdf") : null;

    const canSubmit = Boolean(form.from && form.to);

    function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault();
        if (!canSubmit) return;
        void navigate({
            to: "/report/dpmo-trend",
            search: formToSearch(form, timeZone, numerator),
            replace: false,
        });
    }

    function handleReset() {
        setForm(emptyForm());
        void navigate({ to: "/report/dpmo-trend", search: {} as DpmoTrendSearch, replace: false });
    }

    // Numerator is display-only: update the URL immediately, no refetch.
    function setNumerator(next: DpmoNumerator) {
        void navigate({
            to: "/report/dpmo-trend",
            search: { ...search, numerator: next },
            replace: true,
        });
    }

    function applySavedFilter(filter: DpmoTrendSearch) {
        setForm(searchToForm(filter, timeZone));
        void navigate({ to: "/report/dpmo-trend", search: filter, replace: false });
    }

    async function downloadExport(format: "csv" | "xlsx" | "pdf") {
        if (!reportEnabled) return;
        const stem = `dpmo-trend-${search.bucket ?? "week"}-${search.startUtc?.slice(0, 10) ?? ""}`;
        try {
            // downloadWithAuth, never a plain <a href>: the export endpoints
            // require the bearer token and a bare anchor 401s.
            await downloadWithAuth(dpmoTrendExportUrl(search, format), `${stem}.${format}`);
        } catch {
            // downloadWithAuth clears the session on 401; other errors are surfaced by the card.
        }
    }

    const sources = reportQuery.data?.sources ?? [];
    const hasAnyLines = sources.some((s) => s.lines.length > 0);

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("dpmoTrend.title")}</Title>
                <Text c="dimmed">{t("dpmoTrend.subtitle")}</Text>
            </Stack>

            <Card withBorder padding="lg" radius="md" component="form" onSubmit={handleSubmit}>
                <Title order={4} mb="sm">
                    {t("dpmoTrend.filters.heading")}
                </Title>

                <Stack gap="md">
                    <Group align="flex-end" wrap="wrap" gap="md">
                        <DateTimePicker
                            label={t("dpmoTrend.filters.from")}
                            value={form.from}
                            onChange={(v) => setForm((p) => ({ ...p, from: v }))}
                            clearable
                            w={200}
                        />
                        <DateTimePicker
                            label={t("dpmoTrend.filters.to")}
                            value={form.to}
                            onChange={(v) => setForm((p) => ({ ...p, to: v }))}
                            clearable
                            w={200}
                        />
                    </Group>

                    <Group align="flex-end" wrap="wrap" gap="md">
                        <Stack gap={2}>
                            <Text size="sm" fw={500}>{t("dpmoTrend.filters.bucket")}</Text>
                            <SegmentedControl
                                value={form.bucket}
                                onChange={(v) => setForm((p) => ({ ...p, bucket: v as DpmoTrendBucketSize }))}
                                data={DPMO_TREND_BUCKETS.map((b) => ({
                                    value: b,
                                    label: b === "Week" ? t("dpmoTrend.bucket.week") : t("dpmoTrend.bucket.day"),
                                }))}
                            />
                        </Stack>
                        <Stack gap={2}>
                            <Text size="sm" fw={500}>{t("dpmoTrend.filters.opportunity")}</Text>
                            <SegmentedControl
                                value={form.opportunity}
                                onChange={(v) => setForm((p) => ({ ...p, opportunity: v as DpmoOpportunity }))}
                                data={DPMO_TREND_OPPORTUNITIES.map((o) => ({
                                    value: o,
                                    label: o === "All" ? t("dpmoTrend.opportunity.all") : t("dpmoTrend.opportunity.components"),
                                }))}
                            />
                        </Stack>
                        <Stack gap={2}>
                            <Text size="sm" fw={500}>{t("dpmoTrend.filters.numerator")}</Text>
                            <SegmentedControl
                                value={numerator}
                                onChange={(v) => setNumerator(v as DpmoNumerator)}
                                data={DPMO_NUMERATORS.map((n) => ({
                                    value: n,
                                    label:
                                        n === "Aoi"
                                            ? t("dpmoTrend.numerator.aoi")
                                            : n === "Dummy"
                                              ? t("dpmoTrend.numerator.dummy")
                                              : t("dpmoTrend.numerator.real"),
                                }))}
                            />
                        </Stack>
                    </Group>

                    <Group align="flex-end" wrap="wrap" gap="md">
                        <Switch
                            label={t("dpmoTrend.filters.cleanSkips")}
                            checked={form.skipExclusion === "Clean"}
                            onChange={(e) =>
                                setForm((p) => ({
                                    ...p,
                                    skipExclusion: e.currentTarget.checked ? "Clean" : "Raw",
                                }))
                            }
                        />
                        <Switch
                            label={t("dpmoTrend.filters.excludeNogo")}
                            checked={form.excludeNogo}
                            onChange={(e) => setForm((p) => ({ ...p, excludeNogo: e.currentTarget.checked }))}
                        />
                        <MultiSelectField
                            label={t("dpmoTrend.filters.skipStatuses")}
                            placeholder={t("dpmoTrend.filters.skipStatusesPlaceholder")}
                            data={SKIP_STATUS_VALUES.map((s) => ({ value: s, label: s }))}
                            value={form.skipStatuses}
                            onChange={(v) => setForm((p) => ({ ...p, skipStatuses: v as SkipStatus[] }))}
                            clearable
                            searchable
                            style={{ minWidth: 240 }}
                        />
                        <MultiSelectField
                            label={t("dpmoTrend.filters.line")}
                            placeholder={t("dpmoTrend.filters.linePlaceholder")}
                            data={(linesQuery.data ?? []).map((n) => ({
                                value: String(n),
                                label: t("dpmoTrend.filters.lineOption", { number: n }),
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
                                {t("dpmoTrend.filters.submit")}
                            </Button>
                            <Button variant="subtle" onClick={handleReset} type="button">
                                {t("dpmoTrend.filters.reset")}
                            </Button>
                            <Button
                                variant="default"
                                leftSection={<IconPrinter size={16} />}
                                onClick={() => window.print()}
                                type="button"
                                disabled={!reportEnabled}
                            >
                                {t("dpmoTrend.filters.print")}
                            </Button>
                            <SavedViewsMenu<DpmoTrendSearch>
                                reportKey="dpmo-trend"
                                currentFilter={search}
                                onApply={applySavedFilter}
                                canSave={reportEnabled}
                            />
                        </Group>
                        <Group>
                            <TrendExportLink label={t("dpmoTrend.filters.exportCsv")} onClick={() => void downloadExport("csv")} disabled={!reportEnabled} />
                            <TrendExportLink label={t("dpmoTrend.filters.exportXlsx")} onClick={() => void downloadExport("xlsx")} disabled={!reportEnabled} />
                            <TrendExportLink label={t("dpmoTrend.filters.exportPdf")} onClick={() => void downloadExport("pdf")} disabled={!reportEnabled} />
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
                    <Text c="dimmed">{t("dpmoTrend.results.empty")}</Text>
                </Card>
            )}

            {sources.map((source) =>
                source.lines.length > 0 ? (
                    <SourceSection key={source.source.id} source={source} numerator={numerator} />
                ) : null,
            )}

            <PdfPreviewModal
                opened={pdfPreviewOpen}
                onClose={() => setPdfPreviewOpen(false)}
                pdfUrl={pdfPreviewUrl}
                fallbackFilename={`dpmo-trend-${search.bucket ?? "week"}.pdf`}
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

function SourceSection(props: { source: DpmoTrendSourceResult; numerator: DpmoNumerator }) {
    const { t } = useTranslation();
    const { source, numerator } = props;
    return (
        <Stack gap="sm">
            <Group gap="xs">
                <Title order={3}>{source.source.displayName}</Title>
                <Badge variant="light">{t("dpmoTrend.results.lineCount", { count: source.lines.length })}</Badge>
            </Group>
            <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
                {source.lines.map((line) => {
                    const overall = dpmoFor(line.overall, numerator);
                    return (
                        <Card key={line.machineId} withBorder padding="md" radius="md">
                            <Group justify="space-between" mb={4}>
                                <Text fw={600}>{line.machineName ?? `#${line.machineId}`}</Text>
                                <Badge variant="light">
                                    {t("dpmoTrend.results.overallDpmo")}:{" "}
                                    {overall.toLocaleString(undefined, { maximumFractionDigits: 2 })}
                                </Badge>
                            </Group>
                            <Text size="xs" c="dimmed" mb={6}>
                                {t("dpmoTrend.results.opportunities")}:{" "}
                                {line.overall.opportunityCount.toLocaleString()}
                            </Text>
                            <Suspense fallback={<Loader size="sm" />}>
                                <DpmoTrendChart
                                    buckets={source.buckets}
                                    line={line}
                                    numerator={numerator}
                                    height={200}
                                />
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
    bucket: DpmoTrendBucketSize;
    opportunity: DpmoOpportunity;
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
        opportunity: "Components",
        skipExclusion: "Clean",
        skipStatuses: [],
        lines: [],
        excludeNogo: false,
    };
}

function searchToForm(s: DpmoTrendSearch, timeZone: string): FormState {
    return {
        from: s.startUtc ? instantIsoToWallClock(s.startUtc, timeZone) : null,
        to: s.endUtc ? instantIsoToWallClock(s.endUtc, timeZone) : null,
        bucket: s.bucket ?? "Week",
        opportunity: s.opportunity ?? "Components",
        skipExclusion: s.skipExclusion ?? "Clean",
        skipStatuses: s.skipStatuses ?? [],
        lines: s.lines ?? [],
        excludeNogo: s.excludeNogo ?? false,
    };
}

function formToSearch(f: FormState, timeZone: string, numerator: DpmoNumerator): DpmoTrendSearch {
    return {
        startUtc: f.from ? (wallClockToInstantIso(f.from, timeZone) ?? undefined) : undefined,
        endUtc: f.to ? (wallClockToInstantIso(f.to, timeZone) ?? undefined) : undefined,
        bucket: f.bucket,
        opportunity: f.opportunity,
        numerator,
        skipExclusion: f.skipExclusion === "Clean" ? "Clean" : undefined,
        skipStatuses: f.skipStatuses.length > 0 ? f.skipStatuses : undefined,
        // Emitted as a NUMBER array. validateDpmoTrendSearch must survive that
        // shape — see dpmo-trend.search.ts::toNumberArray.
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
