/**
 * Zone-aware round-trip helpers for the report filter date-time
 * pickers.
 *
 * Every filter panel in the SPA (Panel Yield, DPMO+FPY, Pareto,
 * Canvas, admin Audit, Report Export) stores its `startUtc` / `endUtc`
 * search parameters as ISO-8601 UTC instants — that keeps the URL,
 * the API contract, and the on-server SQL invariant across users.
 * But the date-time controls those users interact with are
 * timezone-naive: Mantine's `DateTimePicker` emits a wall-clock string
 * like `"2026-07-15 14:30"` and the native `<input type="datetime-local">`
 * emits `"2026-07-15T14:30"`. Both mean "the wall clock the user
 * just typed", not any particular instant.
 *
 * These two helpers bridge the gap by interpreting that wall clock in
 * the user's configured IANA time zone. So if a user in
 * `America/New_York` types `"2026-07-15 14:30"`, the API sees
 * `"2026-07-15T18:30:00.000Z"` (14:30 EDT = 18:30 UTC).
 *
 * They are pure functions — no React, no store access — so they are
 * trivially unit-testable and can be reused from any surface.
 */

/**
 * Convert a naive wall-clock string (as typed into a picker) to a
 * UTC ISO-8601 instant, interpreting the wall clock in `timeZone`.
 *
 * Accepts either `"YYYY-MM-DDTHH:mm"` / `"YYYY-MM-DDTHH:mm:ss"`
 * (HTML `datetime-local`) or `"YYYY-MM-DD HH:mm"` /
 * `"YYYY-MM-DD HH:mm:ss"` (Mantine `DateTimePicker`, with a space
 * separator). Any other shape returns `null`.
 *
 * The algorithm is DST-safe: it makes a first guess by pretending the
 * input is UTC, then iterates once measuring the actual wall-clock
 * the guess maps to in `timeZone` and shifts by the delta. Two
 * iterations converge across any DST transition (including the
 * pathological ambiguous / skipped hour) because the second pass
 * has already crossed the boundary and its offset is stable.
 *
 * @param local Wall clock string. Empty / null-ish returns `null`.
 * @param timeZone IANA zone (`Europe/Paris`, `America/New_York`, `UTC`, …).
 * @returns `"YYYY-MM-DDTHH:mm:ss.sssZ"` or `null` on invalid input.
 */
export function wallClockToInstantIso(
    local: string,
    timeZone: string,
): string | null {
    if (!local) return null;
    const normalized = local.trim().replace(" ", "T");
    const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?$/.exec(
        normalized,
    );
    if (!match) return null;
    const [, ys, mos, ds, hs, mis, ss] = match;
    const y = Number(ys);
    const mo = Number(mos) - 1;
    const d = Number(ds);
    const h = Number(hs);
    const mi = Number(mis);
    const s = Number(ss ?? "0");

    // Target wall clock as a "UTC-flavoured" epoch — used only as the
    // reference we're trying to match after applying the zone offset.
    const targetMs = Date.UTC(y, mo, d, h, mi, s);
    if (Number.isNaN(targetMs)) return null;

    let guess = targetMs;
    for (let i = 0; i < 2; i++) {
        const zoned = wallClockOfInstant(new Date(guess), timeZone);
        guess += targetMs - zoned;
    }
    return new Date(guess).toISOString();
}

/**
 * Format a UTC ISO-8601 instant back to a naive wall clock in
 * `timeZone`, using the same shape the pickers consume.
 *
 * @param iso ISO-8601 instant. Invalid input returns `""`.
 * @param timeZone IANA zone.
 * @param separator `" "` for Mantine `DateTimePicker` (default) or
 *     `"T"` for HTML `datetime-local`.
 */
export function instantIsoToWallClock(
    iso: string,
    timeZone: string,
    separator: "T" | " " = " ",
): string {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return "";
    const parts = wallClockPartsOfInstant(d, timeZone);
    return (
        `${parts.year}-${parts.month}-${parts.day}`
        + `${separator}${parts.hour}:${parts.minute}`
    );
}

/**
 * Return the wall-clock components (year, month, day, hour, minute,
 * second) that `instant` shows in `timeZone`, formatted as strings
 * with the leading zeros the pickers expect.
 */
function wallClockPartsOfInstant(
    instant: Date,
    timeZone: string,
): Readonly<{
    year: string;
    month: string;
    day: string;
    hour: string;
    minute: string;
    second: string;
}> {
    const parts = Object.fromEntries(
        formatterFor(timeZone)
            .formatToParts(instant)
            .map((p) => [p.type, p.value]),
    ) as Record<string, string>;
    // Intl in some engines renders midnight as "24" instead of "00";
    // normalise so string comparisons are safe.
    const hour = parts.hour === "24" ? "00" : parts.hour;
    return {
        year: parts.year,
        month: parts.month,
        day: parts.day,
        hour,
        minute: parts.minute,
        second: parts.second,
    };
}

/**
 * Cache of `Intl.DateTimeFormat` instances keyed by IANA zone.
 *
 * Constructing an `Intl.DateTimeFormat` is measurably expensive
 * (allocates an ICU locale + zone rules object under the hood). The
 * report filter panels call `wallClockToInstantIso` on every render
 * of the picker, and the algorithm itself calls this twice per
 * invocation for its DST-safe two-iteration adjust — so caching cuts
 * per-render Intl work to a single object lookup after the first
 * call.
 */
const formatterCache = new Map<string, Intl.DateTimeFormat>();

function formatterFor(timeZone: string): Intl.DateTimeFormat {
    const cached = formatterCache.get(timeZone);
    if (cached) return cached;
    const fmt = new Intl.DateTimeFormat("en-US", {
        timeZone,
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
        hour12: false,
    });
    formatterCache.set(timeZone, fmt);
    return fmt;
}

/**
 * Return `Date.UTC(...)` of the wall-clock components that `instant`
 * shows in `timeZone`. Used internally by `wallClockToInstantIso` as
 * the "guess correctness" measurement — the difference between this
 * value and the target `Date.UTC(...)` of the user's typed wall clock
 * is exactly the zone offset we need to subtract.
 */
function wallClockOfInstant(instant: Date, timeZone: string): number {
    const p = wallClockPartsOfInstant(instant, timeZone);
    return Date.UTC(
        Number(p.year),
        Number(p.month) - 1,
        Number(p.day),
        Number(p.hour),
        Number(p.minute),
        Number(p.second),
    );
}
