import { apiFetch } from "./client";

/**
 * Admin-only skip-classification config API. Mirrors
 * `Nieweb.Api/Endpoints/AdminSkipClassificationEndpoints.cs`. The config
 * is atomic — GET returns the current thresholds + repair-button map and
 * PUT replaces the whole unit. Persisted behind the `skip.*` app
 * parameters; consumed by the DPMO / FPY / Skip Summary reports.
 */

/** The four skip-classification meanings a repair-button label can map to. */
export type RepairButtonMeaning =
    | "Normal"
    | "ManualSkip"
    | "FalseCall"
    | "ConfirmedRealMissing";

export const REPAIR_BUTTON_MEANINGS: readonly RepairButtonMeaning[] = [
    "Normal",
    "ManualSkip",
    "FalseCall",
    "ConfirmedRealMissing",
];

export type RepairButtonMeaningDto = {
    label: string;
    meaning: RepairButtonMeaning;
};

export type SkipClassificationConfigDto = {
    missingRatioThreshold: number;
    minComponentFloor: number;
    absoluteMissingFloor: number;
    repairButtonMeanings: RepairButtonMeaningDto[];
};

export function getSkipClassificationConfig(): Promise<SkipClassificationConfigDto> {
    return apiFetch<SkipClassificationConfigDto>("/api/admin/skip-classification");
}

export function saveSkipClassificationConfig(
    body: SkipClassificationConfigDto,
): Promise<SkipClassificationConfigDto> {
    return apiFetch<SkipClassificationConfigDto>("/api/admin/skip-classification", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}
