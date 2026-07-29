import { useMemo } from "react";
import ReactECharts from "echarts-for-react";
import type { EChartsOption } from "echarts";
import { useTranslation } from "react-i18next";
import type { ParetoRow } from "../api/pareto";
import type { ParetoAxis } from "../routes/pareto.search";
import { PARETO_COLORS } from "./paretoColors";

/**
 * Combined bar + cumulative-percent Pareto chart. Bars are sorted
 * descending by `defectCount` (the server already emits the correct
 * order — we don't re-sort). Clicking a bar invokes `onBarClick`
 * with the underlying row so the parent route can drive drill-in
 * (e.g. on the Defect axis, append the bit to `defectBits` and
 * re-fetch).
 *
 * Design notes:
 *  - Left Y-axis: absolute defect count (bars). Range 0..max.
 *  - Right Y-axis: cumulative percent (line). Fixed 0..100 so the
 *    classic 80% band is always visually anchored.
 *  - Vital-few threshold rendered as a dashed horizontal markLine on
 *    the right axis so operators can eyeball where the boundary sits.
 *  - Others bucket is appended as the last bar with a distinct grey
 *    fill and skipped in click handling (drilling into "everything
 *    else" is not meaningful).
 *  - `role="img"` + `aria-label` for screen readers; ECharts' own
 *    `aria` option is also enabled for series-level descriptions.
 */
export type ParetoChartProps = {
    rows: ParetoRow[];
    othersBucket?: ParetoRow | null;
    axis: ParetoAxis;
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
        vitalFewThresholdPercent = 80,
        onBarClick,
        height = 360,
        ariaLabel,
    } = props;
    const { t } = useTranslation();

    const geometry = useMemo(() => {
        const allRows: { row: ParetoRow; isOthers: boolean }[] = rows.map((row) => ({
            row,
            isOthers: false,
        }));
        if (othersBucket) {
            allRows.push({ row: othersBucket, isOthers: true });
        }
        const categories = allRows.map(({ row }) => categoryLabel(row, axis));
        // Long / numerous category names (e.g. product program names on the
        // Product axis) get rotated. We grow the *canvas height* by this
        // label band (see `chartHeight`) rather than shrinking the plot via
        // `grid.bottom`: over-reserving the bottom (manual grid.bottom +
        // `containLabel` + a large axis-name gap) drove the grid height
        // negative on long labels and collapsed the bars/line to a sliver.
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
            value: row.defectCount,
            itemStyle: {
                color: isOthers
                    ? PARETO_COLORS.others
                    : row.isVitalFew
                        ? PARETO_COLORS.vitalFew
                        : PARETO_COLORS.trivialMany,
            },
            _row: row,
            _isOthers: isOthers,
        }));
        const cumulativeValues = allRows.map(({ row }) =>
            Number(row.cumulativePercent.toFixed(2)),
        );
        const maxDefect = allRows.reduce(
            (m, { row }) => (row.defectCount > m ? row.defectCount : m),
            0,
        );

        return {
            aria: { enabled: true },
            grid: { left: 60, right: 60, top: 40, bottom: 16, containLabel: true },
            legend: {
                data: [
                    t("pareto.chart.seriesDefects"),
                    t("pareto.chart.seriesCumulative"),
                ],
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
                    const lines = [
                        `<strong>${escapeHtml(String(first.name ?? ""))}</strong>`,
                        `${t("pareto.chart.defectCount")}: ${row.defectCount}`,
                        `${t("pareto.chart.opportunityCount")}: ${row.opportunityCount}`,
                        `${t("pareto.chart.dpmoPpm")}: ${Math.round(row.dpmoPpm)}`,
                        `${t("pareto.chart.defectShare")}: ${row.defectSharePercent.toFixed(1)}%`,
                        `${t("pareto.chart.cumulative")}: ${row.cumulativePercent.toFixed(1)}%`,
                    ];
                    return lines.join("<br/>");
                },
            },
            xAxis: {
                type: "category",
                data: categories,
                axisLabel: { rotate: rotateDeg, interval: 0 },
            },
            yAxis: [
                {
                    type: "value",
                    name: t("pareto.chart.yLeftDefects"),
                    min: 0,
                    max: Math.max(1, Math.ceil(maxDefect * 1.05)),
                },
                {
                    type: "value",
                    name: t("pareto.chart.yRightCumulative"),
                    min: 0,
                    max: 100,
                    axisLabel: { formatter: "{value}%" },
                    splitLine: { show: false },
                },
            ],
            series: [
                {
                    type: "bar",
                    name: t("pareto.chart.seriesDefects"),
                    data: barValues,
                    barMaxWidth: 48,
                    yAxisIndex: 0,
                    cursor: onBarClick ? "pointer" : "default",
                },
                {
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
                },
            ],
        };
    }, [geometry, vitalFewThresholdPercent, onBarClick, t]);

    // ECharts' onEvents.click delivers a `{ dataIndex, data, componentType, seriesType }`
    // shape - we act only when the user clicked a bar and it isn't the
    // synthetic Others bucket. Line-series clicks are ignored.
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
                {/* Empty state is rendered by the parent - keep this a no-op div for layout. */}
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
        // Only the Others synthetic row is fully null - keep a
        // localisation-friendly placeholder here so the axis is never
        // empty. The parent's Others handling formats a nicer label,
        // but this is the safe default.
        return "—";
    }
    if (row.groupName && row.groupName.length > 0) return row.groupName;
    if (row.groupKey === null) return "—";
    // Defect axis: group key is a bit number; keep the "bit N" form so
    // it is visually distinct from product/machine ids.
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
