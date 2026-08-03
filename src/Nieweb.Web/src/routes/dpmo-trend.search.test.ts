import { describe, it, expect } from "vitest";
import { validateDpmoTrendSearch, toApiQuery } from "./dpmo-trend.search";

/**
 * Regression coverage for the Line filter round-trip.
 *
 * The bug this pins: `formToSearch` produces `lines` as a NUMBER array
 * (e.g. `[2]`) during in-app navigation, but TanStack Router re-runs
 * `validateSearch` on the navigated search. A string-only `toNumberArray`
 * filters those numbers out, so `lines` silently vanishes from the URL AND
 * the API call, and no `Machine_Id IN (...)` ever reaches SQL. The tell is
 * that string arrays like `skipStatuses` survive while `lines` does not.
 */
describe("validateDpmoTrendSearch / lines", () => {
    it("keeps a numeric lines array (in-app navigation shape)", () => {
        const out = validateDpmoTrendSearch({ lines: [2, 7] });
        expect(out.lines).toEqual([2, 7]);
    });

    it("parses a comma-separated lines string (shared/bookmarked URL shape)", () => {
        const out = validateDpmoTrendSearch({ lines: "2,7" });
        expect(out.lines).toEqual([2, 7]);
    });

    it("keeps a single-element numeric array", () => {
        const out = validateDpmoTrendSearch({ lines: [2] });
        expect(out.lines).toEqual([2]);
    });

    it("parses a string array of numbers", () => {
        const out = validateDpmoTrendSearch({ lines: ["2", "7"] });
        expect(out.lines).toEqual([2, 7]);
    });

    it("drops non-finite / empty entries", () => {
        expect(validateDpmoTrendSearch({ lines: [] }).lines).toBeUndefined();
        expect(validateDpmoTrendSearch({ lines: "abc" }).lines).toBeUndefined();
        expect(validateDpmoTrendSearch({ lines: "" }).lines).toBeUndefined();
        expect(validateDpmoTrendSearch({}).lines).toBeUndefined();
    });

    it("serialises lines to a comma list for the API query", () => {
        const q = toApiQuery({ startUtc: "x", endUtc: "y", lines: [2, 7] });
        expect(q.lines).toBe("2,7");
    });

    it("omits lines from the API query when empty", () => {
        expect(toApiQuery({ startUtc: "x", endUtc: "y" }).lines).toBeUndefined();
        expect(toApiQuery({ startUtc: "x", endUtc: "y", lines: [] }).lines).toBeUndefined();
    });

    it("survives a full navigate round-trip", () => {
        // formToSearch -> validateSearch -> toApiQuery must preserve the filter.
        const fromForm = { startUtc: "x", endUtc: "y", lines: [2, 7] };
        const validated = validateDpmoTrendSearch(fromForm as Record<string, unknown>);
        expect(toApiQuery(validated).lines).toBe("2,7");
    });
});

describe("validateDpmoTrendSearch / defaults", () => {
    it("applies the report defaults", () => {
        const out = validateDpmoTrendSearch({});
        expect(out.bucket).toBe("Week");
        expect(out.opportunity).toBe("Components");
        expect(out.numerator).toBe("Real");
        expect(out.skipExclusion).toBe("Clean");
    });

    it("accepts canonical and lower-case enum spellings", () => {
        expect(validateDpmoTrendSearch({ bucket: "Day" }).bucket).toBe("Day");
        expect(validateDpmoTrendSearch({ bucket: "day" }).bucket).toBe("Day");
        expect(validateDpmoTrendSearch({ numerator: "dummy" }).numerator).toBe("Dummy");
    });

    it("falls back to the default on an unknown enum value", () => {
        expect(validateDpmoTrendSearch({ bucket: "fortnight" }).bucket).toBe("Week");
        expect(validateDpmoTrendSearch({ numerator: "nonsense" }).numerator).toBe("Real");
    });

    it("does not accept the Paste opportunity, which the UI does not offer", () => {
        // Paste opportunities only exist on PastePrintMetrics (pre-reflow)
        // sources, so a paste trend would render an empty post-reflow series
        // that reads as "no defects" rather than "not applicable".
        expect(validateDpmoTrendSearch({ opportunity: "Paste" }).opportunity).toBe("Components");
        expect(validateDpmoTrendSearch({ opportunity: "All" }).opportunity).toBe("All");
    });
});

describe("toApiQuery / numerator", () => {
    it("never sends the numerator: every cell carries all three", () => {
        const q = toApiQuery({ startUtc: "x", endUtc: "y", numerator: "Dummy" });
        expect(q.numerator).toBeUndefined();
    });

    it("sends the opportunity, which does change the server-side query", () => {
        const q = toApiQuery({ startUtc: "x", endUtc: "y", opportunity: "All" });
        expect(q.opportunity).toBe("All");
    });

    it("only carries skipExclusion when Clean (server default is Raw)", () => {
        expect(toApiQuery({ skipExclusion: "Clean" }).skipExclusion).toBe("Clean");
        expect(toApiQuery({ skipExclusion: "Raw" }).skipExclusion).toBeUndefined();
    });
});
