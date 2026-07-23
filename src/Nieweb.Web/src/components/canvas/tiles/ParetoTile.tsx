import { lazy, Suspense } from "react";
import { Alert, Card, Group, Loader, Stack, Text, Title } from "@mantine/core";
import { IconAlertTriangle } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { runParetoReport } from "../../../api/pareto";
import type { ParetoSearch } from "../../../routes/pareto.search";
import {
    canvasFiltersReady,
    useCanvasFilters,
} from "../FilterContext";

// See PanelYieldTile.tsx for the lazy-chart rationale — echarts is
// ~1.1 MB gzipped and we only pull the chunk once a real user asks
// for a Pareto tile.
const ParetoChart = lazy(() =>
    import("../../../charts/ParetoChart").then((m) => ({
        default: m.ParetoChart,
    })),
);

/**
 * Pareto tile for `<ReportCanvas>`.
 *
 * Uses the same defaults as the stand-alone `/report/pareto` route
 * (axis=Defect, numerator=Real, opportunity=Components,
 * weight=Count, topN=10) — the boss-approved "DPMO real defects"
 * view. Canvas-level source / window / narrowing filters are read
 * from `useCanvasFilters()` and forwarded to the API. Advanced
 * per-tile overrides are intentionally out of scope for F10; the
 * report editor (`RC2`) will add per-tile filter overrides on top
 * of this fanout later.
 */
export function ParetoTile() {
    const { t } = useTranslation();
    const { filters } = useCanvasFilters();
    const ready = canvasFiltersReady(filters);

    const search: ParetoSearch = {
        sourceId: filters.sourceId,
        startUtc: filters.startUtc,
        endUtc: filters.endUtc,
        machineIds: filters.machineIds,
        productIds: filters.productIds,
        axis: "Defect",
        numerator: "Real",
        opportunity: "Components",
        weight: "Count",
        topN: 10,
    };

    const reportQuery = useQuery({
        queryKey: [
            "canvas",
            "pareto",
            filters.sourceId,
            filters.startUtc,
            filters.endUtc,
            filters.machineIds?.join(",") ?? "",
            filters.productIds?.join(",") ?? "",
        ],
        queryFn: () => runParetoReport(search),
        enabled: ready,
    });

    if (!ready) {
        return (
            <Text c="dimmed" size="sm">
                {t("canvas.tiles.pareto.emptyPrompt")}
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
            <Group gap="xl" wrap="wrap">
                <Stack gap={0}>
                    <Text size="xs" c="dimmed">
                        {t("canvas.tiles.pareto.totalDefects")}
                    </Text>
                    <Text fw={600}>
                        {result.overall.defectBitCount.toLocaleString()}
                    </Text>
                </Stack>
                <Stack gap={0}>
                    <Text size="xs" c="dimmed">
                        {t("canvas.tiles.pareto.totalOpportunities")}
                    </Text>
                    <Text fw={600}>
                        {result.overall.opportunityCount.toLocaleString()}
                    </Text>
                </Stack>
                <Stack gap={0}>
                    <Text size="xs" c="dimmed">
                        {t("canvas.tiles.pareto.overallDpmoPpm")}
                    </Text>
                    <Text fw={600}>
                        {Math.round(result.overall.dpmoPpm).toLocaleString()}
                    </Text>
                </Stack>
            </Group>
            {result.rows.length > 0 ? (
                <Card withBorder padding="sm" radius="md">
                    <Title order={5} mb="xs">
                        {t("canvas.tiles.pareto.chartHeading")}
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
                        <ParetoChart
                            rows={result.rows}
                            othersBucket={result.othersBucket}
                            axis={result.axis}
                            height={260}
                        />
                    </Suspense>
                </Card>
            ) : (
                <Text size="sm" c="dimmed">
                    {t("canvas.tiles.pareto.noRows")}
                </Text>
            )}
        </Stack>
    );
}
