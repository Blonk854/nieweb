import { describe, expect, it } from "vitest";
import {
    readChromeDefaults,
    writeChromeDefaults,
    resolveWindowPreset,
} from "./reportChrome";

describe("readChromeDefaults", () => {
    it("returns empty for null / empty / malformed", () => {
        expect(readChromeDefaults(null)).toEqual({});
        expect(readChromeDefaults("")).toEqual({});
        expect(readChromeDefaults("{ not json")).toEqual({});
        expect(readChromeDefaults("[1,2]")).toEqual({});
    });

    it("reads a valid source id and window preset", () => {
        expect(
            readChromeDefaults(
                JSON.stringify({ defaultSourceId: "postreflow", defaultWindowPreset: "last7d" }),
            ),
        ).toEqual({ defaultSourceId: "postreflow", defaultWindowPreset: "last7d" });
    });

    it("drops an unknown window preset", () => {
        expect(
            readChromeDefaults(JSON.stringify({ defaultWindowPreset: "last-century" }))
                .defaultWindowPreset,
        ).toBeUndefined();
    });
});

describe("writeChromeDefaults", () => {
    it("preserves unrelated chrome keys", () => {
        const existing = JSON.stringify({ headerText: "ACME", footerNote: "confidential" });
        const next = writeChromeDefaults(existing, {
            defaultSourceId: "prereflow",
            defaultWindowPreset: "today",
        });
        expect(JSON.parse(next!)).toEqual({
            headerText: "ACME",
            footerNote: "confidential",
            defaultSourceId: "prereflow",
            defaultWindowPreset: "today",
        });
    });

    it("removes cleared keys and returns null when nothing is left", () => {
        const existing = JSON.stringify({ defaultSourceId: "postreflow", defaultWindowPreset: "today" });
        expect(writeChromeDefaults(existing, {})).toBeNull();
    });

    it("keeps other keys when defaults are cleared", () => {
        const existing = JSON.stringify({ headerText: "ACME", defaultSourceId: "postreflow" });
        const next = writeChromeDefaults(existing, {});
        expect(JSON.parse(next!)).toEqual({ headerText: "ACME" });
    });

    it("round-trips through readChromeDefaults", () => {
        const written = writeChromeDefaults(null, {
            defaultSourceId: "postreflow",
            defaultWindowPreset: "last30d",
        });
        expect(readChromeDefaults(written)).toEqual({
            defaultSourceId: "postreflow",
            defaultWindowPreset: "last30d",
        });
    });
});

describe("resolveWindowPreset", () => {
    const now = new Date("2026-07-15T10:00:00Z");

    it("resolves midnight-aligned windows in UTC", () => {
        expect(resolveWindowPreset("today", "UTC", now)).toEqual({
            start: "2026-07-15T00:00",
            end: "2026-07-16T00:00",
        });
        expect(resolveWindowPreset("yesterday", "UTC", now)).toEqual({
            start: "2026-07-14T00:00",
            end: "2026-07-15T00:00",
        });
        expect(resolveWindowPreset("last7d", "UTC", now)).toEqual({
            start: "2026-07-08T00:00",
            end: "2026-07-15T00:00",
        });
        expect(resolveWindowPreset("last30d", "UTC", now)).toEqual({
            start: "2026-06-15T00:00",
            end: "2026-07-15T00:00",
        });
    });
});
