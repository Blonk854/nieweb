/**
 * Format the delta between `now` and `pastUtc` as a compact
 * human-friendly string. Used by the source-freshness KPI card so
 * operators can tell at a glance whether they are looking at data that
 * is minutes, hours, or days old.
 *
 * `now` is a parameter (not a `new Date()` call) so tests can drive
 * the clock deterministically and so the caller can freeze time when
 * rendering the same value multiple times in a page.
 *
 * The returned tuple carries a translation key + interpolation params:
 * the actual localised string is produced by the caller via
 * `t(key, params)`. This keeps the helper testable without pulling in
 * an i18n dependency, and lets French/English/... pick their own
 * plural forms.
 */
export type RelativeTimeKey =
    | "freshness.relative.justNow"
    | "freshness.relative.secondsAgo"
    | "freshness.relative.minutesAgo"
    | "freshness.relative.hoursAgo"
    | "freshness.relative.daysAgo"
    | "freshness.relative.inFuture";

export type RelativeTime = {
    key: RelativeTimeKey;
    params: Record<string, number>;
};

const SECOND_MS = 1_000;
const MINUTE_MS = 60 * SECOND_MS;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;

export function relativeFromNow(pastUtc: Date, now: Date = new Date()): RelativeTime {
    const deltaMs = now.getTime() - pastUtc.getTime();
    if (deltaMs < 0) {
        return { key: "freshness.relative.inFuture", params: { count: 0 } };
    }
    if (deltaMs < 30 * SECOND_MS) {
        return { key: "freshness.relative.justNow", params: { count: 0 } };
    }
    if (deltaMs < MINUTE_MS) {
        return { key: "freshness.relative.secondsAgo", params: { count: Math.floor(deltaMs / SECOND_MS) } };
    }
    if (deltaMs < HOUR_MS) {
        return { key: "freshness.relative.minutesAgo", params: { count: Math.floor(deltaMs / MINUTE_MS) } };
    }
    if (deltaMs < DAY_MS) {
        return { key: "freshness.relative.hoursAgo", params: { count: Math.floor(deltaMs / HOUR_MS) } };
    }
    return { key: "freshness.relative.daysAgo", params: { count: Math.floor(deltaMs / DAY_MS) } };
}

/**
 * "Stale" band for the freshness KPI card. Kept coarse on purpose:
 *  - green : within the last hour
 *  - amber : within the last 24 hours
 *  - red   : older than a day or unknown
 */
export type FreshnessBand = "green" | "amber" | "red";

export function freshnessBand(pastUtc: Date | null, now: Date = new Date()): FreshnessBand {
    if (!pastUtc) return "red";
    const deltaMs = now.getTime() - pastUtc.getTime();
    if (deltaMs < 0) return "green"; // future timestamp - treat as fresh.
    if (deltaMs < HOUR_MS) return "green";
    if (deltaMs < DAY_MS) return "amber";
    return "red";
}
