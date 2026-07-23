import { describe, expect, it } from "vitest";
import {
    DEFAULT_TIME_BUCKET,
    TIME_BUCKETS,
    parseTimeBucket,
    timeBucketFixedMinutes,
    timeBucketToSlug,
    type TimeBucket,
} from "./timeDecomposition";

/**
 * Parity spec for the client-side {@link TimeBucket} mirror.
 * Kebab-case slugs must match the server's `TryParseEnumAlias<TimeBucket>`
 * accepted forms (see `ReportEndpoints.Trend.cs`).
 */

const EXPECTED_ORDER: TimeBucket[] = [
    "Hour1",
    "Hour3",
    "Hour6",
    "Hour12",
    "Shift",
    "Day",
    "Week",
    "Month",
];

const EXPECTED_SLUGS: [TimeBucket, string][] = [
    ["Hour1", "hour-1"],
    ["Hour3", "hour-3"],
    ["Hour6", "hour-6"],
    ["Hour12", "hour-12"],
    ["Shift", "shift"],
    ["Day", "day"],
    ["Week", "week"],
    ["Month", "month"],
];

describe("timeDecomposition — enum mirror", () => {
    it("lists every TimeBucket in canonical order", () => {
        expect(TIME_BUCKETS).toEqual(EXPECTED_ORDER);
    });

    it("defaults to Hour1 (matches CR3 fixtures)", () => {
        expect(DEFAULT_TIME_BUCKET).toBe("Hour1");
    });

    it.each(EXPECTED_SLUGS)("emits '%s' as the '%s' slug", (bucket, slug) => {
        expect(timeBucketToSlug(bucket)).toBe(slug);
    });
});

describe("timeDecomposition — parseTimeBucket", () => {
    it.each(EXPECTED_SLUGS)("parses '%s' slug back to '%s'", (bucket, slug) => {
        expect(parseTimeBucket(slug)).toBe(bucket);
    });

    it("accepts PascalCase enum member names (server also accepts these)", () => {
        expect(parseTimeBucket("Hour1")).toBe("Hour1");
        expect(parseTimeBucket("SHIFT")).toBe("Shift");
        expect(parseTimeBucket("month")).toBe("Month");
    });

    it("accepts snake_case for good measure", () => {
        expect(parseTimeBucket("hour_1")).toBe("Hour1");
        expect(parseTimeBucket("hour_12")).toBe("Hour12");
    });

    it("returns null for unknown / empty inputs so callers can fall back", () => {
        expect(parseTimeBucket(null)).toBeNull();
        expect(parseTimeBucket(undefined)).toBeNull();
        expect(parseTimeBucket("")).toBeNull();
        expect(parseTimeBucket("quarter")).toBeNull();
    });
});

describe("timeDecomposition — timeBucketFixedMinutes", () => {
    it("returns fixed minutes for hour buckets", () => {
        expect(timeBucketFixedMinutes("Hour1")).toBe(60);
        expect(timeBucketFixedMinutes("Hour3")).toBe(180);
        expect(timeBucketFixedMinutes("Hour6")).toBe(360);
        expect(timeBucketFixedMinutes("Hour12")).toBe(720);
    });

    it("returns 24h for Day and 7d for Week", () => {
        expect(timeBucketFixedMinutes("Day")).toBe(24 * 60);
        expect(timeBucketFixedMinutes("Week")).toBe(7 * 24 * 60);
    });

    it("returns null for Shift and Month (variable-length)", () => {
        expect(timeBucketFixedMinutes("Shift")).toBeNull();
        expect(timeBucketFixedMinutes("Month")).toBeNull();
    });
});
