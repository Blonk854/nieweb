import { lazy, Suspense, useEffect, useMemo, useState } from "react";
import { Alert, Badge, Button, Card, Group, Loader, Select, SimpleGrid, Stack, Text, Title } from "@mantine/core";
import { Link, useLocation, useParams } from "@tanstack/react-router";
import { IconInfoCircle } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";

import { fetchAnalyseProductDetail } from "../api/analyse";
import { ApiErrorAlert } from "../components/ApiErrorAlert";

const AnalyseProductDetailChart = lazy(() =>
    import("../charts/AnalyseProductDetailChart").then((module) => ({ default: module.AnalyseProductDetailChart })),
);

export function AnalyseProductDetailRoute() {
    const { t } = useTranslation();
    const { productId } = useParams({ from: "/analyse/product/$productId" });
    const location = useLocation();
    const productIdNumber = Number(productId);
    const sourceId = useMemo(() => {
        const search = new URLSearchParams(location.searchStr);
        return search.get("sourceId") ?? undefined;
    }, [location.searchStr]);
    const initialBucket = useMemo(() => {
        const search = new URLSearchParams(location.searchStr);
        const raw = search.get("bucket");
        return raw === "Week" ? "Week" : "Day";
    }, [location.searchStr]);
    const [bucket, setBucket] = useState<"Day" | "Week">(initialBucket);

    useEffect(() => {
        setBucket(initialBucket);
    }, [initialBucket]);

    const detail = useQuery({
        queryKey: ["analyse", "product-detail", productIdNumber, sourceId, bucket],
        queryFn: () =>
            fetchAnalyseProductDetail(productIdNumber, {
                sourceId,
                onlyLastInspection: true,
                bucket,
            }),
        enabled: Number.isFinite(productIdNumber),
        staleTime: 60 * 1000,
    });

    const decimalFormatter = useMemo(
        () =>
            new Intl.NumberFormat(undefined, {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2,
            }),
        [],
    );

    const integerFormatter = useMemo(() => new Intl.NumberFormat(), []);

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("analyse.productDetailTitle")}</Title>
                <Text c="dimmed">{detail.data?.productName ?? t("analyse.productDetailSubtitle")}</Text>
            </Stack>

            <Card withBorder padding="md" radius="md">
                <Stack gap="sm">
                    <Group justify="space-between" align="center">
                        <Text fw={600}>{t("analyse.productIdLabel")}: {productId}</Text>
                        <Button component={Link} to="/analyse" variant="light" size="xs">
                            {t("analyse.productBackAction")}
                        </Button>
                    </Group>

                    <Select
                        label={t("analyse.productDetailBucketLabel")}
                        value={bucket}
                        onChange={(value) => setBucket((value as "Day" | "Week") ?? "Day")}
                        data={[
                            { value: "Day", label: t("fpyTrend.bucket.day") },
                            { value: "Week", label: t("fpyTrend.bucket.week") },
                        ]}
                        w={180}
                        data-testid="analyse-product-detail-bucket"
                    />

                    {detail.isPending && (
                        <Group justify="center" py="lg">
                            <Loader size="sm" />
                        </Group>
                    )}

                    {detail.error && <ApiErrorAlert error={detail.error} />}

                    {detail.data && (
                        <Stack gap="sm">
                            <SimpleGrid cols={{ base: 2, sm: 3, lg: 5 }} spacing="sm">
                                <Stack gap={0}>
                                    <Text size="xs" c="dimmed">{t("analyse.kpi.totalPanels")}</Text>
                                    <Text fw={600}>{integerFormatter.format(detail.data.overallYield.totalPanels)}</Text>
                                </Stack>
                                <Stack gap={0}>
                                    <Text size="xs" c="dimmed">{t("analyse.kpi.inspectedPanels")}</Text>
                                    <Text fw={600}>{integerFormatter.format(detail.data.overallYield.inspectedPanels)}</Text>
                                </Stack>
                                <Stack gap={0}>
                                    <Text size="xs" c="dimmed">{t("analyse.kpi.fpyPercent")}</Text>
                                    <Text fw={600}>{decimalFormatter.format(detail.data.overallYield.fpyPercent)}%</Text>
                                </Stack>
                                <Stack gap={0}>
                                    <Text size="xs" c="dimmed">{t("analyse.kpi.dpmoPpm")}</Text>
                                    <Text fw={600}>{decimalFormatter.format(detail.data.overallDpmo.dpmoPpm)}</Text>
                                </Stack>
                                <Stack gap={0}>
                                    <Text size="xs" c="dimmed">{t("analyse.kpi.defectBits")}</Text>
                                    <Text fw={600}>{integerFormatter.format(detail.data.overallDpmo.defectBitCount)}</Text>
                                </Stack>
                            </SimpleGrid>

                            <Group justify="space-between" align="center">
                                <Text size="sm" fw={600}>{t("analyse.productDetailTrendTitle")}</Text>
                                <Badge variant="dot">{detail.data.filter.bucket}</Badge>
                            </Group>

                            <Suspense fallback={<Group justify="center" py="md"><Loader size="sm" /></Group>}>
                                <AnalyseProductDetailChart buckets={detail.data.buckets} trend={detail.data.trend} />
                            </Suspense>

                            <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="sm">
                                {detail.data.trend.map((point) => (
                                    <Card key={point.bucketIndex} withBorder padding="sm" radius="sm">
                                        <Stack gap={6}>
                                            <Text fw={600}>{point.label}</Text>
                                            <Text size="sm" c="dimmed">
                                                FPY {decimalFormatter.format(point.yield.fpyPercent)}% · DPMO {decimalFormatter.format(point.dpmo.dpmoPpm)}
                                            </Text>
                                            <Text size="xs" c="dimmed">
                                                {t("analyse.kpi.defectBits")}: {integerFormatter.format(point.defectBitCount)}
                                            </Text>
                                            <Group gap={4} wrap="wrap">
                                                {point.topDefectBits.length > 0 ? (
                                                    point.topDefectBits.map((defect) => (
                                                        <Badge key={defect.bitNumber} variant="light" size="xs" color="gray">
                                                            b{defect.bitNumber}: {integerFormatter.format(defect.count)}
                                                        </Badge>
                                                    ))
                                                ) : (
                                                    <Text size="xs" c="dimmed">
                                                        {t("analyse.productNoRows")}
                                                    </Text>
                                                )}
                                            </Group>
                                        </Stack>
                                    </Card>
                                ))}
                            </SimpleGrid>

                            <Stack gap={6}>
                                <Text size="sm" fw={600}>{t("analyse.productDetailDefectBreakdownTitle")}</Text>
                                {detail.data.topDefectBits.length > 0 ? (
                                    <Group gap={6}>
                                        {detail.data.topDefectBits.map((defect) => (
                                            <Badge key={defect.bitNumber} variant="light" size="xs" color="gray">
                                                b{defect.bitNumber}: {integerFormatter.format(defect.count)}
                                            </Badge>
                                        ))}
                                    </Group>
                                ) : (
                                    <Text size="sm" c="dimmed">{t("analyse.productNoRows")}</Text>
                                )}
                            </Stack>

                            {detail.data.dedupeAppliedInMemory && (
                                <Alert icon={<IconInfoCircle size={16} />} color="blue" variant="light" title={t("analyse.dedupeFallbackTitle")}>
                                    {detail.data.dedupeNote ?? t("analyse.dedupeFallbackDefault")}
                                </Alert>
                            )}
                        </Stack>
                    )}
                </Stack>
            </Card>
        </Stack>
    );
}
