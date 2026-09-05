import { useMemo } from "react";
import ReactECharts from "echarts-for-react";
import type { EChartsOption } from "echarts";
import { useTranslation } from "react-i18next";
import type { ParetoRow } from "../api/pareto";
import type { ParetoAxis, ParetoWeight } from "../routes/pareto.search";
import { formatApplicableMetric } from "./formatApplicableMetric";
import { paretoBarPresentation } from "./paretoBarPresentation";
import { PARETO_COLORS } from "./paretoColors";

/**
 * Combined bar + (in count mode) cumulative-percent Pareto chart.
 * Bars follow the server ranking metric (`defectCount` or
 * `weightedScore`). Clicking a bar invokes `onBarClick`.
 */
export type ParetoChartProps = {
    rows: ParetoRow[];
    othersBucket?: ParetoRow | null;
    axis: ParetoAxis;
    weight: ParetoWeight;
    vitalFewThresholdPercent?: number;
    onBarClick?: (row: ParetoRow, index: number) => void;
    /** Height in px. Default 360. */
    height?: number;
    /** Optional accessible label. Defaults to a translated summary. */
    ariaLabel?: string;
};

type BarDatum = {
    value: number;
    itemStyle: { color: string };
    _row: ParetoRow;
    _isOthers: boolean;
};

export function ParetoChart(props: ParetoChartProps) {
    const {
        rows,
        othersBucket,
        axis,
        weight,
        vitalFewThresholdPercent = 80,
        onBarClick,
        height = 360,
        ariaLabel,
    } = props;
    const { t } = useTranslation();
    const presentation = paretoBarPresentation(weight);

    const geometry = useMemo(() => {
        const allRows: { row: ParetoRow; isOthers: boolean }[] = rows.map((row) => ({
            row,
            isOthers: false,
        }));
        if (othersBucket) {
            allRows.push({ row: othersBucket, isOthers: true });
        }
        const categories = allRows.map(({ row }) => categoryLabel(row, axis));
        const maxLabelLen = categories.reduce((m, c) => Math.max(m, c.length), 0);
        const rotateLabels = categories.length > 6 || maxLabelLen > 8;
        const rotateDeg = rotateLabels ? 40 : 0;
        const labelBand = rotateLabels
            ? Math.min(120, Math.round(maxLabelLen * 6 * Math.sin((rotateDeg * Math.PI) / 180)))
            : 14;
        return { allRows, categories, rotateLabels, rotateDeg, labelBand };
    }, [rows, othersBucket, axis]);

    const chartHeight = height + (geometry.rotateLabels ? geometry.labelBand : 0);

    const option = useMemo<EChartsOption>(() => {
        const { allRows, categories, rotateDeg } = geometry;
        const barValues: BarDatum[] = allRows.map(({ row, isOthers }) => ({
            value:
                presentation.barValue === "weightedScore"
                    ? row.weightedScore
                    : row.defectCount,
            itemStyle: {
                color: isOthers
                    ? PARETO_COLORS.others
                    : presentation.showVitalFew && row.isVitalFew
                      ? PARETO_COLORS.vitalFew
                      : PARETO_COLORS.trivialMany,
            },
            _row: row,
            _isOthers: isOthers,
        }));
        const maxBar = allRows.reduce((m, { row }) => {
            const v =
                presentation.barValue === "weightedScore"
                    ? row.weightedScore
                    : row.defectCount;
            return v > m ? v : m;
        }, 0);

        const series: EChartsOption["series"] = [
            {
                type: "bar",
                name: t(presentation.leftAxisLabelKey),
                data: barValues,
                barMaxWidth: 48,
                yAxisIndex: 0,
                cursor: onBarClick ? "pointer" : "default",
            },
        ];

        if (presentation.showCumulative) {
            const cumulativeValues = allRows.map(({ row }) =>
                Number(row.cumulativePercent.toFixed(2)),
            );
            series.push({
                type: "line",
                name: t("pareto.chart.seriesCumulative"),
                data: cumulativeValues,
                yAxisIndex: 1,
                smooth: false,
                symbol: "circle",
                symbolSize: 8,
                lineStyle: { color: PARETO_COLORS.cumulative, width: 2 },
                itemStyle: { color: PARETO_COLORS.cumulative },
                markLine: {
                    symbol: "none",
                    silent: true,
                    lineStyle: { type: "dashed" },
                    data: [
                        {
                            yAxis: vitalFewThresholdPercent,
                            lineStyle: {
                                color: PARETO_COLORS.vitalFew,
                                width: 1,
                                opacity: 0.6,
                            },
                            label: {
                                formatter: `${t("pareto.chart.vitalFew")} (${vitalFewThresholdPercent}%)`,
                                position: "insideEndTop",
                            },
                        },
                    ],
                },
            });
        }

        const yAxis: EChartsOption["yAxis"] = [
            {
                type: "value",
                name: t(presentation.leftAxisLabelKey),
                min: 0,
                max: Math.max(1, Math.ceil(maxBar * 1.05)),
            },
        ];
        if (presentation.showCumulative) {
            yAxis.push({
                type: "value",
                name: t("pareto.chart.yRightCumulative"),
                min: 0,
                max: 100,
                axisLabel: { formatter: "{value}%" },
                splitLine: { show: false },
            });
        }

        const legendData = presentation.showCumulative
            ? [t(presentation.leftAxisLabelKey), t("pareto.chart.seriesCumulative")]
            : [t(presentation.leftAxisLabelKey)];

        return {
            aria: { enabled: true },
            grid: { left: 60, right: 60, top: 40, bottom: 16, containLabel: true },
            legend: {
                data: legendData,
                top: 8,
            },
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "shadow" },
                formatter: (params: unknown) => {
                    const arr = Array.isArray(params) ? params : [params];
                    const first = arr[0] as
                        | { name?: string; data?: BarDatum; value?: number }
                        | undefined;
                    if (!first) return "";
                    const row = first.data?._row;
                    if (!row) return "";
                    const na = t("pareto.results.notApplicable");
                    const lines = [
                        `<strong>${escapeHtml(String(first.name ?? ""))}</strong>`,
                        `${t("pareto.chart.defectCount")}: ${row.defectCount}`,
                        `${t("pareto.chart.opportunityCount")}: ${formatApplicableMetric(row.opportunitiesApplicable, row.opportunityCount, (n) => String(n), na)}`,
                        `${t("pareto.chart.dpmoPpm")}: ${formatApplicableMetric(row.opportunitiesApplicable, row.dpmoPpm, (n) => String(Math.round(n)), na)}`,
                        `${t("pareto.chart.defectShare")}: ${row.defectSharePercent.toFixed(1)}%`,
                    ];
                    if (presentation.showCumulative) {
                        lines.push(
                            `${t("pareto.chart.cumulative")}: ${row.cumulativePercent.toFixed(1)}%`,
                        );
                    }
                    return lines.join("<br/>");
                },
            },
            xAxis: {
                type: "category",
                data: categories,
                axisLabel: { rotate: rotateDeg, interval: 0 },
            },
            yAxis,
            series,
        };
    }, [geometry, presentation, vitalFewThresholdPercent, onBarClick, t]);

    const onEvents = useMemo(
        () => ({
            click: (params: unknown) => {
                if (!onBarClick) return;
                const p = params as {
                    componentType?: string;
                    seriesType?: string;
                    dataIndex?: number;
                    data?: BarDatum;
                };
                if (p.componentType !== "series" || p.seriesType !== "bar") return;
                const datum = p.data;
                if (!datum || datum._isOthers) return;
                if (typeof p.dataIndex !== "number") return;
                onBarClick(datum._row, p.dataIndex);
            },
        }),
        [onBarClick],
    );

    if (rows.length === 0 && !othersBucket) {
        return (
            <div role="img" aria-label={ariaLabel ?? t("pareto.chart.emptyChart")}>
            </div>
        );
    }

    return (
        <div
            role="img"
            aria-label={
                ariaLabel ?? t("pareto.chart.ariaSummary", { count: rows.length })
            }
        >
            <ReactECharts
                option={option}
                style={{ height: chartHeight, width: "100%" }}
                notMerge
                lazyUpdate
                onEvents={onEvents}
            />
        </div>
    );
}

function categoryLabel(row: ParetoRow, axis: ParetoAxis): string {
    if (row.groupKey === null && row.groupName === null) {
        return "—";
    }
    if (row.groupName && row.groupName.length > 0) return row.groupName;
    if (row.groupKey === null) return "—";
    if (axis === "Defect") return `bit ${row.groupKey}`;
    return row.groupKey;
}

function escapeHtml(s: string): string {
    return s
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
}
