import { describe, expect, it } from "vitest";
import { rowsToCsv } from "./csvExport";
import type { Column } from "./DataTable";

type Row = { id: number; name: string | null; qty: number | null; note?: string };

const cols: Column<Row>[] = [
    { key: "name", header: "Name", accessor: (r) => r.name },
    { key: "qty", header: "Qty", accessor: (r) => r.qty },
    { key: "note", header: "Note", accessor: (r) => r.note ?? null },
];

describe("rowsToCsv", () => {
    it("prepends a UTF-8 BOM so Excel opens the file correctly", () => {
        const out = rowsToCsv([], cols);
        expect(out.charCodeAt(0)).toBe(0xfeff);
    });

    it("emits a header row from the column headers", () => {
        const out = rowsToCsv([], cols);
        expect(out.slice(1)).toBe("Name,Qty,Note");
    });

    it("uses CRLF between rows (RFC 4180)", () => {
        const out = rowsToCsv([{ id: 1, name: "a", qty: 1 }], cols);
        expect(out).toContain("\r\n");
    });

    it("quotes cells containing commas, quotes, or newlines and doubles embedded quotes", () => {
        const rows: Row[] = [
            { id: 1, name: "Line 1, Line 2", qty: 3 },
            { id: 2, name: 'She said "hi"', qty: 4 },
            { id: 3, name: "multi\nline", qty: 5 },
            { id: 4, name: "plain", qty: 6 },
        ];
        const out = rowsToCsv(rows, cols).slice(1); // drop BOM
        const lines = out.split("\r\n");
        expect(lines[1]).toBe('"Line 1, Line 2",3,');
        expect(lines[2]).toBe('"She said ""hi""",4,');
        expect(lines[3]).toBe('"multi\nline",5,');
        expect(lines[4]).toBe("plain,6,");
    });

    it("serialises null/undefined as an empty cell", () => {
        const rows: Row[] = [{ id: 1, name: null, qty: null }];
        const out = rowsToCsv(rows, cols).slice(1);
        const lines = out.split("\r\n");
        expect(lines[1]).toBe(",,");
    });

    it("uses csvFormatter when provided", () => {
        const withFmt: Column<Row>[] = [
            {
                key: "qty",
                header: "Qty",
                accessor: (r) => r.qty,
                csvFormatter: (v) => (typeof v === "number" ? v.toFixed(2) : "N/A"),
            },
        ];
        const rows: Row[] = [
            { id: 1, name: "a", qty: 3 },
            { id: 2, name: "b", qty: null },
        ];
        const out = rowsToCsv(rows, withFmt).slice(1);
        const lines = out.split("\r\n");
        expect(lines[1]).toBe("3.00");
        expect(lines[2]).toBe("N/A");
    });

    it("emits header row only when rows is empty", () => {
        const out = rowsToCsv([], cols).slice(1);
        expect(out).toBe("Name,Qty,Note");
    });
});
