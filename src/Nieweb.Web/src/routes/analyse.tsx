import { useEffect, useMemo, useState } from "react";
import {
    Alert,
    Badge,
    Button,
    Card,
    Group,
    Loader,
    Select,
    SegmentedControl,
    SimpleGrid,
    Stack,
    Text,
    Title,
} from "@mantine/core";
import { IconAlertTriangle, IconPlugConnectedX } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";

import {
    fetchAnalyseContracts,
    fetchAnalyseCpCpk,
    fetchAnalyseLinePerformanceSummary,
    fetchAnalyseLiveSummary,
    fetchAnalysePanelSummary,
    fetchAnalyseProductSummary,
} from "../api/analyse";
import { getAuthConfig } from "../api/auth";
import { fetchSources } from "../api/sources";
import { ApiErrorAlert } from "../components/ApiErrorAlert";

export function AnalyseRoute() {
    const { t } = useTranslation();
    const [selectedSourceId, setSelectedSourceId] = useState<string | null>(null);
    const [productSort, setProductSort] = useState<"defectBits" | "fpy" | "dpmo">("defectBits");
    const [panelSort, setPanelSort] = useState<"defectBits" | "barcode" | "date">("defectBits");
    const integerFormatter = useMemo(() => new Intl.NumberFormat(), []);
    const decimalFormatter = useMemo(
        () =>
            new Intl.NumberFormat(undefined, {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2,
            }),
        [],
    );

    const formatInt = (value: number) => integerFormatter.format(value);
    const formatDecimal = (value: number) => decimalFormatter.format(value);

    const authConfig = useQuery({
        queryKey: ["auth", "config"],
        queryFn: getAuthConfig,
        staleTime: 5 * 60 * 1000,
        retry: 1,
    });

    const sources = useQuery({
        queryKey: ["sources"],
        queryFn: fetchSources,
        staleTime: 60 * 1000,
    });

    const defaultSourceId = useMemo(() => {
        const rows = sources.data ?? [];
        if (rows.length === 0) return undefined;
        return (rows.find((s) => s.available) ?? rows[0]).id;
    }, [sources.data]);

    useEffect(() => {
        if (!defaultSourceId) {
            setSelectedSourceId(null);
            return;
        }

        const rows = sources.data ?? [];
        const stillExists =
            selectedSourceId !== null && rows.some((s) => s.id === selectedSourceId);
        if (!stillExists) {
            setSelectedSourceId(defaultSourceId);
        }
    }, [defaultSourceId, selectedSourceId, sources.data]);

    const contracts = useQuery({
        queryKey: ["analyse", "contracts", selectedSourceId],
        queryFn: () =>
            fetchAnalyseContracts({
                sourceId: selectedSourceId ?? undefined,
                onlyLastInspection: true,
            }),
        enabled: Boolean(selectedSourceId),
        staleTime: 60 * 1000,
    });

    const liveSummary = useQuery({
        queryKey: ["analyse", "live-summary", selectedSourceId],
        queryFn: () =>
            fetchAnalyseLiveSummary({
                sourceId: selectedSourceId ?? undefined,
                onlyLastInspection: true,
            }),
        enabled: Boolean(selectedSourceId),
        staleTime: 60 * 1000,
    });

    const linePerformance = useQuery({
        queryKey: ["analyse", "line-performance-summary", selectedSourceId],
        queryFn: () =>
            fetchAnalyseLinePerformanceSummary({
                sourceId: selectedSourceId ?? undefined,
                onlyLastInspection: true,
            }),
        enabled: Boolean(selectedSourceId),
        staleTime: 60 * 1000,
    });

    const productSummary = useQuery({
        queryKey: ["analyse", "product-summary", selectedSourceId],
        queryFn: () =>
            fetchAnalyseProductSummary({
                sourceId: selectedSourceId ?? undefined,
                onlyLastInspection: true,
            }),
        enabled: Boolean(selectedSourceId),
        staleTime: 60 * 1000,
    });

    const panelSummary = useQuery({
        queryKey: ["analyse", "panel-summary", selectedSourceId],
        queryFn: () =>
            fetchAnalysePanelSummary({
                sourceId: selectedSourceId ?? undefined,
                onlyLastInspection: true,
            }),
        enabled: Boolean(selectedSourceId),
        staleTime: 60 * 1000,
    });

    const cpCpk = useQuery({
        queryKey: ["analyse", "cp-cpk", selectedSourceId],
        queryFn: () =>
            fetchAnalyseCpCpk({
                sourceId: selectedSourceId ?? undefined,
                onlyLastInspection: true,
            }),
        enabled: Boolean(selectedSourceId),
        staleTime: 60 * 1000,
    });

    const sortedProducts = useMemo(() => {
        const rows = productSummary.data?.products ?? [];
        const next = [...rows];
        switch (productSort) {
            case "fpy":
                next.sort((a, b) =>
                    a.yield.fpyPercent - b.yield.fpyPercent
                    || b.defectBitCount - a.defectBitCount
                    || a.productId - b.productId,
                );
                break;
            case "dpmo":
                next.sort((a, b) =>
                    b.dpmo.dpmoPpm - a.dpmo.dpmoPpm
                    || b.defectBitCount - a.defectBitCount
                    || a.productId - b.productId,
                );
                break;
            case "defectBits":
            default:
                next.sort((a, b) =>
                    b.defectBitCount - a.defectBitCount
                    || b.dpmo.dpmoPpm - a.dpmo.dpmoPpm
                    || a.productId - b.productId,
                );
                break;
        }
        return next;
    }, [productSummary.data?.products, productSort]);

    const sortedPanels = useMemo(() => {
        const rows = panelSummary.data?.panels ?? [];
        const next = [...rows];
        switch (panelSort) {
            case "barcode":
                next.sort((a, b) =>
                    a.barcode.localeCompare(b.barcode)
                    || b.defectBitCount - a.defectBitCount
                    || a.panelId - b.panelId,
                );
                break;
            case "date":
                next.sort((a, b) =>
                    b.panelUtc.localeCompare(a.panelUtc)
                    || b.defectBitCount - a.defectBitCount
                    || a.panelId - b.panelId,
                );
                break;
            case "defectBits":
            default:
                next.sort((a, b) =>
                    b.defectBitCount - a.defectBitCount
                    || b.panelUtc.localeCompare(a.panelUtc)
                    || a.panelId - b.panelId,
                );
                break;
        }
        return next;
    }, [panelSummary.data?.panels, panelSort]);

    if (authConfig.isPending || sources.isPending) {
        return (
            <Group justify="center" py="xl">
                <Loader size="sm" />
            </Group>
        );
    }

    if (authConfig.error) {
        return <ApiErrorAlert error={authConfig.error} />;
    }

    if (sources.error) {
        return <ApiErrorAlert error={sources.error} />;
    }

    if (!authConfig.data?.analyseEnabled) {
        return (
            <Alert
                color="yellow"
                icon={<IconAlertTriangle size={18} />}
                title={t("analyse.forbiddenTitle")}
                role="alert"
            >
                {t("analyse.forbidden")}
            </Alert>
        );
    }

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("analyse.title")}</Title>
                <Text c="dimmed">{t("analyse.subtitle")}</Text>
            </Stack>

            <Card withBorder padding="md" radius="md">
                <Group justify="space-between" align="flex-end">
                    <Select
                        data-testid="analyse-source-select"
                        label={t("analyse.sourceLabel")}
                        value={selectedSourceId}
                        data={(sources.data ?? []).map((s) => ({
                            value: s.id,
                            label: `${s.displayName} (${s.id})`,
                        }))}
                        onChange={setSelectedSourceId}
                        disabled={(sources.data?.length ?? 0) === 0}
                        description={t("analyse.sourceAutoHint")}
                        w={360}
                    />
                    <Badge variant="light">ANA-02 scaffold</Badge>
                </Group>
            </Card>

            {liveSummary.error && <ApiErrorAlert error={liveSummary.error} />}
            {liveSummary.isPending && (
                <Group justify="center" py="lg">
                    <Loader size="sm" />
                </Group>
            )}

            {liveSummary.data && (
                <Card withBorder padding="md" radius="md" data-testid="analyse-live-summary-card">
                    <Stack gap="xs">
                        <Group justify="space-between">
                            <Text fw={600}>{t("analyse.liveSummaryTitle")}</Text>
                            <Badge variant="light">ANA-03</Badge>
                        </Group>
                        <SimpleGrid cols={{ base: 2, sm: 3, lg: 6 }} spacing="sm">
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.totalPanels")}</Text>
                                <Text fw={600}>{liveSummary.data.kpi.totalPanels}</Text>
                            </Stack>
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.inspectedPanels")}</Text>
                                <Text fw={600}>{liveSummary.data.kpi.inspectedPanels}</Text>
                            </Stack>
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.goodPanels")}</Text>
                                <Text fw={600}>{liveSummary.data.kpi.goodPanels}</Text>
                            </Stack>
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.faultyPanels")}</Text>
                                <Text fw={600}>{liveSummary.data.kpi.faultyPanels}</Text>
                            </Stack>
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.notInspectedPanels")}</Text>
                                <Text fw={600}>{liveSummary.data.kpi.notInspectedPanels}</Text>
                            </Stack>
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.fpyPercent")}</Text>
                                <Text fw={600}>{liveSummary.data.kpi.fpyPercent.toFixed(2)}%</Text>
                            </Stack>
                        </SimpleGrid>
                        {liveSummary.data.dedupeAppliedInMemory && (
                            <Alert color="blue" variant="light" title={t("analyse.dedupeFallbackTitle")}> 
                                {liveSummary.data.dedupeNote ?? t("analyse.dedupeFallbackDefault")}
                            </Alert>
                        )}
                    </Stack>
                </Card>
            )}

            {linePerformance.error && <ApiErrorAlert error={linePerformance.error} />}
            {linePerformance.isPending && (
                <Group justify="center" py="lg">
                    <Loader size="sm" />
                </Group>
            )}

            {linePerformance.data && (
                <Card withBorder padding="md" radius="md" data-testid="analyse-line-performance-card">
                    <Stack gap="xs">
                        <Group justify="space-between">
                            <Text fw={600}>{t("analyse.linePerformanceTitle")}</Text>
                            <Badge variant="light">ANA-03</Badge>
                        </Group>
                        <SimpleGrid cols={{ base: 2, sm: 3, lg: 4 }} spacing="sm">
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.totalPanels")}</Text>
                                <Text fw={600}>{linePerformance.data.overallYield.totalPanels}</Text>
                            </Stack>
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.fpyPercent")}</Text>
                                <Text fw={600}>{linePerformance.data.overallYield.fpyPercent.toFixed(2)}%</Text>
                            </Stack>
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.dpmoPpm")}</Text>
                                <Text fw={600}>{linePerformance.data.overallDpmo.dpmoPpm.toFixed(2)}</Text>
                            </Stack>
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.defectBits")}</Text>
                                <Text fw={600}>{linePerformance.data.overallDpmo.defectBitCount}</Text>
                            </Stack>
                        </SimpleGrid>
                        <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="sm">
                            {linePerformance.data.byMachine.slice(0, 3).map((row) => (
                                <Card key={row.machineId} withBorder padding="sm" radius="sm">
                                    <Stack gap={2}>
                                        <Text fw={600}>{row.machineName ?? `${row.machineId}`}</Text>
                                        <Text size="sm" c="dimmed">
                                            FPY {row.yield.fpyPercent.toFixed(2)}% · DPMO {row.dpmo.dpmoPpm.toFixed(2)}
                                        </Text>
                                    </Stack>
                                </Card>
                            ))}
                        </SimpleGrid>
                        {linePerformance.data.dedupeAppliedInMemory && (
                            <Alert color="blue" variant="light" title={t("analyse.dedupeFallbackTitle")}>
                                {linePerformance.data.dedupeNote ?? t("analyse.dedupeFallbackDefault")}
                            </Alert>
                        )}
                    </Stack>
                </Card>
            )}

            {productSummary.error && <ApiErrorAlert error={productSummary.error} />}
            {productSummary.isPending && (
                <Group justify="center" py="lg">
                    <Loader size="sm" />
                </Group>
            )}

            {productSummary.data && (
                <Card withBorder padding="md" radius="md" data-testid="analyse-product-summary-card">
                    <Stack gap="sm">
                        <Group justify="space-between">
                            <Text fw={600}>{t("analyse.productTitle")}</Text>
                            <Badge variant="light">ANA-04</Badge>
                        </Group>
                        <Text size="sm" c="dimmed">
                            {t("analyse.productOverviewCaption", {
                                count: productSummary.data.products.length,
                            })}
                        </Text>
                        <SimpleGrid cols={{ base: 2, sm: 3, lg: 5 }} spacing="sm">
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.totalPanels")}</Text>
                                <Text fw={600}>{formatInt(productSummary.data.overallYield.totalPanels)}</Text>
                            </Stack>
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.inspectedPanels")}</Text>
                                <Text fw={600}>{formatInt(productSummary.data.overallYield.inspectedPanels)}</Text>
                            </Stack>
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.fpyPercent")}</Text>
                                <Text fw={600}>{formatDecimal(productSummary.data.overallYield.fpyPercent)}%</Text>
                            </Stack>
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.dpmoPpm")}</Text>
                                <Text fw={600}>{formatDecimal(productSummary.data.overallDpmo.dpmoPpm)}</Text>
                            </Stack>
                            <Stack gap={0}>
                                <Text size="xs" c="dimmed">{t("analyse.kpi.defectBits")}</Text>
                                <Text fw={600}>{formatInt(productSummary.data.overallDpmo.defectBitCount)}</Text>
                            </Stack>
                        </SimpleGrid>
                        <Stack gap="xs">
                            <Group justify="space-between">
                                <Text size="sm" fw={600}>{t("analyse.productTopCaption")}</Text>
                                <Group gap="xs">
                                    <Badge variant="dot">{formatInt(productSummary.data.products.length)}</Badge>
                                    <SegmentedControl
                                        data-testid="analyse-product-sort"
                                        size="xs"
                                        value={productSort}
                                        onChange={(value) => setProductSort(value as "defectBits" | "fpy" | "dpmo")}
                                        data={[
                                            { value: "defectBits", label: t("analyse.productSortDefectBits") },
                                            { value: "fpy", label: t("analyse.productSortFpy") },
                                            { value: "dpmo", label: t("analyse.productSortDpmo") },
                                        ]}
                                        aria-label={t("analyse.productSortLabel")}
                                    />
                                </Group>
                            </Group>
                            {productSummary.data.products.length === 0 ? (
                                <Text size="sm" c="dimmed">{t("analyse.productNoRows")}</Text>
                            ) : (
                                <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="sm">
                                    {sortedProducts.slice(0, 6).map((row, index) => (
                                        <Card
                                            key={row.productId}
                                            withBorder
                                            padding="sm"
                                            radius="sm"
                                            data-testid={`analyse-product-row-${index}`}
                                        >
                                            <Stack gap={6}>
                                                <Group justify="space-between" align="flex-start">
                                                    <Stack gap={1}>
                                                        <Text fw={700} lineClamp={1}>
                                                            {row.productName ?? `${t("analyse.productIdLabel")} ${row.productId}`}
                                                        </Text>
                                                        <Text size="xs" c="dimmed">
                                                            {t("analyse.productIdLabel")}: {row.productId}
                                                        </Text>
                                                    </Stack>
                                                    <Badge variant="light">#{index + 1}</Badge>
                                                </Group>
                                                <Group gap="md" wrap="wrap">
                                                    <Stack gap={0}>
                                                        <Text size="xs" c="dimmed">{t("analyse.kpi.fpyPercent")}</Text>
                                                        <Text fw={600}>{formatDecimal(row.yield.fpyPercent)}%</Text>
                                                    </Stack>
                                                    <Stack gap={0}>
                                                        <Text size="xs" c="dimmed">{t("analyse.kpi.dpmoPpm")}</Text>
                                                        <Text fw={600}>{formatDecimal(row.dpmo.dpmoPpm)}</Text>
                                                    </Stack>
                                                    <Stack gap={0}>
                                                        <Text size="xs" c="dimmed">{t("analyse.kpi.defectBits")}</Text>
                                                        <Text fw={600}>{formatInt(row.defectBitCount)}</Text>
                                                    </Stack>
                                                </Group>
                                                {row.topDefectBits.length > 0 && (
                                                    <Group gap={6}>
                                                        <Text size="xs" c="dimmed">{t("analyse.productDefectPreview")}</Text>
                                                        {row.topDefectBits.map((defect) => (
                                                            <Badge key={defect.bitNumber} variant="light" size="xs" color="gray">
                                                                b{defect.bitNumber}: {formatInt(defect.count)}
                                                            </Badge>
                                                        ))}
                                                    </Group>
                                                )}
                                                <Group justify="flex-end">
                                                    <Button
                                                        data-testid={`analyse-product-detail-${row.productId}`}
                                                        component={Link}
                                                        to="/analyse/product/$productId"
                                                        params={{ productId: String(row.productId) }}
                                                        search={{ sourceId: selectedSourceId ?? undefined }}
                                                        variant="subtle"
                                                        size="compact-xs"
                                                    >
                                                        {t("analyse.productDetailsAction")}
                                                    </Button>
                                                </Group>
                                            </Stack>
                                        </Card>
                                    ))}
                                </SimpleGrid>
                            )}
                        </Stack>
                        {productSummary.data.dedupeAppliedInMemory && (
                            <Alert color="blue" variant="light" title={t("analyse.dedupeFallbackTitle")}>
                                {productSummary.data.dedupeNote ?? t("analyse.dedupeFallbackDefault")}
                            </Alert>
                        )}
                    </Stack>
                </Card>
            )}

            {panelSummary.error && <ApiErrorAlert error={panelSummary.error} />}
            {panelSummary.isPending && (
                <Group justify="center" py="lg">
                    <Loader size="sm" />
                </Group>
            )}

            {panelSummary.data && (
                <Card withBorder padding="md" radius="md" data-testid="analyse-panel-summary-card">
                    <Stack gap="sm">
                        <Group justify="space-between">
                            <Text fw={600}>{t("analyse.panelTitle")}</Text>
                            <Badge variant="light">ANA-05</Badge>
                        </Group>
                        <Text size="sm" c="dimmed">
                            {t("analyse.panelOverviewCaption", {
                                count: panelSummary.data.panels.length,
                                total: panelSummary.data.totalPanels,
                            })}
                        </Text>
                        <Stack gap="xs">
                            <Group justify="space-between">
                                <Text size="sm" fw={600}>{t("analyse.panelTopCaption")}</Text>
                                <Group gap="xs">
                                    <Badge variant="dot">{formatInt(panelSummary.data.panels.length)}</Badge>
                                    <SegmentedControl
                                        data-testid="analyse-panel-sort"
                                        size="xs"
                                        value={panelSort}
                                        onChange={(value) => setPanelSort(value as "defectBits" | "barcode" | "date")}
                                        data={[
                                            { value: "defectBits", label: t("analyse.panelSortDefectBits") },
                                            { value: "barcode", label: t("analyse.panelSortBarcode") },
                                            { value: "date", label: t("analyse.panelSortDate") },
                                        ]}
                                        aria-label={t("analyse.panelSortLabel")}
                                    />
                                </Group>
                            </Group>
                        {panelSummary.data.panels.length === 0 ? (
                            <Text size="sm" c="dimmed">{t("analyse.panelNoRows")}</Text>
                        ) : (
                            <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="sm">
                                {sortedPanels.slice(0, 6).map((row, index) => (
                                    <Card
                                        key={row.panelId}
                                        withBorder
                                        padding="sm"
                                        radius="sm"
                                        data-testid={`analyse-panel-row-${index}`}
                                    >
                                        <Stack gap={6}>
                                            <Group justify="space-between" align="flex-start">
                                                <Stack gap={1}>
                                                    <Text fw={700} lineClamp={1}>
                                                        {row.barcode}
                                                    </Text>
                                                    <Text size="xs" c="dimmed">
                                                        {t("analyse.panelBarcodeLabel")}: {row.barcode} · {row.productName ?? `${t("analyse.productIdLabel")} ${row.productId}`} · {row.machineName ?? row.machineId}
                                                    </Text>
                                                </Stack>
                                                <Badge variant="light">#{index + 1}</Badge>
                                            </Group>
                                            <Group gap="md" wrap="wrap">
                                                <Stack gap={0}>
                                                    <Text size="xs" c="dimmed">{t("analyse.kpi.defectBits")}</Text>
                                                    <Text fw={600}>{formatInt(row.defectBitCount)}</Text>
                                                </Stack>
                                                <Stack gap={0}>
                                                    <Text size="xs" c="dimmed">{t("analyse.kpi.fpyPercent")}</Text>
                                                    <Text fw={600}>{row.panelStatus}</Text>
                                                </Stack>
                                            </Group>
                                            {row.topDefectBits.length > 0 && (
                                                <Group gap={6}>
                                                    <Text size="xs" c="dimmed">{t("analyse.productDefectPreview")}</Text>
                                                    {row.topDefectBits.map((defect) => (
                                                        <Badge key={defect.bitNumber} variant="light" size="xs" color="gray">
                                                            b{defect.bitNumber}: {formatInt(defect.count)}
                                                        </Badge>
                                                    ))}
                                                </Group>
                                            )}
                                            <Group justify="flex-end">
                                                <Button
                                                    data-testid={`analyse-panel-trace-${row.panelId}`}
                                                    component={Link}
                                                    to="/traceability/board"
                                                    search={{ barcode: row.barcode }}
                                                    variant="subtle"
                                                    size="compact-xs"
                                                >
                                                    {t("analyse.panelOpenTraceAction")}
                                                </Button>
                                            </Group>
                                        </Stack>
                                    </Card>
                                ))}
                            </SimpleGrid>
                        )}
                        </Stack>
                        {panelSummary.data.dedupeAppliedInMemory && (
                            <Alert color="blue" variant="light" title={t("analyse.dedupeFallbackTitle")}>
                                {panelSummary.data.dedupeNote ?? t("analyse.dedupeFallbackDefault")}
                            </Alert>
                        )}
                    </Stack>
                </Card>
            )}

            {cpCpk.error && <ApiErrorAlert error={cpCpk.error} />}
            {cpCpk.isPending && (
                <Group justify="center" py="lg">
                    <Loader size="sm" />
                </Group>
            )}

            {cpCpk.data && (
                <Card withBorder padding="md" radius="md" data-testid="analyse-cp-cpk-card">
                    <Stack gap="sm">
                        <Group justify="space-between">
                            <Text fw={600}>{t("analyse.cpCpkTitle")}</Text>
                            <Badge variant="light">ANA-06</Badge>
                        </Group>
                        <Text size="sm" c="dimmed">
                            {t("analyse.cpCpkOverviewCaption", {
                                count: cpCpk.data.rows.length,
                            })}
                        </Text>
                        {cpCpk.data.rows.length === 0 ? (
                            <Text size="sm" c="dimmed">{t("analyse.cpCpkNoRows")}</Text>
                        ) : (
                            <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="sm">
                                {cpCpk.data.rows.map((row, index) => (
                                    <Card
                                        key={`${row.opportunity}-${row.axis}`}
                                        withBorder
                                        padding="sm"
                                        radius="sm"
                                        data-testid={`analyse-cp-cpk-row-${index}`}
                                    >
                                        <Stack gap={6}>
                                            <Group justify="space-between" align="flex-start">
                                                <Stack gap={1}>
                                                    <Text fw={700} lineClamp={1}>
                                                        {row.opportunity} · {row.axis}
                                                    </Text>
                                                    <Text size="xs" c="dimmed">
                                                        {t("analyse.cpCpkSampleCount")}: {formatInt(row.sampleCount)}
                                                    </Text>
                                                </Stack>
                                                {!row.toleranceConfigured && (
                                                    <Badge variant="light" color="yellow">{t("analyse.cpCpkNotConfigured")}</Badge>
                                                )}
                                            </Group>
                                            <Group gap="md" wrap="wrap">
                                                <Stack gap={0}>
                                                    <Text size="xs" c="dimmed">Cp</Text>
                                                    <Text fw={600}>{row.cp === null ? "—" : formatDecimal(row.cp)}</Text>
                                                </Stack>
                                                <Stack gap={0}>
                                                    <Text size="xs" c="dimmed">Cpk</Text>
                                                    <Text fw={600}>{row.cpk === null ? "—" : formatDecimal(row.cpk)}</Text>
                                                </Stack>
                                                <Stack gap={0}>
                                                    <Text size="xs" c="dimmed">σ</Text>
                                                    <Text fw={600}>{formatDecimal(row.stdDev)}</Text>
                                                </Stack>
                                            </Group>
                                        </Stack>
                                    </Card>
                                ))}
                            </SimpleGrid>
                        )}
                        {cpCpk.data.dedupeAppliedInMemory && (
                            <Alert color="blue" variant="light" title={t("analyse.dedupeFallbackTitle")}>
                                {cpCpk.data.dedupeNote ?? t("analyse.dedupeFallbackDefault")}
                            </Alert>
                        )}
                    </Stack>
                </Card>
            )}

            {contracts.error && <ApiErrorAlert error={contracts.error} />}
            {contracts.isPending && (
                <Group justify="center" py="lg">
                    <Loader size="sm" />
                </Group>
            )}

            {contracts.data && (
                <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
                    {contracts.data.dashboards.map((d) => (
                        <Card key={d.dashboard} withBorder padding="md" radius="md">
                            <Stack gap="xs">
                                <Group justify="space-between">
                                    <Text fw={600}>{d.dashboard}</Text>
                                    <Badge color={d.supported ? "green" : "yellow"}>
                                        {d.supported
                                            ? t("analyse.supported")
                                            : t("analyse.limited")}
                                    </Badge>
                                </Group>
                                {d.features.length === 0 && (
                                    <Text size="sm" c="dimmed">
                                        {t("analyse.noCapabilityGates")}
                                    </Text>
                                )}
                                {d.features.map((f) => (
                                    <Group key={f.featureId} justify="space-between" align="flex-start" wrap="nowrap">
                                        <Text size="sm">{f.featureId}</Text>
                                        {f.supported ? (
                                            <Badge size="xs" color="green" variant="light">
                                                {t("analyse.supported")}
                                            </Badge>
                                        ) : (
                                            <Badge
                                                size="xs"
                                                color="yellow"
                                                variant="light"
                                                leftSection={<IconPlugConnectedX size={12} />}
                                            >
                                                {f.missingCapability ?? t("analyse.postReflowOnly")}
                                            </Badge>
                                        )}
                                    </Group>
                                ))}
                            </Stack>
                        </Card>
                    ))}
                </SimpleGrid>
            )}
        </Stack>
    );
}
