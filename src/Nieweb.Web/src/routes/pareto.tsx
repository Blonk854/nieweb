import { useMemo, useState, lazy, Suspense } from "react";
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
    Select,
    Stack,
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
    IconPrinter,
    IconX,
} from "@tabler/icons-react";
import "@mantine/dates/styles.css";
import {
    fetchMachines,
    fetchProducts,
    fetchRecipes,
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
    pickDefaultSourceId,
    withDefectBit,
    withoutDefectBit,
    type ParetoAxis,
    type ParetoNumerator,
    type ParetoOpportunity,
    type ParetoSearch,
} from "./pareto.search";
import { DataTable, type Column } from "../components/DataTable";
import { downloadCsv, rowsToCsv } from "../components/csvExport";

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

    const [form, setForm] = useState<FormState>(() => searchToForm(search));

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
    const recipesQuery = useQuery({
        queryKey: ["recipes", effectiveSourceId],
        queryFn: () => fetchRecipes(effectiveSourceId!),
        enabled: Boolean(effectiveSourceId),
    });

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

    const canSubmit = Boolean(effectiveSourceId && form.from && form.to && form.axis);

    function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault();
        if (!canSubmit) return;
        const next: ParetoSearch = formToSearch({
            ...form,
            sourceId: effectiveSourceId,
        });
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

    // Chart bar click on the Defect axis: append the clicked bit to
    // the URL's defectBits filter and re-fetch. The user typically
    // then flips axis to Product / PartNumber to see which parts
    // contain that defect. Ignored on non-Defect axes (a bar's
    // groupKey there is a product/machine/topology id, not a bit).
    function handleBarClick(row: ParetoRow) {
        if (search.axis !== "Defect") return;
        if (!row.groupKey) return;
        const bit = Number(row.groupKey);
        if (!Number.isFinite(bit) || !Number.isInteger(bit) || bit <= 0) return;
        const next = withDefectBit(search, bit);
        if (next === search) return;
        // Keep local form state in sync so the visible filter chips
        // update immediately (the URL is the source of truth, but
        // useState-in-effect would flicker on back/forward).
        setForm((prev) => ({ ...prev, defectBits: next.defectBits ?? [] }));
        void navigate({
            to: "/report/pareto",
            search: next,
            replace: false,
        });
    }

    function removeDefectBit(bit: number) {
        const next = withoutDefectBit(search, bit);
        setForm((prev) => ({ ...prev, defectBits: next.defectBits ?? [] }));
        void navigate({
            to: "/report/pareto",
            search: next,
            replace: false,
        });
    }

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
                                    recipeIds: [],
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

                    <Group grow>
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
                    </Group>

                    <Group grow>
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
                        <NumberInput
                            label={t("pareto.filters.topN")}
                            description={t("pareto.filters.topNHint")}
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

                    <MultiSelect
                        label={t("pareto.filters.machines")}
                        placeholder={t("pareto.filters.machinesPlaceholder")}
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
                        label={t("pareto.filters.products")}
                        placeholder={t("pareto.filters.productsPlaceholder")}
                        data={(productsQuery.data ?? []).map((p) => ({
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

                    <MultiSelect
                        label={t("pareto.filters.recipes")}
                        placeholder={t("pareto.filters.recipesPlaceholder")}
                        data={(recipesQuery.data ?? []).map((r) => ({
                            value: String(r.id),
                            label: r.variantName
                                ? `${r.name} — ${r.variantName}`
                                : r.name,
                        }))}
                        value={(form.recipeIds ?? []).map(String)}
                        onChange={(vals) =>
                            setForm((prev) => ({
                                ...prev,
                                recipeIds: vals.map(Number).filter(Number.isFinite),
                            }))
                        }
                        disabled={!effectiveSourceId || recipesQuery.isPending}
                        searchable
                        clearable
                    />

                    {(search.defectBits?.length ?? 0) > 0 && (
                        <Group gap="xs" align="center">
                            <Text size="sm" fw={500}>
                                {t("pareto.filters.defectBitsChipsLabel")}:
                            </Text>
                            {(search.defectBits ?? []).map((bit) => (
                                <Badge
                                    key={bit}
                                    variant="light"
                                    color="red"
                                    rightSection={
                                        <IconX
                                            size={12}
                                            role="button"
                                            aria-label={t("pareto.filters.removeDefectBit", {
                                                bit,
                                            })}
                                            style={{ cursor: "pointer" }}
                                            onClick={() => removeDefectBit(bit)}
                                        />
                                    }
                                >
                                    {t("pareto.filters.defectBitChip", { bit })}
                                </Badge>
                            ))}
                        </Group>
                    )}

                    {!canSubmit && (
                        <Text c="dimmed" size="sm">
                            {t("pareto.filters.missingRequired")}
                        </Text>
                    )}

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
                                href={reportEnabled ? paretoExportUrl(search, "csv") : undefined}
                                aria-disabled={!reportEnabled}
                                data-disabled={!reportEnabled || undefined}
                            >
                                <Group gap={4}>
                                    <IconDownload size={16} />
                                    <Text size="sm">
                                        {t("pareto.filters.exportCsv")}
                                    </Text>
                                </Group>
                            </Anchor>
                            <Anchor
                                href={reportEnabled ? paretoExportUrl(search, "xlsx") : undefined}
                                aria-disabled={!reportEnabled}
                                data-disabled={!reportEnabled || undefined}
                            >
                                <Group gap={4}>
                                    <IconDownload size={16} />
                                    <Text size="sm">
                                        {t("pareto.filters.exportXlsx")}
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
    topN: number | undefined;
    vitalFewThreshold: number | undefined;
    machineIds: number[];
    productIds: number[];
    recipeIds: number[];
    defectBits: number[];
};

function emptyForm(): FormState {
    return {
        sourceId: undefined,
        from: null,
        to: null,
        axis: "Defect",
        numerator: "Real",
        opportunity: "All",
        topN: undefined,
        vitalFewThreshold: undefined,
        machineIds: [],
        productIds: [],
        recipeIds: [],
        defectBits: [],
    };
}

function searchToForm(s: ParetoSearch): FormState {
    return {
        sourceId: s.sourceId,
        from: s.startUtc ? isoToMantine(s.startUtc) : null,
        to: s.endUtc ? isoToMantine(s.endUtc) : null,
        axis: s.axis ?? "Defect",
        numerator: s.numerator ?? "Real",
        opportunity: s.opportunity ?? "All",
        topN: s.topN,
        vitalFewThreshold: s.vitalFewThreshold,
        machineIds: s.machineIds ?? [],
        productIds: s.productIds ?? [],
        recipeIds: s.recipeIds ?? [],
        defectBits: s.defectBits ?? [],
    };
}

function formToSearch(f: FormState): ParetoSearch {
    return {
        sourceId: f.sourceId,
        startUtc: f.from ? mantineToIso(f.from) : undefined,
        endUtc: f.to ? mantineToIso(f.to) : undefined,
        axis: f.axis,
        numerator: f.numerator,
        opportunity: f.opportunity,
        topN: f.topN,
        vitalFewThreshold: f.vitalFewThreshold,
        machineIds: f.machineIds.length > 0 ? f.machineIds : undefined,
        productIds: f.productIds.length > 0 ? f.productIds : undefined,
        recipeIds: f.recipeIds.length > 0 ? f.recipeIds : undefined,
        defectBits: f.defectBits.length > 0 ? f.defectBits : undefined,
    };
}

/** "YYYY-MM-DDTHH:mm:ss.sssZ" -> "YYYY-MM-DD HH:mm" (UTC parts). */
function isoToMantine(iso: string): string {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return "";
    return d.toISOString().slice(0, 16).replace("T", " ");
}

/** "YYYY-MM-DD HH:mm[:ss]" (treated as UTC) -> ISO-8601 with Z. */
function mantineToIso(value: string): string {
    const normalized = value.trim().replace(" ", "T");
    const withSeconds = /:\d{2}:\d{2}$/.test(normalized)
        ? normalized
        : `${normalized}:00`;
    return new Date(`${withSeconds}Z`).toISOString();
}

// ---------------------------------------------------------------
// Results panel.
// ---------------------------------------------------------------

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
        return (
            <Card withBorder padding="lg" radius="md">
                <Text c="dimmed">{t("pareto.filters.emptyPrompt")}</Text>
            </Card>
        );
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
                                        axis === "Defect" ? onBarClick : undefined
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
