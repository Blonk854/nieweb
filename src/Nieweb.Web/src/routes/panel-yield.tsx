import { useMemo, useState, lazy, Suspense } from "react";
import {
    Alert,
    Anchor,
    Button,
    Card,
    Checkbox,
    Group,
    Loader,
    MultiSelect,
    Select,
    Stack,
    Table,
    Text,
    Title,
} from "@mantine/core";
import { DateTimePicker } from "@mantine/dates";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { IconAlertTriangle, IconDownload } from "@tabler/icons-react";
import "@mantine/dates/styles.css";
import {
    fetchMachines,
    fetchProducts,
    fetchRecipes,
    fetchSources,
    type SourceInfo,
} from "../api/sources";
import {
    panelYieldExportUrl,
    runPanelYieldReport,
    type PanelYieldResult,
} from "../api/reports";
import type { PanelYieldSearch } from "./panel-yield.search";
import { pickDefaultSourceId } from "./panel-yield.search";
// Chart is loaded on-demand (echarts is ~1.1 MB gzipped). Splitting it
// out keeps the initial bundle small; the chunk is only fetched when a
// user actually runs a report with per-machine rows.
const FpyBarChart = lazy(() =>
    import("../charts/FpyBarChart").then((m) => ({ default: m.FpyBarChart })),
);

/**
 * Panel Yield by Line report - F4 filter form.
 *
 * Every filter (source, from, to, machines, products, recipes,
 * onlyLastInspection) lives in the URL search-params, validated by
 * `validatePanelYieldSearch` in ../routes/panel-yield.search.ts.
 * Editing a control updates local form state; submitting navigates to
 * the same route with the new search params and kicks off a report
 * mutation. This way, the URL alone is enough to bookmark, share, or
 * reload a report exactly as it was run.
 */
export function PanelYieldRoute() {
    const { t } = useTranslation();
    // `strict: false` lets us read the search on any route the
    // component is mounted under. In production it's under
    // panelYieldRoute (validated by validatePanelYieldSearch); in
    // tests it's under a minimal test tree - either way, the shape we
    // cast to is enforced by the URL validator.
    const rawSearch = useSearch({ strict: false });
    const search = rawSearch as PanelYieldSearch;
    const navigate = useNavigate();

    const sourcesQuery = useQuery({ queryKey: ["sources"], queryFn: fetchSources });
    const sources = useMemo(() => sourcesQuery.data ?? [], [sourcesQuery.data]);

    // ----- Local form state. Initialised once from the URL search
    // params; the user then edits it freely and Submit pushes the
    // final shape back into the URL (which drives the report query
    // below). External URL changes (back/forward) update the report
    // panel but do NOT reset in-progress form edits - keeping the form
    // as URL-driven state would trip react-hooks/set-state-in-effect
    // and cascade re-renders on every keystroke.
    const [form, setForm] = useState<FormState>(() => searchToForm(search));

    // Effective source: what the user picked, or a sensible default
    // once the sources list loads. Computed on the fly so we don't
    // need a setState-in-effect to seed form.sourceId.
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

    // The report query is driven off the URL search params (not the
    // local form) so the URL alone reproduces a run. Cached per shape.
    const reportEnabled = Boolean(
        search.sourceId && search.startUtc && search.endUtc,
    );
    const reportQuery = useQuery({
        queryKey: ["panelYield", search],
        queryFn: () => runPanelYieldReport(search),
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
        const next: PanelYieldSearch = formToSearch({
            ...form,
            sourceId: effectiveSourceId,
        });
        void navigate({
            to: "/report/panel-yield",
            search: next,
            replace: false,
        });
    }

    function handleReset() {
        setForm(emptyForm());
        void navigate({
            to: "/report/panel-yield",
            search: {} as PanelYieldSearch,
            replace: false,
        });
    }

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("panelYield.title")}</Title>
                <Text c="dimmed">{t("panelYield.subtitle")}</Text>
            </Stack>

            <Card
                withBorder
                padding="lg"
                radius="md"
                component="form"
                onSubmit={handleSubmit}
            >
                <Title order={4} mb="sm">
                    {t("panelYield.filters.heading")}
                </Title>

                <Stack gap="md">
                    <Group grow align="flex-end">
                        <Select
                            label={t("panelYield.filters.source")}
                            placeholder={t("panelYield.filters.sourcePlaceholder")}
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
                        <Checkbox
                            label={t("panelYield.filters.onlyLastInspection")}
                            description={t(
                                "panelYield.filters.onlyLastInspectionHint",
                            )}
                            checked={form.onlyLastInspection ?? true}
                            onChange={(event) =>
                                setForm((prev) => ({
                                    ...prev,
                                    onlyLastInspection: event.currentTarget.checked,
                                }))
                            }
                        />
                    </Group>

                    <Group grow>
                        <DateTimePicker
                            label={t("panelYield.filters.from")}
                            value={form.from}
                            onChange={(value) =>
                                setForm((prev) => ({ ...prev, from: value }))
                            }
                            valueFormat="YYYY-MM-DD HH:mm"
                            clearable
                            required
                        />
                        <DateTimePicker
                            label={t("panelYield.filters.to")}
                            value={form.to}
                            onChange={(value) =>
                                setForm((prev) => ({ ...prev, to: value }))
                            }
                            valueFormat="YYYY-MM-DD HH:mm"
                            clearable
                            required
                        />
                    </Group>

                    <MultiSelect
                        label={t("panelYield.filters.machines")}
                        placeholder={t("panelYield.filters.machinesPlaceholder")}
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
                        label={t("panelYield.filters.products")}
                        placeholder={t("panelYield.filters.productsPlaceholder")}
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
                        label={t("panelYield.filters.recipes")}
                        placeholder={t("panelYield.filters.recipesPlaceholder")}
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

                    {!canSubmit && (
                        <Text c="dimmed" size="sm">
                            {t("panelYield.filters.missingRequired")}
                        </Text>
                    )}

                    <Group justify="space-between">
                        <Group>
                            <Button type="submit" disabled={!canSubmit}>
                                {t("panelYield.filters.submit")}
                            </Button>
                            <Button variant="subtle" onClick={handleReset} type="button">
                                {t("panelYield.filters.reset")}
                            </Button>
                        </Group>
                        <Group>
                            <Anchor
                                href={reportEnabled ? panelYieldExportUrl(search, "csv") : undefined}
                                aria-disabled={!reportEnabled}
                                data-disabled={!reportEnabled || undefined}
                            >
                                <Group gap={4}>
                                    <IconDownload size={16} />
                                    <Text size="sm">
                                        {t("panelYield.filters.exportCsv")}
                                    </Text>
                                </Group>
                            </Anchor>
                            <Anchor
                                href={reportEnabled ? panelYieldExportUrl(search, "xlsx") : undefined}
                                aria-disabled={!reportEnabled}
                                data-disabled={!reportEnabled || undefined}
                            >
                                <Group gap={4}>
                                    <IconDownload size={16} />
                                    <Text size="sm">
                                        {t("panelYield.filters.exportXlsx")}
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
            />
        </Stack>
    );
}

// ---------------------------------------------------------------
// Local form <-> URL search shape converters.
// ---------------------------------------------------------------

type FormState = {
    sourceId: string | undefined;
    /** "YYYY-MM-DD HH:mm" local Mantine format; interpreted as UTC on submit. */
    from: string | null;
    /** "YYYY-MM-DD HH:mm" local Mantine format; interpreted as UTC on submit. */
    to: string | null;
    machineIds: number[];
    productIds: number[];
    recipeIds: number[];
    onlyLastInspection: boolean | undefined;
};

function emptyForm(): FormState {
    return {
        sourceId: undefined,
        from: null,
        to: null,
        machineIds: [],
        productIds: [],
        recipeIds: [],
        onlyLastInspection: undefined,
    };
}

function searchToForm(s: PanelYieldSearch): FormState {
    return {
        sourceId: s.sourceId,
        from: s.startUtc ? isoToMantine(s.startUtc) : null,
        to: s.endUtc ? isoToMantine(s.endUtc) : null,
        machineIds: s.machineIds ?? [],
        productIds: s.productIds ?? [],
        recipeIds: s.recipeIds ?? [],
        onlyLastInspection: s.onlyLastInspection,
    };
}

function formToSearch(f: FormState): PanelYieldSearch {
    return {
        sourceId: f.sourceId,
        startUtc: f.from ? mantineToIso(f.from) : undefined,
        endUtc: f.to ? mantineToIso(f.to) : undefined,
        machineIds: f.machineIds.length > 0 ? f.machineIds : undefined,
        productIds: f.productIds.length > 0 ? f.productIds : undefined,
        recipeIds: f.recipeIds.length > 0 ? f.recipeIds : undefined,
        onlyLastInspection: f.onlyLastInspection,
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
    // Mantine v9 emits strings like "2026-07-15 12:34" (no zone). We
    // interpret them as UTC so that the same string in the URL means the
    // same point in time regardless of the operator's local timezone.
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
    data: PanelYieldResult | undefined;
    error: unknown;
    source: SourceInfo | undefined;
}) {
    const { t } = useTranslation();
    const { enabled, isPending, isFetching, data, error, source } = props;

    if (!enabled) {
        return (
            <Card withBorder padding="lg" radius="md">
                <Text c="dimmed">{t("panelYield.filters.emptyPrompt")}</Text>
            </Card>
        );
    }

    return (
        <Card withBorder padding="lg" radius="md">
            <Group justify="space-between" mb="sm">
                <Title order={4}>{t("panelYield.results.heading")}</Title>
                {isFetching && <Loader size="xs" />}
            </Group>

            {error ? (
                <Alert
                    color="red"
                    icon={<IconAlertTriangle size={18} />}
                    title={t("home.sourcesErrorTitle")}
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
                            <strong>{t("panelYield.results.source")}:</strong>{" "}
                            {source?.displayName ?? data.source.displayName}
                        </Text>
                        <Text>
                            <strong>{t("panelYield.results.window")}:</strong>{" "}
                            {formatWindow(data.window.startUtc, data.window.endUtcExclusive)}
                        </Text>
                    </Group>

                    <OverallRow data={data} />

                    {data.byMachine.length === 0 ? (
                        <Text c="dimmed">{t("panelYield.results.noRows")}</Text>
                    ) : (
                        <>
                            <Suspense fallback={<Loader size="sm" />}>
                                <FpyBarChart
                                    rows={data.byMachine}
                                    overallFpyPercent={data.overall.fpyPercent}
                                />
                            </Suspense>
                            <Table striped withTableBorder>
                            <Table.Thead>
                                <Table.Tr>
                                    <Table.Th>{t("panelYield.results.machineName")}</Table.Th>
                                    <Table.Th>{t("panelYield.results.totalPanels")}</Table.Th>
                                    <Table.Th>{t("panelYield.results.inspectedPanels")}</Table.Th>
                                    <Table.Th>{t("panelYield.results.goodPanels")}</Table.Th>
                                    <Table.Th>{t("panelYield.results.faultyPanels")}</Table.Th>
                                    <Table.Th>{t("panelYield.results.notInspectedPanels")}</Table.Th>
                                    <Table.Th>{t("panelYield.results.fpyPercent")}</Table.Th>
                                </Table.Tr>
                            </Table.Thead>
                            <Table.Tbody>
                                {data.byMachine.map((row) => (
                                    <Table.Tr key={row.machineId}>
                                        <Table.Td>{row.machineName ?? `#${row.machineId}`}</Table.Td>
                                        <Table.Td>{row.kpi.totalPanels}</Table.Td>
                                        <Table.Td>{row.kpi.inspectedPanels}</Table.Td>
                                        <Table.Td>{row.kpi.goodPanels}</Table.Td>
                                        <Table.Td>{row.kpi.faultyPanels}</Table.Td>
                                        <Table.Td>{row.kpi.notInspectedPanels}</Table.Td>
                                        <Table.Td>{row.kpi.fpyPercent.toFixed(2)}</Table.Td>
                                    </Table.Tr>
                                ))}
                            </Table.Tbody>
                        </Table>
                        </>
                    )}
                </Stack>
            )}
        </Card>
    );
}

function OverallRow({ data }: { data: PanelYieldResult }) {
    const { t } = useTranslation();
    return (
        <Group gap="lg">
            <Text>
                <strong>{t("panelYield.results.overall")}:</strong>{" "}
            </Text>
            <Text>
                {t("panelYield.results.totalPanels")}: {data.overall.totalPanels}
            </Text>
            <Text>
                {t("panelYield.results.goodPanels")}: {data.overall.goodPanels}
            </Text>
            <Text>
                {t("panelYield.results.fpyPercent")}: {data.overall.fpyPercent.toFixed(2)}
            </Text>
        </Group>
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
