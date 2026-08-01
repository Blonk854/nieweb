import { useMemo } from "react";
import ReactECharts from "echarts-for-react";
import type { EChartsOption } from "echarts";
import { useTranslation } from "react-i18next";
import type { PanelYieldByMachineRow } from "../api/reports";
import {
    colorForFpy,
    DEFAULT_FPY_THRESHOLDS,
    FPY_BAND_COLORS,
    type FpyThresholds,
} from "./fpyThresholds";

/**
 * Per-machine FPY bar chart used on the Panel Yield report.
 *
 * Design notes:
 *  - Each bar is coloured by band (green/amber/red) based on the
 *    configured thresholds. Defaults come from
 *    docs/phase-1-mvp.md §7.5 F5 (green ≥ 99.5, amber 98.0–99.5,
 *    red < 98.0) but callers can override for saved views (F8).
 *  - A dashed horizontal markLine renders the overall FPY across all
 *    machines so an operator can immediately spot machines that drag
 *    the line down.
 *  - Two thin dashed markLines render the green/amber thresholds
 *    themselves - useful when the Y-axis is zoomed to a small range.
 *  - Y-axis is clamped 0-100 by default; when every value is within a
 *    narrow band (>= 90) we zoom in to make small differences visible.
 *  - The chart wrapper carries a role="img" + aria-label so screen
 *    readers get a summary. ECharts' own `aria` option is also enabled
 *    for series-level descriptions.
 */
export type FpyBarChartProps = {
    rows: PanelYieldByMachineRow[];
    overallFpyPercent: number;
    thresholds?: FpyThresholds;
    /** Height in px. Default 320. */
    height?: number;
    /** Optional accessible label. Defaults to a translated summary. */
    ariaLabel?: string;
};

export function FpyBarChart(props: FpyBarChartProps) {
    const {
        rows,
        overallFpyPercent,
        thresholds = DEFAULT_FPY_THRESHOLDS,
        height = 320,
        ariaLabel,
    } = props;
    const { t } = useTranslation();

    const option = useMemo<EChartsOption>(() => {
        const categories = rows.map((r) => r.machineName ?? `#${r.machineId}`);
        const values = rows.map((r) => ({
            value: Number(r.kpi.fpyPercent.toFixed(2)),
            itemStyle: { color: colorForFpy(r.kpi.fpyPercent, thresholds) },
            // Keep the raw KPI accessible in the tooltip formatter.
            _raw: r.kpi,
        }));

        const finiteValues = rows
            .map((r) => r.kpi.fpyPercent)
            .filter((v) => Number.isFinite(v));
        const minValue = finiteValues.length > 0 ? Math.min(...finiteValues) : 0;
        // Zoom in only when every machine is at least in the amber band.
        // If any bar is red, keep 0-100 so the severity is visible.
        const yMin = minValue >= thresholds.amber ? Math.max(0, Math.floor(minValue) - 1) : 0;

        return {
            aria: { enabled: true },
            grid: { left: 60, right: 20, top: 40, bottom: 40, containLabel: true },
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "shadow" },
                formatter: (params: unknown) => {
                    const arr = Array.isArray(params) ? params : [params];
                    const first = arr[0] as {
                        name?: string;
                        value?: number;
                        data?: { _raw?: PanelYieldByMachineRow["kpi"] };
                    } | undefined;
                    if (!first) return "";
                    const kpi = first.data?._raw;
                    const lines = [
                        `<strong>${escapeHtml(String(first.name ?? ""))}</strong>`,
                        `${t("panelYield.results.fpyPercent")}: ${Number(first.value ?? 0).toFixed(2)}%`,
                    ];
                    if (kpi) {
                        lines.push(
                            `${t("panelYield.results.totalPanels")}: ${kpi.totalPanels}`,
                            `${t("panelYield.results.goodPanels")}: ${kpi.goodPanels}`,
                            `${t("panelYield.results.faultyPanels")}: ${kpi.faultyPanels}`,
                        );
                    }
                    return lines.join("<br/>");
                },
            },
            xAxis: {
                type: "category",
                data: categories,
                axisLabel: { rotate: categories.length > 6 ? 30 : 0 },
                name: t("panelYield.chart.axisMachine"),
                nameLocation: "middle",
                nameGap: 32,
            },
            yAxis: {
                type: "value",
                min: yMin,
                max: 100,
                name: t("panelYield.chart.axisFpy"),
                axisLabel: { formatter: "{value}%" },
            },
            series: [
                {
                    type: "bar",
                    name: t("panelYield.results.fpyPercent"),
                    data: values,
                    barMaxWidth: 48,
                    markLine: {
                        symbol: "none",
                        silent: true,
                        lineStyle: { type: "dashed" },
                        data: [
                            {
                                yAxis: Number(overallFpyPercent.toFixed(2)),
                                lineStyle: { color: "#495057", width: 2 },
                                label: {
                                    formatter: `${t("panelYield.chart.overallFpy")}: ${overallFpyPercent.toFixed(2)}%`,
                                    position: "insideEndTop",
                                },
                            },
                            {
                                yAxis: thresholds.green,
                                lineStyle: { color: FPY_BAND_COLORS.green, width: 1, opacity: 0.5 },
                                label: {
                                    formatter: `${t("panelYield.chart.thresholdGreen")} (${thresholds.green}%)`,
                                    position: "insideStartTop",
                                },
                            },
                            {
                                yAxis: thresholds.amber,
                                lineStyle: { color: FPY_BAND_COLORS.amber, width: 1, opacity: 0.5 },
                                label: {
                                    formatter: `${t("panelYield.chart.thresholdAmber")} (${thresholds.amber}%)`,
                                    position: "insideStartBottom",
                                },
                            },
                        ],
                    },
                },
            ],
        };
    }, [rows, overallFpyPercent, thresholds, t]);

    if (rows.length === 0) {
        return (
            <div role="img" aria-label={ariaLabel ?? t("panelYield.chart.emptyChart")}>
                {/* Empty state is rendered by the parent - keep this a no-op div for layout. */}
            </div>
        );
    }

    return (
        <div role="img" aria-label={ariaLabel ?? t("panelYield.chart.ariaSummary", { count: rows.length })}>
            <ReactECharts
                option={option}
                style={{ height, width: "100%" }}
                notMerge
                lazyUpdate
            />
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
