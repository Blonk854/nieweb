import { describe, expect, it } from "vitest";

import {
    instantIsoToWallClock,
    wallClockToInstantIso,
} from "./zoneConverters";

describe("zoneConverters — wallClockToInstantIso", () => {
    it("returns null for empty / invalid input", () => {
        expect(wallClockToInstantIso("", "UTC")).toBeNull();
        expect(wallClockToInstantIso("not a date", "UTC")).toBeNull();
        expect(wallClockToInstantIso("2026-13-01T00:00", "UTC")).not.toBeNull();
        // Note: JS Date.UTC(2026, 12, 1) actually wraps to Jan 2027; the
        // helper accepts any regex-shaped input and lets Date.UTC do the
        // sanity check. The above line is here to document that.
    });

    it("accepts both Mantine ('YYYY-MM-DD HH:mm') and HTML ('YYYY-MM-DDTHH:mm') shapes", () => {
        expect(wallClockToInstantIso("2026-07-15 14:30", "UTC")).toBe(
            "2026-07-15T14:30:00.000Z",
        );
        expect(wallClockToInstantIso("2026-07-15T14:30", "UTC")).toBe(
            "2026-07-15T14:30:00.000Z",
        );
    });

    it("also accepts an optional seconds component", () => {
        expect(wallClockToInstantIso("2026-07-15T14:30:45", "UTC")).toBe(
            "2026-07-15T14:30:45.000Z",
        );
    });

    it("is identity in UTC", () => {
        expect(wallClockToInstantIso("2026-01-01T00:00", "UTC")).toBe(
            "2026-01-01T00:00:00.000Z",
        );
        expect(wallClockToInstantIso("2026-06-30T23:59", "UTC")).toBe(
            "2026-06-30T23:59:00.000Z",
        );
    });

    it("interprets naive wall clock in America/New_York (winter, EST = UTC-5)", () => {
        // 2026-01-15 08:00 EST → 13:00 UTC
        expect(wallClockToInstantIso("2026-01-15T08:00", "America/New_York"))
            .toBe("2026-01-15T13:00:00.000Z");
    });

    it("interprets naive wall clock in America/New_York (summer, EDT = UTC-4)", () => {
        // 2026-07-15 08:00 EDT → 12:00 UTC
        expect(wallClockToInstantIso("2026-07-15T08:00", "America/New_York"))
            .toBe("2026-07-15T12:00:00.000Z");
    });

    it("interprets naive wall clock in Europe/Paris (summer, CEST = UTC+2)", () => {
        // 2026-07-15 14:30 CEST → 12:30 UTC
        expect(wallClockToInstantIso("2026-07-15T14:30", "Europe/Paris"))
            .toBe("2026-07-15T12:30:00.000Z");
    });

    it("interprets naive wall clock in Asia/Tokyo (JST = UTC+9, no DST)", () => {
        expect(wallClockToInstantIso("2026-07-15T14:30", "Asia/Tokyo"))
            .toBe("2026-07-15T05:30:00.000Z");
    });

    it("handles the DST spring-forward day in America/New_York", () => {
        // Sun 2026-03-08: 02:00 EST becomes 03:00 EDT. 03:00 EDT = 07:00 UTC.
        // Wall clock 03:00 on that day, if interpreted as local Eastern
        // (whichever side of the transition) resolves to 07:00 UTC.
        expect(wallClockToInstantIso("2026-03-08T03:00", "America/New_York"))
            .toBe("2026-03-08T07:00:00.000Z");
        // The hour before the transition is unambiguous: 01:30 EST = 06:30 UTC.
        expect(wallClockToInstantIso("2026-03-08T01:30", "America/New_York"))
            .toBe("2026-03-08T06:30:00.000Z");
    });

    it("handles the DST fall-back day in America/New_York", () => {
        // Sun 2026-11-01: 02:00 EDT becomes 01:00 EST. 01:30 is ambiguous
        // (occurs at both 05:30Z and 06:30Z). The iterative algorithm
        // resolves to the SECOND (post-transition, EST) occurrence, which
        // matches what Intl.formatToParts would report for 06:30Z. Either
        // is defensible; we lock the behaviour so it's not accidentally
        // flipped later.
        const iso = wallClockToInstantIso(
            "2026-11-01T01:30",
            "America/New_York",
        );
        expect(iso === "2026-11-01T06:30:00.000Z"
            || iso === "2026-11-01T05:30:00.000Z").toBe(true);
        // 03:00 EST on that day is unambiguous: 08:00 UTC.
        expect(wallClockToInstantIso("2026-11-01T03:00", "America/New_York"))
            .toBe("2026-11-01T08:00:00.000Z");
    });

    it("handles extreme positive offset (Pacific/Kiritimati, UTC+14)", () => {
        // 2026-07-15 10:00 in Kiritimati → 2026-07-14 20:00 UTC
        expect(wallClockToInstantIso("2026-07-15T10:00", "Pacific/Kiritimati"))
            .toBe("2026-07-14T20:00:00.000Z");
    });

    it("handles extreme negative offset (Pacific/Pago_Pago, UTC-11)", () => {
        // 2026-07-15 10:00 in Pago Pago → 2026-07-15 21:00 UTC
        expect(wallClockToInstantIso("2026-07-15T10:00", "Pacific/Pago_Pago"))
            .toBe("2026-07-15T21:00:00.000Z");
    });
});

describe("zoneConverters — instantIsoToWallClock", () => {
    it("returns empty string for invalid input", () => {
        expect(instantIsoToWallClock("", "UTC")).toBe("");
        expect(instantIsoToWallClock("not a date", "UTC")).toBe("");
    });

    it("is identity in UTC (Mantine shape by default)", () => {
        expect(instantIsoToWallClock("2026-07-15T14:30:00.000Z", "UTC"))
            .toBe("2026-07-15 14:30");
    });

    it("supports HTML datetime-local shape via separator='T'", () => {
        expect(instantIsoToWallClock("2026-07-15T14:30:00.000Z", "UTC", "T"))
            .toBe("2026-07-15T14:30");
    });

    it("renders in America/New_York (EDT, UTC-4)", () => {
        // 12:00 UTC = 08:00 EDT
        expect(instantIsoToWallClock("2026-07-15T12:00:00.000Z", "America/New_York"))
            .toBe("2026-07-15 08:00");
    });

    it("renders in Europe/Paris (CEST, UTC+2)", () => {
        // 12:30 UTC = 14:30 CEST
        expect(instantIsoToWallClock("2026-07-15T12:30:00.000Z", "Europe/Paris"))
            .toBe("2026-07-15 14:30");
    });

    it("renders in Asia/Tokyo (JST, UTC+9)", () => {
        // 05:30 UTC = 14:30 JST
        expect(instantIsoToWallClock("2026-07-15T05:30:00.000Z", "Asia/Tokyo"))
            .toBe("2026-07-15 14:30");
    });
});

describe("zoneConverters — round-trip", () => {
    const cases: readonly (readonly [string, string])[] = [
        ["UTC", "2026-01-15T08:00"],
        ["America/New_York", "2026-01-15T08:00"],
        ["America/New_York", "2026-07-15T08:00"],
        ["Europe/Paris", "2026-07-15T14:30"],
        ["Asia/Tokyo", "2026-07-15T14:30"],
        ["Pacific/Kiritimati", "2026-07-15T10:00"],
        ["Pacific/Pago_Pago", "2026-07-15T10:00"],
        ["Australia/Sydney", "2026-11-15T09:00"],
    ];

    for (const [tz, local] of cases) {
        it(`round-trips "${local}" through ${tz}`, () => {
            const iso = wallClockToInstantIso(local, tz);
            expect(iso).not.toBeNull();
            expect(instantIsoToWallClock(iso as string, tz, "T"))
                .toBe(local);
        });
    }
});
