import { describe, it, expect } from "vitest";

import { validateFpyTrendSearch, toApiQuery } from "./fpy-trend.search";

// Regression coverage for the Line filter round-trip. The bug: `formToSearch`
// produces `lines` as a NUMBER array (e.g. [2]) during in-app navigation, but
// TanStack Router re-runs `validateSearch` on the navigated search. A previous
// `toNumberArray` delegated to a string-only `toStringArray`, which filtered
// out the numbers — so `lines` silently vanished from the URL and the API call,
// and the report never actually filtered by line.
describe("validateFpyTrendSearch / lines", () => {
    it("keeps a numeric lines array (in-app navigation shape)", () => {
        const out = validateFpyTrendSearch({ lines: [2, 7] });
        expect(out.lines).toEqual([2, 7]);
    });

    it("parses a comma-separated lines string (shared/bookmarked URL shape)", () => {
        const out = validateFpyTrendSearch({ lines: "2,7" });
        expect(out.lines).toEqual([2, 7]);
    });

    it("parses a JSON-encoded lines array string (TanStack default serializer)", () => {
        // TanStack Router serialises `lines: [2]` to the URL as `lines=[2]`,
        // which parses back to the array [2] before validation.
        const out = validateFpyTrendSearch({ lines: [2] });
        expect(out.lines).toEqual([2]);
    });

    it("drops non-finite / empty entries", () => {
        expect(validateFpyTrendSearch({ lines: [] }).lines).toBeUndefined();
        expect(validateFpyTrendSearch({ lines: "abc" }).lines).toBeUndefined();
    });

    it("serialises lines to a comma list for the API query", () => {
        const q = toApiQuery({ startUtc: "x", endUtc: "y", lines: [2, 7] });
        expect(q.lines).toBe("2,7");
    });

    it("omits lines from the API query when empty", () => {
        const q = toApiQuery({ startUtc: "x", endUtc: "y" });
        expect(q.lines).toBeUndefined();
    });
});
