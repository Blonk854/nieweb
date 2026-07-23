import { lazy, Suspense, useMemo } from "react";
import { Alert, Card, Group, Loader, Stack, Text, Title } from "@mantine/core";
import { IconAlertTriangle } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { fetchSources } from "../../../api/sources";
import { runPanelYieldReport } from "../../../api/reports";
import { KpiCards } from "../../KpiCards";
import {
    canvasFiltersReady,
    useCanvasFilters,
} from "../FilterContext";

// Chart shares the same on-demand-loaded `echarts` chunk with the
// stand-alone panel-yield route — importing lazily here keeps the
// canvas-demo entry chunk small.
const FpyBarChart = lazy(() =>
    import("../../../charts/FpyBarChart").then((m) => ({
        default: m.FpyBarChart,
    })),
);

/**
 * Panel-Yield tile for `<ReportCanvas>`.
 *
 * Reads the current source / window / narrowing filters from
 * `useCanvasFilters()` and runs the same `/api/reports/panel-yield`
 * query as the stand-alone route. Renders KPI cards + the per-machine
 * FPY bar chart when the API returns rows. Failure paths (missing
 * filters, empty response, 4xx / 5xx) render a scoped `<Alert>` so
 * one bad tile never blanks the whole canvas.
 */
export function PanelYieldTile() {
    const { t } = useTranslation();
    const { filters } = useCanvasFilters();
    const ready = canvasFiltersReady(filters);

    const sourcesQuery = useQuery({
        queryKey: ["sources"],
        queryFn: fetchSources,
    });

    const reportQuery = useQuery({
        queryKey: [
            "canvas",
            "panel-yield",
            filters.sourceId,
            filters.startUtc,
            filters.endUtc,
            filters.machineIds?.join(",") ?? "",
            filters.productIds?.join(",") ?? "",
        ],
        queryFn: () =>
            runPanelYieldReport({
                sourceId: filters.sourceId,
                startUtc: filters.startUtc,
                endUtc: filters.endUtc,
                machineIds: filters.machineIds,
                productIds: filters.productIds,
                onlyLastInspection: true,
            }),
        enabled: ready,
    });

    const sourceDisplayName = useMemo(() => {
        const src = (sourcesQuery.data ?? []).find(
            (s) => s.id.toLowerCase() === filters.sourceId?.toLowerCase(),
        );
        return src?.displayName ?? filters.sourceId ?? "";
    }, [sourcesQuery.data, filters.sourceId]);

    if (!ready) {
        return (
            <Text c="dimmed" size="sm">
                {t("canvas.tiles.panelYield.emptyPrompt")}
            </Text>
        );
    }

    if (reportQuery.isPending) {
        return (
            <Group>
                <Loader size="sm" />
                <Text size="sm" c="dimmed">
                    {t("canvas.tiles.loading")}
                </Text>
            </Group>
        );
    }

    if (reportQuery.isError) {
        return (
            <Alert
                icon={<IconAlertTriangle size={16} />}
                color="red"
                variant="light"
                title={t("canvas.tiles.errorTitle")}
            >
                {(reportQuery.error as Error).message}
            </Alert>
        );
    }

    const result = reportQuery.data;

    return (
        <Stack gap="sm">
            <KpiCards
                totalPanels={result.overall.totalPanels}
                overallFpyPercent={result.overall.fpyPercent}
                latestPanelUtc={result.window.endUtcExclusive}
                sourceDisplayName={sourceDisplayName}
            />
            {result.byMachine.length > 0 ? (
                <Card withBorder padding="sm" radius="md">
                    <Title order={5} mb="xs">
                        {t("canvas.tiles.panelYield.chartHeading")}
                    </Title>
                    <Suspense
                        fallback={
                            <Group>
                                <Loader size="sm" />
                                <Text size="sm" c="dimmed">
                                    {t("canvas.tiles.loading")}
                                </Text>
                            </Group>
                        }
                    >
                        <FpyBarChart
                            rows={result.byMachine}
                            overallFpyPercent={result.overall.fpyPercent}
                            height={260}
                        />
                    </Suspense>
                </Card>
            ) : (
                <Text size="sm" c="dimmed">
                    {t("canvas.tiles.panelYield.noRows")}
                </Text>
            )}
        </Stack>
    );
}
