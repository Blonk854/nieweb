import { wallClockToInstantIso } from "../../i18n/zoneConverters";
import type { DpmoSearch } from "../../routes/dpmo.search";
import type { ParetoSearch } from "../../routes/pareto.search";
import { resolveMachineId } from "./resolveMachineId";
import type { ReportPreset, ReportPresetBuildContext } from "./types";

/** Post-reflow AOI source where L6PSTAOI is registered. */
export const L6_PART_ANALYSIS_SOURCE_ID = "postreflow";

/** Machine display name for line 6 post-reflow AOI. */
export const L6_PART_ANALYSIS_MACHINE_NAME = "L6PSTAOI";

/**
 * Inclusive August 1 00:00 through exclusive September 1 00:00 in the
 * user's site time zone, for `year` (defaults to current calendar year).
 */
export function augustWindowUtc(
    timeZone: string,
    year: number = new Date().getFullYear(),
): { startUtc: string; endUtc: string } | null {
    const startUtc = wallClockToInstantIso(`${year}-08-01 00:00`, timeZone);
    const endUtc = wallClockToInstantIso(`${year}-09-01 00:00`, timeZone);
    if (!startUtc || !endUtc) return null;
    return { startUtc, endUtc };
}

function l6MachineIds(ctx: ReportPresetBuildContext): number[] | null {
    const id = resolveMachineId(ctx.machines, L6_PART_ANALYSIS_MACHINE_NAME);
    if (id === null) return null;
    return [id];
}

function l6BaseWindow(ctx: ReportPresetBuildContext): {
    sourceId: string;
    startUtc: string;
    endUtc: string;
    machineIds: number[];
} | null {
    const window = augustWindowUtc(ctx.timeZone);
    const machineIds = l6MachineIds(ctx);
    if (!window || !machineIds) return null;
    return {
        sourceId: L6_PART_ANALYSIS_SOURCE_ID,
        startUtc: window.startUtc,
        endUtc: window.endUtc,
        machineIds,
    };
}

/** DPMO presets for the L6 August part-analysis workflow. */
export const DPMO_L6_AUG_PRESETS: ReportPreset<DpmoSearch>[] = [
    {
        id: "l6-aug-dpmo-worst-parts",
        labelKey: "reportPresets.l6Aug.dpmoWorstParts",
        descriptionKey: "reportPresets.l6Aug.dpmoWorstPartsHint",
        build(ctx) {
            const base = l6BaseWindow(ctx);
            if (!base) return null;
            return {
                ...base,
                groupBy: "PartNumber",
                numerator: "Real",
                opportunity: "Components",
                skipExclusion: "Clean",
            };
        },
    },
];

/** Pareto presets for the L6 August part-analysis workflow. */
export const PARETO_L6_AUG_PRESETS: ReportPreset<ParetoSearch>[] = [
    {
        id: "l6-aug-pareto-worst-parts",
        labelKey: "reportPresets.l6Aug.paretoWorstParts",
        descriptionKey: "reportPresets.l6Aug.paretoWorstPartsHint",
        build(ctx) {
            const base = l6BaseWindow(ctx);
            if (!base) return null;
            return {
                ...base,
                axis: "PartNumber",
                numerator: "Real",
                opportunity: "Components",
                skipExclusion: "Clean",
                weight: "Dpmo",
                topN: 20,
            };
        },
    },
    {
        id: "l6-aug-pareto-refdes-top5",
        labelKey: "reportPresets.l6Aug.paretoRefdesTop5",
        descriptionKey: "reportPresets.l6Aug.paretoRefdesTop5Hint",
        build(ctx) {
            const base = l6BaseWindow(ctx);
            if (!base) return null;
            return {
                ...base,
                axis: "ReferenceDesignator",
                numerator: "Real",
                opportunity: "Components",
                skipExclusion: "Clean",
                weight: "Count",
                topN: 5,
            };
        },
    },
    {
        id: "l6-aug-pareto-subpanel-combined",
        labelKey: "reportPresets.l6Aug.paretoSubpanelCombined",
        descriptionKey: "reportPresets.l6Aug.paretoSubpanelCombinedHint",
        build(ctx) {
            const base = l6BaseWindow(ctx);
            if (!base) return null;
            return {
                ...base,
                axis: "Subpanel",
                numerator: "Real",
                opportunity: "Components",
                skipExclusion: "Clean",
                weight: "Count",
            };
        },
    },
    {
        id: "l6-aug-pareto-subpanel-one-refdes",
        labelKey: "reportPresets.l6Aug.paretoSubpanelOneRefdes",
        descriptionKey: "reportPresets.l6Aug.paretoSubpanelOneRefdesHint",
        build(ctx) {
            const base = l6BaseWindow(ctx);
            if (!base) return null;
            return {
                ...base,
                axis: "Subpanel",
                numerator: "Real",
                opportunity: "Components",
                skipExclusion: "Clean",
                weight: "Count",
            };
        },
    },
];
