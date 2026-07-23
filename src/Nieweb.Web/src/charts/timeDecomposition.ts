/**
 * Client-side mirror of the `Nieweb.Reports.Common.TimeBucket`
 * enum. The server accepts either the raw PascalCase member name
 * (`Hour1`, `Shift`, `Day`, …) or the kebab-case slug
 * (`hour-1`, `shift`, `day`, …) — Nieweb SPA emits the kebab-case
 * form to keep query strings idiomatic.
 *
 * When the C# enum grows a new member, add it here and to the
 * parity spec (`timeDecomposition.test.ts`).
 */

/** Ordered from finest to coarsest — same order as the C# enum. */
export type TimeBucket =
    | "Hour1"
    | "Hour3"
    | "Hour6"
    | "Hour12"
    | "Shift"
    | "Day"
    | "Week"
    | "Month";

/**
 * Canonical order used by every dropdown / picker in the SPA so
 * chart tiles feel consistent regardless of route. Do not reorder
 * without also updating the parity test.
 */
export const TIME_BUCKETS: readonly TimeBucket[] = [
    "Hour1",
    "Hour3",
    "Hour6",
    "Hour12",
    "Shift",
    "Day",
    "Week",
    "Month",
];

/**
 * Vieweb's default bucket for the trend / deviation charts is one
 * hour — matches CR3's snapshot fixtures.
 */
export const DEFAULT_TIME_BUCKET: TimeBucket = "Hour1";

/**
 * Kebab-case slug used on the wire (query strings, saved views).
 * The server's `TryParseEnumAlias<TimeBucket>` accepts both slug
 * and enum member name, but the SPA emits the slug.
 */
export function timeBucketToSlug(bucket: TimeBucket): string {
    switch (bucket) {
        case "Hour1":
            return "hour-1";
        case "Hour3":
            return "hour-3";
        case "Hour6":
            return "hour-6";
        case "Hour12":
            return "hour-12";
        case "Shift":
            return "shift";
        case "Day":
            return "day";
        case "Week":
            return "week";
        case "Month":
            return "month";
    }
}

/**
 * Parses a kebab-case slug or an enum member name back to a
 * {@link TimeBucket}. Returns `null` when the input is unknown so
 * callers can fall back to {@link DEFAULT_TIME_BUCKET} without
 * throwing on a stale URL.
 */
export function parseTimeBucket(raw: string | null | undefined): TimeBucket | null {
    if (raw == null) return null;
    const normalised = raw.replace(/[-_]/g, "").toLowerCase();
    switch (normalised) {
        case "hour1":
            return "Hour1";
        case "hour3":
            return "Hour3";
        case "hour6":
            return "Hour6";
        case "hour12":
            return "Hour12";
        case "shift":
            return "Shift";
        case "day":
            return "Day";
        case "week":
            return "Week";
        case "month":
            return "Month";
        default:
            return null;
    }
}

/**
 * How long a bucket spans in minutes for the fixed-duration
 * buckets. Returns `null` for {@link TimeBucket.Shift} and any
 * calendar bucket, because their span depends on site config or
 * calendar (DST for Day, leap seconds — no, that's a joke — but
 * variable-length months are real).
 *
 * Used by chart tiles to warn "your window is too narrow / too
 * wide for this bucket" — e.g. picking Hour1 with a 90-day
 * window would produce 2 160 X-axis ticks.
 */
export function timeBucketFixedMinutes(bucket: TimeBucket): number | null {
    switch (bucket) {
        case "Hour1":
            return 60;
        case "Hour3":
            return 180;
        case "Hour6":
            return 360;
        case "Hour12":
            return 720;
        case "Day":
            // Assume 24h even though DST can skew this by ±1h;
            // heuristic only.
            return 24 * 60;
        case "Week":
            return 7 * 24 * 60;
        case "Shift":
        case "Month":
            return null;
    }
}
