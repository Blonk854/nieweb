import { lazy, Suspense } from "react";
import { Alert, Card, Group, Loader, Stack, Text, Title } from "@mantine/core";
import { IconAlertTriangle } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { runParetoFromTile } from "../../../api/pareto";
import type { CanvasFilters } from "../FilterContext";
import type { TileProps } from "./registry";
import {
    canvasFiltersReady,
    useCanvasFilters,
} from "../FilterContext";

const ParetoChart = lazy(() =>
    import("../../../charts/ParetoChart").then((m) => ({
        default: m.ParetoChart,
    })),
);

export function paretoTileQueryKey(
    filters: CanvasFilters,
    configJson: string,
): readonly unknown[] {
    return [
        "canvas",
        "pareto",
        filters.sourceId,
        filters.startUtc,
        filters.endUtc,
        filters.machineIds,
        filters.productIds,
        configJson,
    ];
}

export function ParetoTile({ config }: TileProps) {
    const { t } = useTranslation();
    const { filters } = useCanvasFilters();
    const ready = canvasFiltersReady(filters);
    const configJson = config ?? "{}";

    const reportQuery = useQuery({
        queryKey: paretoTileQueryKey(filters, configJson),
        queryFn: () =>
            runParetoFromTile({
                sourceId: filters.sourceId!,
                startUtc: filters.startUtc!,
                endUtc: filters.endUtc!,
                machineIds: filters.machineIds,
                productIds: filters.productIds,
                configJson,
            }),
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
                            weight={result.weight}
                            vitalFewThresholdPercent={result.vitalFewThresholdPercent}
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
