import { useMemo } from "react";
import ReactECharts from "echarts-for-react";
import type { EChartsOption } from "echarts";
import { useTranslation } from "react-i18next";
import type { FpyTrendBucket, FpyTrendLine } from "../api/fpyTrend";
import { fpyPercentFor } from "../api/fpyTrend";
import type { FpyTrendFlavor } from "../routes/fpy-trend.search";
import {
    colorForFpy,
    DEFAULT_FPY_THRESHOLDS,
    FPY_BAND_COLORS,
    type FpyThresholds,
} from "./fpyThresholds";

/**
 * Per-line FPY trend line-chart: one point per time bucket, coloured by band
 * (green/amber/red). Buckets where the line produced no panels render as
 * gaps (echarts `connectNulls: false`). Threshold guide lines mark the
 * green/amber bands. The displayed FPY flavour (Diagnostic / AOI) is a prop
 * so switching it never triggers a data refetch.
 */
export type FpyTrendChartProps = {
    buckets: FpyTrendBucket[];
    line: FpyTrendLine;
    flavor: FpyTrendFlavor;
    thresholds?: FpyThresholds;
    /** Height in px. Default 220. */
    height?: number;
};

export function FpyTrendChart(props: FpyTrendChartProps) {
    const {
        buckets,
        line,
        flavor,
        thresholds = DEFAULT_FPY_THRESHOLDS,
        height = 220,
    } = props;
    const { t } = useTranslation();

    const option = useMemo<EChartsOption>(() => {
        const byBucket = new Map(line.points.map((p) => [p.bucketIndex, p.kpi]));
        const categories = buckets.map((b) => b.label);

        // Value per bucket (null = gap). Colour each point by its band.
        const data = buckets.map((b) => {
            const kpi = byBucket.get(b.index);
            if (!kpi) return null;
            const value = Number(fpyPercentFor(kpi, flavor).toFixed(2));
            return {
                value,
                itemStyle: { color: colorForFpy(value, thresholds) },
                _kpi: kpi,
            };
        });

        const present = data
            .filter((d): d is NonNullable<typeof d> => d !== null)
            .map((d) => d.value);
        const minValue = present.length > 0 ? Math.min(...present) : 0;
        // Zoom in when every plotted value is at least in the amber band.
        const yMin = minValue >= thresholds.amber ? Math.max(0, Math.floor(minValue) - 1) : 0;

        return {
            aria: { enabled: true },
            grid: { left: 44, right: 12, top: 24, bottom: 48, containLabel: true },
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "line" },
                formatter: (params: unknown) => {
                    const arr = Array.isArray(params) ? params : [params];
                    const first = arr[0] as {
                        name?: string;
                        value?: number | null;
                        data?: { _kpi?: { inspectedCount: number; goodAoiCount: number; faultyCount: number } };
                    } | undefined;
                    if (!first || first.value == null) return "";
                    const kpi = first.data?._kpi;
                    const lines = [
                        `<strong>${escapeHtml(String(first.name ?? ""))}</strong>`,
                        `${t("fpyTrend.chart.fpy")}: ${Number(first.value).toFixed(2)}%`,
                    ];
                    if (kpi) {
                        lines.push(
                            `${t("fpyTrend.chart.inspected")}: ${kpi.inspectedCount}`,
                            `${t("fpyTrend.chart.faulty")}: ${kpi.faultyCount}`,
                        );
                    }
                    return lines.join("<br/>");
                },
            },
            xAxis: {
                type: "category",
                data: categories,
                axisLabel: { rotate: categories.length > 6 ? 35 : 0, fontSize: 10 },
            },
            yAxis: {
                type: "value",
                min: yMin,
                max: 100,
                axisLabel: { formatter: "{value}%", fontSize: 10 },
            },
            series: [
                {
                    type: "line",
                    name: line.machineName ?? `#${line.machineId}`,
                    data,
                    connectNulls: false,
                    smooth: false,
                    symbolSize: 6,
                    lineStyle: { width: 2, color: "#868e96" },
                    markLine: {
                        symbol: "none",
                        silent: true,
                        lineStyle: { type: "dashed", width: 1, opacity: 0.5 },
                        data: [
                            {
                                yAxis: thresholds.green,
                                lineStyle: { color: FPY_BAND_COLORS.green },
                                label: { formatter: `${thresholds.green}%`, position: "insideEndTop", fontSize: 9 },
                            },
                            {
                                yAxis: thresholds.amber,
                                lineStyle: { color: FPY_BAND_COLORS.amber },
                                label: { formatter: `${thresholds.amber}%`, position: "insideEndBottom", fontSize: 9 },
                            },
                        ],
                    },
                },
            ],
        };
    }, [buckets, line, flavor, thresholds, t]);

    return (
        <div
            role="img"
            aria-label={t("fpyTrend.chart.ariaSummary", {
                line: line.machineName ?? `#${line.machineId}`,
                count: line.points.length,
            })}
        >
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
