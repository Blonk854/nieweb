import { describe, expect, it } from "vitest";
import { freshnessBand, relativeFromNow } from "./freshness";

const NOW = new Date("2026-07-21T12:00:00Z");

describe("relativeFromNow", () => {
    it("returns justNow for deltas under 30 seconds", () => {
        expect(relativeFromNow(new Date("2026-07-21T11:59:59Z"), NOW).key).toBe(
            "freshness.relative.justNow",
        );
    });

    it("returns secondsAgo for deltas 30-59 seconds", () => {
        const r = relativeFromNow(new Date("2026-07-21T11:59:15Z"), NOW);
        expect(r.key).toBe("freshness.relative.secondsAgo");
        expect(r.params.count).toBe(45);
    });

    it("returns minutesAgo for deltas 1-59 minutes", () => {
        const r = relativeFromNow(new Date("2026-07-21T11:47:00Z"), NOW);
        expect(r.key).toBe("freshness.relative.minutesAgo");
        expect(r.params.count).toBe(13);
    });

    it("returns hoursAgo for deltas 1-23 hours", () => {
        const r = relativeFromNow(new Date("2026-07-21T04:30:00Z"), NOW);
        expect(r.key).toBe("freshness.relative.hoursAgo");
        expect(r.params.count).toBe(7);
    });

    it("returns daysAgo for deltas >= 24 hours", () => {
        const r = relativeFromNow(new Date("2026-07-18T12:00:00Z"), NOW);
        expect(r.key).toBe("freshness.relative.daysAgo");
        expect(r.params.count).toBe(3);
    });

    it("returns inFuture when the timestamp is later than now", () => {
        const r = relativeFromNow(new Date("2026-07-22T00:00:00Z"), NOW);
        expect(r.key).toBe("freshness.relative.inFuture");
    });
});

describe("freshnessBand", () => {
    it("returns green within the last hour", () => {
        expect(freshnessBand(new Date("2026-07-21T11:15:00Z"), NOW)).toBe("green");
        // Exactly 59 minutes ago is still green.
        expect(freshnessBand(new Date("2026-07-21T11:01:00Z"), NOW)).toBe("green");
    });

    it("returns amber between 1 and 24 hours old", () => {
        expect(freshnessBand(new Date("2026-07-21T10:59:59Z"), NOW)).toBe("amber");
        expect(freshnessBand(new Date("2026-07-20T12:00:01Z"), NOW)).toBe("amber");
    });

    it("returns red older than 24 hours", () => {
        expect(freshnessBand(new Date("2026-07-20T11:59:59Z"), NOW)).toBe("red");
    });

    it("returns red for null (source has no PANELS rows)", () => {
        expect(freshnessBand(null, NOW)).toBe("red");
    });

    it("treats future timestamps as green (clock skew is not a defect)", () => {
        expect(freshnessBand(new Date("2026-07-22T00:00:00Z"), NOW)).toBe("green");
    });
});
