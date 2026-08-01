import { beforeAll, describe, expect, it } from "vitest";
import type { TFunction } from "i18next";

import { initI18n } from "../i18n";
import { ApiError } from "./client";
import { describeApiError } from "./problem";

/**
 * The regression these cover: an empty/inverted date range used to render
 * as the blanket "Could not reach the API — HTTP 400 Bad Request", which
 * told the user nothing about what to fix.
 */
describe("describeApiError", () => {
    let t: TFunction;

    beforeAll(async () => {
        const i18n = initI18n();
        await i18n.changeLanguage("en");
        t = i18n.t.bind(i18n) as TFunction;
    });

    function problem(code: string, title: string, status = 400): ApiError {
        return new ApiError(
            status,
            "Bad Request",
            JSON.stringify({ title, status, code }),
        );
    }

    it("maps the empty_window code onto an actionable date-range message", () => {
        const { title, message } = describeApiError(
            problem("empty_window", "'endUtc' must be strictly after 'startUtc'."),
            t,
        );
        expect(title).toBe("Check your filters");
        expect(message).toMatch(/date range is empty/i);
        expect(message).not.toMatch(/HTTP 400/);
        expect(message).not.toMatch(/endUtc/);
    });

    it("localizes the empty_window message", async () => {
        const i18n = initI18n();
        await i18n.changeLanguage("fr");
        const frT = i18n.t.bind(i18n) as TFunction;
        const { message } = describeApiError(
            problem("empty_window", "'endUtc' must be strictly after 'startUtc'."),
            frT,
        );
        expect(message).toMatch(/plage de dates est vide/i);
        await i18n.changeLanguage("en");
    });

    it("falls back to the server title for an unmapped code", () => {
        const { message } = describeApiError(
            problem("invalid_parameter", "Query parameter 'bucket' is invalid: nope."),
            t,
        );
        expect(message).toBe("Query parameter 'bucket' is invalid: nope.");
    });

    it("keeps HTTP <status> when the body is not a problem document", () => {
        const { title, message } = describeApiError(
            new ApiError(500, "Server Error", "boom"),
            t,
        );
        expect(title).toBe("The report could not be run");
        expect(message).toBe("HTTP 500 Server Error");
    });

    it("reports a rejected fetch as a connectivity problem", () => {
        const { title, message } = describeApiError(new TypeError("Failed to fetch"), t);
        expect(title).toBe("Could not reach the API");
        expect(message).toMatch(/did not respond/i);
    });

    it("flattens ValidationProblem field errors", () => {
        const error = new ApiError(
            400,
            "Bad Request",
            JSON.stringify({ title: "One or more validation errors occurred.", errors: { Name: ["Name is required."] } }),
        );
        expect(describeApiError(error, t).message).toContain("Name is required.");
    });
});
