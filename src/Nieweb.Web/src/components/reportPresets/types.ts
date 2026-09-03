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
export type ReportPreset<TFilter> = {
    id: string;
    /** i18n key under `reportPresets.*`. */
    labelKey: string;
    /** Optional i18n hint shown as a dim subtitle in the menu. */
    descriptionKey?: string;
    build: (ctx: ReportPresetBuildContext) => TFilter | null;
};
