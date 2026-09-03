import type { MachineOption } from "../../api/sources";

/** Context passed to preset builders (machine list + site time zone). */
export type ReportPresetBuildContext = {
    machines: readonly MachineOption[];
    timeZone: string;
};

/**
 * A built-in report filter template. Unlike user-saved views (stored in
 * the DB), presets ship with the SPA and resolve dynamic values such as
 * machine display name → numeric id at apply time.
 */
/** i18n keys under `reportPresets.*` usable as preset labels. */
export type ReportPresetLabelKey =
    | "reportPresets.machineNotFound"
    | "reportPresets.l6Aug.dpmoWorstParts"
    | "reportPresets.l6Aug.dpmoWorstPartsHint"
    | "reportPresets.l6Aug.paretoWorstParts"
    | "reportPresets.l6Aug.paretoWorstPartsHint"
    | "reportPresets.l6Aug.paretoRefdesTop5"
    | "reportPresets.l6Aug.paretoRefdesTop5Hint"
    | "reportPresets.l6Aug.paretoSubpanelCombined"
    | "reportPresets.l6Aug.paretoSubpanelCombinedHint"
    | "reportPresets.l6Aug.paretoSubpanelOneRefdes"
    | "reportPresets.l6Aug.paretoSubpanelOneRefdesHint";

export type ReportPreset<TFilter> = {
    id: string;
    /** i18n key under `reportPresets.*`. */
    labelKey: ReportPresetLabelKey;
    /** Optional i18n hint shown as a dim subtitle in the menu. */
    descriptionKey?: ReportPresetLabelKey;
    build: (ctx: ReportPresetBuildContext) => TFilter | null;
};
