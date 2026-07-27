/**
 * Report-level "chrome" defaults stored in `Report.ChromeJson`.
 *
 * `ChromeJson` is an opaque, forward-compatible JSON blob on the report
 * (it also carries other header/footer chrome). This module owns just
 * two keys — the author-chosen **default AOI source** and **default
 * time window** a viewer opens the report to. All other keys are
 * preserved untouched on write.
 *
 * The window is stored as a *relative preset* (e.g. "last 7 days")
 * rather than absolute timestamps so a saved report never goes stale.
 * {@link resolveWindowPreset} turns a preset into the wall-clock
 * `datetime-local` strings the export panel binds to.
 */
import { instantIsoToWallClock } from "../../i18n/zoneConverters";

export type ReportWindowPreset = "today" | "yesterday" | "last7d" | "last30d";

export const REPORT_WINDOW_PRESETS: readonly ReportWindowPreset[] = [
    "today",
    "yesterday",
    "last7d",
    "last30d",
];

export type ReportChromeDefaults = {
    /** SourceDescriptor.Id the report opens to (case-insensitive). */
    defaultSourceId?: string;
    /** Relative time window the report opens to. */
    defaultWindowPreset?: ReportWindowPreset;
};

function parseRaw(chromeJson: string | null | undefined): Record<string, unknown> {
    if (typeof chromeJson !== "string" || chromeJson.trim().length === 0) {
        return {};
    }
    try {
        const parsed: unknown = JSON.parse(chromeJson);
        if (parsed !== null && typeof parsed === "object" && !Array.isArray(parsed)) {
            return parsed as Record<string, unknown>;
        }
    } catch {
        // fall through to empty
    }
    return {};
}

function isWindowPreset(v: unknown): v is ReportWindowPreset {
    return typeof v === "string" && (REPORT_WINDOW_PRESETS as readonly string[]).includes(v);
}

/** Read the two default keys out of a report's `chromeJson`. */
export function readChromeDefaults(
    chromeJson: string | null | undefined,
): ReportChromeDefaults {
    const raw = parseRaw(chromeJson);
    const src =
        typeof raw.defaultSourceId === "string" && raw.defaultSourceId.trim().length > 0
            ? raw.defaultSourceId
            : undefined;
    return {
        defaultSourceId: src,
        defaultWindowPreset: isWindowPreset(raw.defaultWindowPreset)
            ? raw.defaultWindowPreset
            : undefined,
    };
}

/**
 * Merge the given defaults into an existing `chromeJson`, preserving any
 * other keys. Returns `null` when the resulting object is empty so the
 * column stays null rather than storing `"{}"`.
 */
export function writeChromeDefaults(
    chromeJson: string | null | undefined,
    defaults: ReportChromeDefaults,
): string | null {
    const raw = parseRaw(chromeJson);

    if (defaults.defaultSourceId && defaults.defaultSourceId.trim().length > 0) {
        raw.defaultSourceId = defaults.defaultSourceId;
    } else {
        delete raw.defaultSourceId;
    }

    if (defaults.defaultWindowPreset) {
        raw.defaultWindowPreset = defaults.defaultWindowPreset;
    } else {
        delete raw.defaultWindowPreset;
    }

    return Object.keys(raw).length > 0 ? JSON.stringify(raw) : null;
}

/**
 * Resolve a window preset to `{ start, end }` wall-clock strings in the
 * `YYYY-MM-DDTHH:mm` shape the `datetime-local` inputs use, interpreted
 * in `timeZone`. Windows are midnight-aligned half-open ranges.
 */
export function resolveWindowPreset(
    preset: ReportWindowPreset,
    timeZone: string,
    now: Date = new Date(),
): { start: string; end: string } {
    const todayDate = instantIsoToWallClock(now.toISOString(), timeZone, "T").slice(0, 10);

    // Offset a Y-M-D date by whole days via a UTC-noon anchor (safe
    // across month / DST boundaries), returning the resulting Y-M-D.
    const offsetDate = (baseDate: string, days: number): string => {
        const anchor = new Date(`${baseDate}T12:00:00Z`);
        anchor.setUTCDate(anchor.getUTCDate() + days);
        return anchor.toISOString().slice(0, 10);
    };

    const at = (date: string) => `${date}T00:00`;

    switch (preset) {
        case "today":
            return { start: at(todayDate), end: at(offsetDate(todayDate, 1)) };
        case "yesterday":
            return { start: at(offsetDate(todayDate, -1)), end: at(todayDate) };
        case "last7d":
            return { start: at(offsetDate(todayDate, -7)), end: at(todayDate) };
        case "last30d":
            return { start: at(offsetDate(todayDate, -30)), end: at(todayDate) };
    }
}
