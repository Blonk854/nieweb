import type { TFunction } from "i18next";

import { ApiError } from "./client";

/**
 * Human-facing rendering of a failed request: an alert heading plus the
 * body text. Produced by {@link describeApiError} so every screen in the
 * app reports the *same* failure the same way.
 */
export type ApiErrorDisplay = {
    title: string;
    message: string;
};

/**
 * Server `code` extension -> localized message. Mirrors
 * `ReportEndpoints.ProblemCodes` in `src/Nieweb.Api`. Written as a switch
 * over literal keys so the typed `t` catches a mistyped key at build time.
 * Codes absent here fall through to the server-supplied `detail` / `title`,
 * so adding a code on the server never regresses the client to "HTTP 400".
 */
function messageForCode(code: string, t: TFunction): string | undefined {
    switch (code) {
        case "empty_window":
            return t("errors.emptyWindow");
        case "invalid_window":
            return t("errors.invalidWindow");
        case "invalid_start":
            return t("errors.invalidStart");
        case "invalid_end":
            return t("errors.invalidEnd");
        case "missing_source":
            return t("errors.missingSource");
        case "unknown_source":
            return t("errors.unknownSource");
        default:
            return undefined;
    }
}

/**
 * Turns any thrown value from the API layer into an actionable, localized
 * alert. Resolution order for the message:
 *
 * 1. a known `code` from the problem body (the only branch that can be
 *    translated, and the only one that can be phrased in the user's terms
 *    rather than the query-parameter's);
 * 2. the server's `detail`, then `title` (English, but specific);
 * 3. flattened `ValidationProblem` field errors;
 * 4. `HTTP <status> <statusText>` as the last resort — deliberately kept
 *    so a bodyless 500 is still identifiable in a bug report.
 *
 * A rejected `fetch` (no response at all) is reported as a connectivity
 * problem, which is the *only* case that should read "could not reach
 * the API".
 */
export function describeApiError(error: unknown, t: TFunction): ApiErrorDisplay {
    if (error instanceof ApiError) {
        return { title: titleForStatus(error.status, t), message: messageForApiError(error, t) };
    }
    // `fetch` rejects with a TypeError when the request never reached a
    // server (DNS, offline, CORS, connection refused).
    if (error instanceof TypeError) {
        return { title: t("errors.networkTitle"), message: t("errors.network") };
    }
    return {
        title: t("errors.genericTitle"),
        message: error instanceof Error ? error.message : String(error),
    };
}

function titleForStatus(status: number, t: TFunction): string {
    if (status === 400) return t("errors.badRequestTitle");
    if (status === 401) return t("errors.unauthorizedTitle");
    if (status === 403) return t("errors.forbiddenTitle");
    if (status === 404) return t("errors.notFoundTitle");
    if (status >= 500) return t("errors.serverTitle");
    return t("errors.genericTitle");
}

function messageForApiError(error: ApiError, t: TFunction): string {
    const coded = error.code === undefined ? undefined : messageForCode(error.code, t);
    if (coded !== undefined) {
        return coded;
    }
    if (error.status === 401) return t("errors.unauthorized");
    if (error.status === 403) return t("errors.forbidden");

    const problem = error.problem;
    if (problem) {
        const detail = firstNonEmpty(problem.detail, problem.title);
        const fields = flattenValidationErrors(problem.errors);
        if (detail && fields) return `${detail} ${fields}`;
        if (detail) return detail;
        if (fields) return fields;
    }
    return error.message;
}

function firstNonEmpty(...values: (string | undefined)[]): string | undefined {
    for (const value of values) {
        if (value !== undefined && value.trim() !== "") return value.trim();
    }
    return undefined;
}

function flattenValidationErrors(errors?: Record<string, string[]>): string | undefined {
    if (!errors) return undefined;
    const lines = Object.values(errors).flat().filter((m) => m.trim() !== "");
    return lines.length > 0 ? lines.join(" ") : undefined;
}
