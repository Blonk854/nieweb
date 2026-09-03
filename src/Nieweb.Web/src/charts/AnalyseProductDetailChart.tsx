import { useMemo } from "react";
import ReactECharts from "echarts-for-react";
import type { EChartsOption } from "echarts";
import { useTranslation } from "react-i18next";

import type { AnalyseProductDetailResult } from "../api/analyse";

export type AnalyseProductDetailChartProps = {
    buckets: AnalyseProductDetailResult["buckets"];
    trend: AnalyseProductDetailResult["trend"];
    height?: number;
};

export function AnalyseProductDetailChart(props: AnalyseProductDetailChartProps) {
    const { buckets, trend, height = 260 } = props;
    const { t } = useTranslation();

    const option = useMemo<EChartsOption>(() => {
        const byBucket = new Map(trend.map((point) => [point.bucketIndex, point]));
        const categories = buckets.map((bucket) => bucket.label);

        const fpyData = buckets.map((bucket) => {
            const point = byBucket.get(bucket.index);
            return point ? Number(point.yield.fpyPercent.toFixed(2)) : null;
        });

        const dpmoData = buckets.map((bucket) => {
            const point = byBucket.get(bucket.index);
            return point ? Number(point.dpmo.dpmoPpm.toFixed(2)) : null;
        });

        return {
            aria: { enabled: true },
            grid: { left: 48, right: 56, top: 28, bottom: 48, containLabel: true },
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "line" },
                formatter: (params: unknown) => {
                    const series = Array.isArray(params) ? params : [params];
                    const first = series[0] as { name?: string; data?: number | null } | undefined;
                    if (!first || first.data == null) {
                        return "";
                    }

                    const bucket = buckets.find((item) => item.label === first.name);
                    if (!bucket) {
                        return "";
                    }
                    const point = byBucket.get(bucket.index);
                    if (!point) {
                        return "";
                    }

                    return [
                        `<strong>${escapeHtml(bucket.label)}</strong>`,
                        `FPY: ${point.yield.fpyPercent.toFixed(2)}%`,
                        `DPMO: ${point.dpmo.dpmoPpm.toLocaleString(undefined, { maximumFractionDigits: 2 })}`,
                        `Defect bits: ${point.defectBitCount.toLocaleString()}`,
                    ].join("<br/>");
                },
            },
            legend: { top: 0 },
            xAxis: {
                type: "category",
                data: categories,
                axisLabel: { rotate: categories.length > 6 ? 35 : 0, fontSize: 10 },
            },
            yAxis: [
                {
                    type: "value",
                    name: "FPY %",
                    min: 0,
                    max: 100,
                    axisLabel: { formatter: "{value}%", fontSize: 10 },
                },
                {
                    type: "value",
                    name: "DPMO",
                    min: 0,
                    axisLabel: { fontSize: 10 },
                },
            ],
            series: [
                {
                    type: "line",
                    name: "FPY",
                    data: fpyData,
                    connectNulls: false,
                    smooth: false,
                    symbolSize: 7,
                    yAxisIndex: 0,
                    lineStyle: { width: 2, color: "#2f9e44" },
                    itemStyle: { color: "#2f9e44" },
                },
                {
                    type: "bar",
                    name: "DPMO",
                    data: dpmoData,
                    yAxisIndex: 1,
                    barMaxWidth: 28,
                    itemStyle: { color: "#1971c2", opacity: 0.75 },
                },
            ],
        };
    }, [buckets, trend]);

    return (
        <div role="img" aria-label={t("analyse.productDetailChartAria")}>
            <ReactECharts option={option} style={{ height, width: "100%" }} notMerge lazyUpdate />
        </div>
    );
}

function escapeHtml(s: string): string {
    return s
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
}
