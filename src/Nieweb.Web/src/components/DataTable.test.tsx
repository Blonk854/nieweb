import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { I18nextProvider } from "react-i18next";
import { DataTable, type Column } from "./DataTable";
import i18n from "../i18n";

type Row = { id: number; name: string; fpy: number | null };

const rows: Row[] = [
    { id: 1, name: "L1-AOI", fpy: 99.9 },
    { id: 2, name: "L2-AOI", fpy: 98.0 },
    { id: 3, name: "L3-AOI", fpy: 99.5 },
    { id: 4, name: "L4-AOI", fpy: null },
];

const cols: Column<Row>[] = [
    { key: "name", header: "Machine", accessor: (r) => r.name, hideable: false },
    { key: "fpy", header: "FPY", accessor: (r) => r.fpy, align: "right" },
];

const wrapper = ({ children }: { children: React.ReactNode }) => (
    <I18nextProvider i18n={i18n}>
        <MantineProvider>{children}</MantineProvider>
    </I18nextProvider>
);

function machineCells(): string[] {
    return Array.from(document.querySelectorAll("tbody tr td:first-child")).map(
        (td) => td.textContent ?? "",
    );
}

describe("DataTable", () => {
    it("renders all rows in source order when no sort is set", () => {
        render(
            <DataTable columns={cols} rows={rows} rowKey={(r) => r.id} />,
            { wrapper },
        );
        expect(machineCells()).toEqual(["L1-AOI", "L2-AOI", "L3-AOI", "L4-AOI"]);
    });

    it("sorts ascending on first header click, descending on second, and clears on third", async () => {
        const user = userEvent.setup();
        render(
            <DataTable columns={cols} rows={rows} rowKey={(r) => r.id} />,
            { wrapper },
        );
        const fpyHeader = screen.getByRole("columnheader", { name: /FPY/i });
        // 1st click - ascending. Null sorts last.
        await user.click(fpyHeader);
        expect(machineCells()).toEqual(["L2-AOI", "L3-AOI", "L1-AOI", "L4-AOI"]);
        // 2nd click - descending. Null still sorts last.
        await user.click(fpyHeader);
        expect(machineCells()).toEqual(["L1-AOI", "L3-AOI", "L2-AOI", "L4-AOI"]);
        // 3rd click - clear sort back to source order.
        await user.click(fpyHeader);
        expect(machineCells()).toEqual(["L1-AOI", "L2-AOI", "L3-AOI", "L4-AOI"]);
    });

    it("does not offer sort on non-sortable columns", async () => {
        const nonSortable: Column<Row>[] = [
            { key: "name", header: "Machine", accessor: (r) => r.name, sortable: false, hideable: false },
        ];
        render(
            <DataTable columns={nonSortable} rows={rows} rowKey={(r) => r.id} />,
            { wrapper },
        );
        const header = screen.getByRole("columnheader", { name: /Machine/i });
        expect(header).toHaveAttribute("aria-sort", "none");
    });

    it("paginates when row count exceeds the smallest page size and clamps on shrink", async () => {
        const many: Row[] = Array.from({ length: 30 }, (_, i) => ({
            id: i + 1,
            name: `M${(i + 1).toString().padStart(2, "0")}`,
            fpy: 99,
        }));
        render(
            <DataTable
                columns={cols}
                rows={many}
                rowKey={(r) => r.id}
                initialPageSize={10}
                pageSizes={[10, 25]}
            />,
            { wrapper },
        );
        // First page has 10 rows.
        expect(document.querySelectorAll("tbody tr").length).toBe(10);
        expect(machineCells()[0]).toBe("M01");
        // Pagination control shows total pages = 3.
        expect(screen.getByText("30 rows")).toBeInTheDocument();
    });

    it("collapses pagination when rows <= smallest page size", () => {
        render(
            <DataTable
                columns={cols}
                rows={rows}
                rowKey={(r) => r.id}
                pageSizes={[10, 25]}
            />,
            { wrapper },
        );
        // Only 4 rows so no page-size selector rendered.
        expect(screen.queryByRole("combobox")).toBeNull();
    });

    it("calls onExportCsv with sorted+visible rows and visible columns only", async () => {
        const user = userEvent.setup();
        const onExport = vi.fn();
        render(
            <DataTable
                columns={cols}
                rows={rows}
                rowKey={(r) => r.id}
                onExportCsv={onExport}
            />,
            { wrapper },
        );
        // Sort by FPY asc first.
        await user.click(screen.getByRole("columnheader", { name: /FPY/i }));
        // Click download.
        const dlBtn = screen.getByRole("button", { name: /Download visible as CSV/i });
        await user.click(dlBtn);
        expect(onExport).toHaveBeenCalledTimes(1);
        const [passedRows, passedCols] = onExport.mock.calls[0];
        expect(passedRows.map((r: Row) => r.name)).toEqual([
            "L2-AOI",
            "L3-AOI",
            "L1-AOI",
            "L4-AOI",
        ]);
        expect(passedCols.map((c: Column<Row>) => c.key)).toEqual(["name", "fpy"]);
    });

    it("hides a column when its Columns menu entry is clicked", async () => {
        const user = userEvent.setup();
        render(
            <DataTable columns={cols} rows={rows} rowKey={(r) => r.id} />,
            { wrapper },
        );
        // Open the columns menu.
        await user.click(screen.getByRole("button", { name: /Columns/i }));
        // Only the FPY column is hideable ("name" has hideable=false).
        const fpyItem = await screen.findByRole("menuitem", { name: /FPY/i });
        await user.click(fpyItem);
        // FPY header should be gone.
        expect(screen.queryByRole("columnheader", { name: /FPY/i })).toBeNull();
        // Machine cells still visible.
        expect(machineCells()).toEqual(["L1-AOI", "L2-AOI", "L3-AOI", "L4-AOI"]);
    });

    it("disables the toggle for non-hideable columns", async () => {
        const user = userEvent.setup();
        render(
            <DataTable columns={cols} rows={rows} rowKey={(r) => r.id} />,
            { wrapper },
        );
        await user.click(screen.getByRole("button", { name: /Columns/i }));
        const nameItem = await screen.findByRole("menuitem", { name: /Machine/i });
        expect(nameItem).toHaveAttribute("data-disabled");
    });

    it("shows an empty-state row inside the table body when there are no rows", () => {
        render(
            <DataTable columns={cols} rows={[]} rowKey={(r) => r.id} />,
            { wrapper },
        );
        const body = document.querySelector("tbody") as HTMLElement;
        expect(within(body).getByText(/No rows to display/i)).toBeInTheDocument();
    });
});
