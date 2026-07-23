import { useSessionStore } from "../state/session";

/**
 * RC5 report-level export helper. Because the export endpoints are
 * gated by the JWT bearer token and the browser will not forward that
 * on a plain <c>&lt;a download&gt;</c> click, we do a manual
 * <c>fetch → blob → objectURL → a.click</c> dance.
 *
 * Filename is picked up from the server-set <c>Content-Disposition</c>
 * header when present; otherwise a sensible fallback is built from the
 * report id and format.
 */
export type ReportExportFormat = "xlsx" | "pdf";

export type ReportExportFilter = {
    sourceId: string;
    startUtc: string;
    endUtc: string;
    machineIds?: string;
    productIds?: string;
    onlyLastInspection?: boolean;
};

export function reportExportUrl(
    reportId: number,
    format: ReportExportFormat,
    filter: ReportExportFilter,
): string {
    const params = new URLSearchParams({
        sourceId: filter.sourceId,
        startUtc: filter.startUtc,
        endUtc: filter.endUtc,
    });
    if (filter.machineIds) params.set("machineIds", filter.machineIds);
    if (filter.productIds) params.set("productIds", filter.productIds);
    if (filter.onlyLastInspection !== undefined) {
        params.set("onlyLastInspection", String(filter.onlyLastInspection));
    }
    return `/api/reports/${reportId}/export.${format}?${params.toString()}`;
}

const DEFAULT_FILENAMES: Record<ReportExportFormat, (id: number) => string> = {
    xlsx: (id) => `report-${id}.xlsx`,
    pdf: (id) => `report-${id}.pdf`,
};

/**
 * Fetches the requested export, saves the returned blob under the
 * server-provided filename (falling back to <c>report-{id}.{ext}</c>),
 * and rejects with an <see cref="Error"/> containing the HTTP status
 * text on non-2xx so the caller can surface it in an Alert.
 */
export async function downloadReportExport(
    reportId: number,
    format: ReportExportFormat,
    filter: ReportExportFilter,
): Promise<void> {
    const token = useSessionStore.getState().token;
    const headers = new Headers();
    if (token) headers.set("Authorization", `Bearer ${token}`);
    const url = reportExportUrl(reportId, format, filter);
    const response = await fetch(url, { headers });
    if (!response.ok) {
        const body = await response.text().catch(() => "");
        throw new Error(
            `HTTP ${response.status} ${response.statusText}${body ? `: ${body}` : ""}`,
        );
    }
    const filename = extractFilename(response.headers.get("Content-Disposition"))
        ?? DEFAULT_FILENAMES[format](reportId);
    const blob = await response.blob();
    const objectUrl = URL.createObjectURL(blob);
    try {
        const anchor = document.createElement("a");
        anchor.href = objectUrl;
        anchor.download = filename;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
    }
    finally {
        URL.revokeObjectURL(objectUrl);
    }
}

/**
 * Extracts the filename from a <c>Content-Disposition</c> header of
 * the shape <c>attachment; filename="report-1-postreflow-....xlsx"</c>.
 * Also handles the RFC-5987 <c>filename*=UTF-8''...</c> form.
 */
function extractFilename(header: string | null): string | null {
    if (!header) return null;
    const utf8 = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(header);
    if (utf8) {
        try { return decodeURIComponent(utf8[1].trim()); }
        catch { /* fall through */ }
    }
    const quoted = /filename\s*=\s*"([^"]+)"/i.exec(header);
    if (quoted) return quoted[1];
    const bare = /filename\s*=\s*([^;]+)/i.exec(header);
    if (bare) return bare[1].trim();
    return null;
}
