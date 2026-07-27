import { useMemo, useState } from "react";
import {
    Alert,
    Badge,
    Button,
    Card,
    Checkbox,
    Group,
    Loader,
    MultiSelect,
    Select,
    Stack,
    Text,
    Title,
} from "@mantine/core";
import { DateTimePicker } from "@mantine/dates";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { IconAlertTriangle } from "@tabler/icons-react";
import "@mantine/dates/styles.css";
import {
    fetchMachines,
    fetchProducts,
    fetchSources,
    type SourceInfo,
} from "../api/sources";
import {
    runSkipSummaryReport,
    type SkipClassCount,
    type SkipSummaryResult,
} from "../api/skipSummary";
import {
    pickDefaultSourceId,
    type SkipSummarySearch,
} from "./skip-summary.search";
import { DataTable, type Column } from "../components/DataTable";
import { downloadCsv, rowsToCsv } from "../components/csvExport";
import {
    instantIsoToWallClock,
    wallClockToInstantIso,
} from "../i18n/zoneConverters";
import { resolveTimeZone, usePreferencesStore } from "../state/preferences";

type FormState = {
    sourceId: string | undefined;
    from: string | null;
    to: string | null;
    machineIds: number[];
    productIds: number[];
    onlyLastInspection: boolean;
};

/**
 * Skip-summary report route. Classifies every sub-panel in the window
 * into None / ManualSkip / MachineFlagged / HeuristicMissing so an
 * analyst can quantify skipped / empty-board pollution (which otherwise
 * inflates DPMO and depresses FPY).
 *
 * URL-first: the whole filter lives in the search params so the report
 * is shareable and bookmarkable.
 */
export function SkipSummaryRoute() {
    const { t } = useTranslation();
    const rawSearch = useSearch({ strict: false });
    const search = rawSearch as SkipSummarySearch;
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
        queryKey: ["skip-summary", search],
        queryFn: () => runSkipSummaryReport(search),
        enabled: reportEnabled,
    });

    const activeSource = useMemo<SourceInfo | undefined>(
        () => sources.find((s) => s.id === search.sourceId),
        [sources, search.sourceId],
    );

    const canSubmit = Boolean(effectiveSourceId && form.from && form.to);

    function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault();
        if (!canSubmit) return;
        const next = formToSearch({ ...form, sourceId: effectiveSourceId }, timeZone);
        void navigate({ to: "/report/skip-summary", search: next, replace: false });
    }

    function handleReset() {
        setForm(emptyForm());
        void navigate({
            to: "/report/skip-summary",
            search: {} as SkipSummarySearch,
            replace: false,
        });
    }

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("skipSummary.title")}</Title>
                <Text c="dimmed">{t("skipSummary.subtitle")}</Text>
            </Stack>

            <Card withBorder padding="lg" radius="md" component="form" onSubmit={handleSubmit}>
                <Title order={4} mb="sm">
                    {t("skipSummary.filters.heading")}
                </Title>

                <Stack gap="md">
                    <Group grow align="flex-end">
                        <Select
                            label={t("skipSummary.filters.source")}
                            placeholder={t("skipSummary.filters.sourcePlaceholder")}
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
                    </Group>

                    <Group grow>
                        <DateTimePicker
                            label={t("skipSummary.filters.from")}
                            value={form.from}
                            onChange={(value) => setForm((prev) => ({ ...prev, from: value }))}
                            valueFormat="YYYY-MM-DD HH:mm"
                            clearable
                            required
                        />
                        <DateTimePicker
                            label={t("skipSummary.filters.to")}
                            value={form.to}
                            onChange={(value) => setForm((prev) => ({ ...prev, to: value }))}
                            valueFormat="YYYY-MM-DD HH:mm"
                            clearable
                            required
                        />
                    </Group>

                    <MultiSelect
                        label={t("skipSummary.filters.machines")}
                        placeholder={t("skipSummary.filters.machinesPlaceholder")}
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
                        label={t("skipSummary.filters.products")}
                        placeholder={t("skipSummary.filters.productsPlaceholder")}
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

                    <Checkbox
                        label={t("skipSummary.filters.onlyLastInspection")}
                        description={t("skipSummary.filters.onlyLastInspectionHint")}
                        checked={form.onlyLastInspection}
                        onChange={(event) =>
                            setForm((prev) => ({
                                ...prev,
                                onlyLastInspection: event.currentTarget.checked,
                            }))
                        }
                    />

                    {!canSubmit && (
                        <Text c="dimmed" size="sm">
                            {t("skipSummary.filters.missingRequired")}
                        </Text>
                    )}

                    <Group>
                        <Button type="submit" disabled={!canSubmit}>
                            {t("skipSummary.filters.submit")}
                        </Button>
                        <Button variant="subtle" onClick={handleReset} type="button">
                            {t("skipSummary.filters.reset")}
                        </Button>
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
        </Stack>
    );
}

function ResultsCard(props: {
    enabled: boolean;
    isPending: boolean;
    isFetching: boolean;
    data: SkipSummaryResult | undefined;
    error: unknown;
    source: SourceInfo | undefined;
}) {
    const { t } = useTranslation();
    const { enabled, isPending, isFetching, data, error, source } = props;

    const columns = useMemo<Column<SkipClassCount>[]>(
        () => [
            {
                key: "class",
                header: t("skipSummary.results.class"),
                accessor: (r) => r.class,
                formatter: (_v, r) => t(`skipSummary.classLabel.${r.class}`),
                hideable: false,
            },
            {
                key: "cardCount",
                header: t("skipSummary.results.cardCount"),
                accessor: (r) => r.cardCount,
                formatter: (v) => (typeof v === "number" ? v.toLocaleString() : ""),
                align: "right",
            },
            {
                key: "cardPercent",
                header: t("skipSummary.results.cardPercent"),
                accessor: (r) => r.cardPercent,
                formatter: (v) => (typeof v === "number" ? `${v.toFixed(1)}%` : ""),
                csvFormatter: (v) => (typeof v === "number" ? v.toFixed(2) : ""),
                align: "right",
            },
            {
                key: "componentCount",
                header: t("skipSummary.results.componentCount"),
                accessor: (r) => r.componentCount,
                formatter: (v) => (typeof v === "number" ? v.toLocaleString() : ""),
                align: "right",
            },
        ],
        [t],
    );

    if (!enabled) {
        return (
            <Card withBorder padding="lg" radius="md">
                <Text c="dimmed">{t("skipSummary.filters.emptyPrompt")}</Text>
            </Card>
        );
    }

    return (
        <Card withBorder padding="lg" radius="md">
            <Group justify="space-between" mb="sm">
                <Title order={4}>{t("skipSummary.results.heading")}</Title>
                {isFetching && <Loader size="xs" />}
            </Group>

            {error ? (
                <Alert
                    color="red"
                    icon={<IconAlertTriangle size={18} />}
                    title={t("skipSummary.results.errorTitle")}
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
                            <strong>{t("skipSummary.results.source")}:</strong>{" "}
                            {source?.displayName ?? data.source.displayName}
                        </Text>
                        <Text>
                            <strong>{t("skipSummary.results.window")}:</strong>{" "}
                            {formatWindow(data.window.startUtc, data.window.endUtcExclusive)}
                        </Text>
                    </Group>

                    <Group gap="lg">
                        <Text size="sm">
                            <strong>{t("skipSummary.results.totalCards")}:</strong>{" "}
                            {data.totalCards.toLocaleString()}
                        </Text>
                        <Text size="sm">
                            <strong>{t("skipSummary.results.totalComponents")}:</strong>{" "}
                            {data.totalComponents.toLocaleString()}
                        </Text>
                        <Text size="sm">
                            <strong>{t("skipSummary.results.skippedCards")}:</strong>{" "}
                            {data.skippedCards.toLocaleString()}
                        </Text>
                        <Badge
                            color={data.skippedCardPercent > 0 ? "orange" : "green"}
                            variant="light"
                            size="lg"
                        >
                            {t("skipSummary.results.skippedCardPercent")}:{" "}
                            {data.skippedCardPercent.toFixed(2)}%
                        </Badge>
                    </Group>

                    <DataTable
                        columns={columns}
                        rows={data.classes}
                        rowKey={(r) => r.class}
                        onExportCsv={(visibleRows, visibleColumns) => {
                            downloadCsv(
                                `skip-summary-${data.source.id}-${data.window.startUtc.slice(0, 10)}.csv`,
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
        machineIds: [],
        productIds: [],
        onlyLastInspection: true,
    };
}

function searchToForm(s: SkipSummarySearch, timeZone: string): FormState {
    return {
        sourceId: s.sourceId,
        from: s.startUtc ? instantIsoToWallClock(s.startUtc, timeZone) : null,
        to: s.endUtc ? instantIsoToWallClock(s.endUtc, timeZone) : null,
        machineIds: s.machineIds ?? [],
        productIds: s.productIds ?? [],
        onlyLastInspection: s.onlyLastInspection ?? true,
    };
}

function formToSearch(f: FormState, timeZone: string): SkipSummarySearch {
    return {
        sourceId: f.sourceId,
        startUtc: f.from ? (wallClockToInstantIso(f.from, timeZone) ?? undefined) : undefined,
        endUtc: f.to ? (wallClockToInstantIso(f.to, timeZone) ?? undefined) : undefined,
        machineIds: f.machineIds.length > 0 ? f.machineIds : undefined,
        productIds: f.productIds.length > 0 ? f.productIds : undefined,
        // Server default is true; only carry the flag when explicitly off.
        onlyLastInspection: f.onlyLastInspection ? undefined : false,
    };
}
