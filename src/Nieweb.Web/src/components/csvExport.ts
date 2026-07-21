import type { Column } from "./DataTable";

/**
 * Serialise a set of rows + columns to RFC 4180 CSV.
 *
 * - Cell values come from `col.csvFormatter` if provided, else
 *   `String(col.accessor(row) ?? "")`.
 * - Values are quoted only when they contain `,`, `"`, `\r`, or `\n`.
 * - Embedded `"` is doubled per RFC 4180.
 * - Rows are joined with CRLF (also RFC 4180).
 *
 * The BOM (`\uFEFF`) is prepended so Excel opens the file in UTF-8
 * without the user having to pick an encoding.
 */
export function rowsToCsv<T>(rows: T[], columns: Column<T>[]): string {
    const lines: string[] = [];
    lines.push(columns.map((c) => escape(c.header)).join(","));
    for (const row of rows) {
        const cells = columns.map((c) => {
            const raw = c.accessor(row);
            const cell = c.csvFormatter ? c.csvFormatter(raw, row) : raw === null || raw === undefined ? "" : String(raw);
            return escape(cell);
        });
        lines.push(cells.join(","));
    }
    return "\uFEFF" + lines.join("\r\n");
}

function escape(v: string): string {
    if (v === "") return "";
    if (/[",\r\n]/.test(v)) {
        return `"${v.replace(/"/g, '""')}"`;
    }
    return v;
}

/**
 * Trigger a browser download of a CSV string. Wraps the standard
 * Blob-to-object-URL dance so callers can just call
 * `downloadCsv("panel-yield.csv", rowsToCsv(rows, cols))`.
 *
 * Guarded by a `document`/`URL` check so tests running in
 * non-browser envs don't blow up.
 */
export function downloadCsv(filename: string, csv: string): void {
    if (typeof document === "undefined" || typeof URL === "undefined") return;
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    // Revoke on the next tick so Safari has a chance to start the download.
    setTimeout(() => URL.revokeObjectURL(url), 0);
}
