import { useMemo } from "react";
import ReactECharts from "echarts-for-react";
import type { EChartsOption } from "echarts";
import { useTranslation } from "react-i18next";
import type { DpmoTrendBucket, DpmoTrendLine } from "../api/dpmoTrend";
import { defectsFor, dpmoFor } from "../api/dpmoTrend";
import type { DpmoNumerator } from "../routes/dpmo-trend.search";

/**
 * Per-line DPMO trend line-chart: one point per time bucket. Buckets where
 * the line inspected nothing render as gaps (echarts `connectNulls: false`);
 * a bucket that was inspected and came back clean renders as a real zero.
 *
 * Deliberately NOT a copy of `FpyTrendChart`'s axis treatment. FPY is a
 * percentage bounded at 100 with a meaningful "good" band near the top, so
 * that chart zooms the Y axis and draws green/amber threshold guides. DPMO is
 * an unbounded rate where LOWER is better and values routinely span orders of
 * magnitude, so:
 *   - the Y axis always includes zero (a zoomed axis makes a good line look
 *     alarming), and
 *   - there are no threshold guides, because DPMO targets are per-process,
 *     not universal. Adding an arbitrary one would imply a spec that does not
 *     exist.
 *
 * The displayed numerator (Real / AOI / Dummy) is a prop, so switching it
 * never triggers a data refetch — every cell already carries all three.
 */
export type DpmoTrendChartProps = {
    buckets: DpmoTrendBucket[];
    line: DpmoTrendLine;
    numerator: DpmoNumerator;
    /** Height in px. Default 220. */
    height?: number;
};

/** Single series colour. DPMO has no band semantics to encode. */
const LINE_COLOR = "#1c7ed6";

export function DpmoTrendChart(props: DpmoTrendChartProps) {
    const { buckets, line, numerator, height = 220 } = props;
    const { t } = useTranslation();

    const option = useMemo<EChartsOption>(() => {
        const byBucket = new Map(line.points.map((p) => [p.bucketIndex, p.kpi]));
        const categories = buckets.map((b) => b.label);

        // Value per bucket (null = the line inspected nothing => gap).
        const data = buckets.map((b) => {
            const kpi = byBucket.get(b.index);
            if (!kpi) return null;
            return {
                value: Number(dpmoFor(kpi, numerator).toFixed(2)),
                _kpi: kpi,
                _defects: defectsFor(kpi, numerator),
            };
        });

        return {
            aria: { enabled: true },
            grid: { left: 44, right: 12, top: 24, bottom: 48, containLabel: true },
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "line" },
                formatter: (params: unknown) => {
                    const arr = Array.isArray(params) ? params : [params];
                    const first = arr[0] as
                        | {
                              name?: string;
                              value?: number | null;
                              data?: {
                                  _kpi?: { opportunityCount: number };
                                  _defects?: number;
                              };
                          }
                        | undefined;
                    if (!first || first.value == null) return "";
                    const kpi = first.data?._kpi;
                    const lines = [
                        `<strong>${escapeHtml(String(first.name ?? ""))}</strong>`,
                        `${t("dpmoTrend.chart.dpmo")}: ${Number(first.value).toLocaleString(undefined, {
                            maximumFractionDigits: 2,
                        })}`,
                    ];
                    if (kpi) {
                        lines.push(
                            `${t("dpmoTrend.chart.defects")}: ${(first.data?._defects ?? 0).toLocaleString()}`,
                            `${t("dpmoTrend.chart.opportunities")}: ${kpi.opportunityCount.toLocaleString()}`,
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
                // Anchored at zero: DPMO is unbounded and lower is better.
                min: 0,
                axisLabel: { fontSize: 10 },
            },
            series: [
                {
                    type: "line",
                    name: line.machineName ?? `#${line.machineId}`,
                    data,
                    connectNulls: false,
                    smooth: false,
                    symbolSize: 6,
                    itemStyle: { color: LINE_COLOR },
                    lineStyle: { width: 2, color: LINE_COLOR },
                },
            ],
        };
    }, [buckets, line, numerator, t]);

    return (
        <div
            role="img"
            aria-label={t("dpmoTrend.chart.ariaSummary", {
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
