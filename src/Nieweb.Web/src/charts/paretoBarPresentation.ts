import type { ParetoWeight } from "../routes/pareto.search";

export type ParetoBarPresentation = {
    barValue: "defectCount" | "weightedScore";
    showCumulative: boolean;
    showVitalFew: boolean;
    leftAxisLabelKey:
        | "pareto.chart.yLeftDefects"
        | "pareto.chart.yLeftDpmo"
        | "pareto.chart.yLeftPpm";
};

export function paretoBarPresentation(weight: ParetoWeight): ParetoBarPresentation {
    if (weight === "Dpmo") {
        return {
            barValue: "weightedScore",
            showCumulative: false,
            showVitalFew: false,
            leftAxisLabelKey: "pareto.chart.yLeftDpmo",
        };
    }
    if (weight === "Ppm") {
        return {
            barValue: "weightedScore",
            showCumulative: false,
            showVitalFew: false,
            leftAxisLabelKey: "pareto.chart.yLeftPpm",
        };
    }
    return {
        barValue: "defectCount",
        showCumulative: true,
        showVitalFew: true,
        leftAxisLabelKey: "pareto.chart.yLeftDefects",
    };
}
