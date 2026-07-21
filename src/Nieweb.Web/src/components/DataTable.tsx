import { useEffect, useMemo, useState } from "react";
import {
    ActionIcon,
    Group,
    Menu,
    Pagination,
    Select,
    Table,
    Text,
} from "@mantine/core";
import {
    IconArrowsSort,
    IconChevronDown,
    IconChevronUp,
    IconColumns,
    IconDownload,
} from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

/**
 * Sortable / paginated / column-toggleable data table.
 *
 * Design notes:
 *  - Purely presentational; the parent owns the source rows and the
 *    export button wiring (via `onExport`). Sort + page + column
 *    visibility are internal state - they never round-trip through
 *    the URL, so different reports can keep their own layouts.
 *  - `columns` is a typed `Column<T>[]` where `accessor(row)` yields
 *    the sortable value (number or string). `formatter(value, row)`
 *    controls the displayed string; if omitted, `String(value ?? "")`.
 *  - Sort compares numbers numerically and strings via
 *    `localeCompare` with the current language. Rows with `null`/
 *    `undefined` accessor results sort to the end regardless of
 *    direction (so "not measured" never disguises itself as "worst"
 *    or "best").
 *  - Column visibility toggles: at least one column must remain
 *    visible; the last visible column's checkbox is disabled.
 *  - Pagination: user picks 10/25/50/100. If total rows <= smallest
 *    page size, controls collapse to a plain footer count.
 */
export type Column<T> = {
    /** Stable key; also used as React key + CSV/XLSX header if `header` is omitted. */
    key: string;
    /** Localised column header. */
    header: string;
    /** Extract the sortable value. Return null/undefined to mark "no value". */
    accessor: (row: T) => string | number | null | undefined;
    /** Optional custom cell renderer / formatter. */
    formatter?: (value: string | number | null | undefined, row: T) => React.ReactNode;
    /** Optional CSV cell formatter (defaults to raw accessor value). */
    csvFormatter?: (value: string | number | null | undefined, row: T) => string;
    /** Text alignment; defaults to "left". */
    align?: "left" | "right" | "center";
    /** Whether the column can be sorted (default true). */
    sortable?: boolean;
    /** Whether the column can be hidden (default true). */
    hideable?: boolean;
};

export type SortState = {
    key: string | null;
    direction: "asc" | "desc";
};

export type DataTableProps<T> = {
    columns: Column<T>[];
    rows: T[];
    /** Stable id extractor for React keys. */
    rowKey: (row: T) => string | number;
    /** Initial sort. Defaults to no sort (source order). */
    initialSort?: SortState;
    /** Available page sizes; default [10, 25, 50, 100]. */
    pageSizes?: number[];
    /** Default page size (must be in pageSizes). Default 25. */
    initialPageSize?: number;
    /** Optional caption above the table. */
    caption?: string;
    /**
     * Called when the user clicks the "Download visible as CSV" button.
     * Receives the CURRENTLY-VISIBLE + SORTED rows (all pages) and the
     * visible columns in display order. The parent decides how to
     * deliver the file (usually via `downloadCsv` from ./csvExport).
     */
    onExportCsv?: (visibleRows: T[], visibleColumns: Column<T>[]) => void;
};

export function DataTable<T>(props: DataTableProps<T>) {
    const {
        columns,
        rows,
        rowKey,
        initialSort,
        pageSizes = [10, 25, 50, 100],
        initialPageSize = 25,
        caption,
        onExportCsv,
    } = props;
    const { t, i18n } = useTranslation();

    const [sort, setSort] = useState<SortState>(initialSort ?? { key: null, direction: "asc" });
    const [pageSize, setPageSize] = useState<number>(initialPageSize);
    const [page, setPage] = useState<number>(1);
    const [hiddenKeys, setHiddenKeys] = useState<Set<string>>(new Set());
    // When the browser is preparing a print, expand the tbody to every
    // sorted row so the paper output isn't truncated to the current
    // pagination window. Reverts on afterprint / print-dialog cancel.
    const [isPrinting, setIsPrinting] = useState<boolean>(false);
    useEffect(() => {
        if (typeof window === "undefined") return;
        const before = () => setIsPrinting(true);
        const after = () => setIsPrinting(false);
        window.addEventListener("beforeprint", before);
        window.addEventListener("afterprint", after);
        return () => {
            window.removeEventListener("beforeprint", before);
            window.removeEventListener("afterprint", after);
        };
    }, []);

    const visibleColumns = useMemo(
        () => columns.filter((c) => !hiddenKeys.has(c.key)),
        [columns, hiddenKeys],
    );

    const sortedRows = useMemo(() => {
        if (!sort.key) return rows;
        const col = columns.find((c) => c.key === sort.key);
        if (!col) return rows;
        const dir = sort.direction === "asc" ? 1 : -1;
        const collator = new Intl.Collator(i18n.language, { numeric: true, sensitivity: "base" });
        const copy = rows.slice();
        copy.sort((a, b) => {
            const av = col.accessor(a);
            const bv = col.accessor(b);
            // null/undefined always sort to the bottom regardless of dir.
            const aMissing = av === null || av === undefined || av === "";
            const bMissing = bv === null || bv === undefined || bv === "";
            if (aMissing && bMissing) return 0;
            if (aMissing) return 1;
            if (bMissing) return -1;
            if (typeof av === "number" && typeof bv === "number") {
                return (av - bv) * dir;
            }
            return collator.compare(String(av), String(bv)) * dir;
        });
        return copy;
    }, [rows, columns, sort, i18n.language]);

    const totalPages = Math.max(1, Math.ceil(sortedRows.length / pageSize));
    // Clamp page in case data shrunk.
    const currentPage = Math.min(page, totalPages);
    const pagedRows = useMemo(() => {
        if (isPrinting) return sortedRows;
        const start = (currentPage - 1) * pageSize;
        return sortedRows.slice(start, start + pageSize);
    }, [sortedRows, currentPage, pageSize, isPrinting]);

    function toggleSort(key: string) {
        setSort((prev) => {
            if (prev.key !== key) return { key, direction: "asc" };
            if (prev.direction === "asc") return { key, direction: "desc" };
            // Third click: clear sort back to source order.
            return { key: null, direction: "asc" };
        });
    }

    function toggleHidden(key: string) {
        setHiddenKeys((prev) => {
            const next = new Set(prev);
            if (next.has(key)) next.delete(key);
            else next.add(key);
            return next;
        });
    }

    const smallestPageSize = pageSizes[0] ?? 10;
    const showPagination = sortedRows.length > smallestPageSize;

    return (
        <div>
            <Group justify="space-between" mb="xs">
                {caption ? <Text fw={500}>{caption}</Text> : <span />}
                <Group gap="xs" className="no-print">
                    {onExportCsv ? (
                        <ActionIcon
                            variant="subtle"
                            aria-label={t("table.downloadCsv")}
                            title={t("table.downloadCsv")}
                            onClick={() => onExportCsv(sortedRows, visibleColumns)}
                        >
                            <IconDownload size={18} />
                        </ActionIcon>
                    ) : null}
                    <Menu shadow="md" closeOnItemClick={false} position="bottom-end">
                        <Menu.Target>
                            <ActionIcon
                                variant="subtle"
                                aria-label={t("table.columns")}
                                title={t("table.columns")}
                            >
                                <IconColumns size={18} />
                            </ActionIcon>
                        </Menu.Target>
                        <Menu.Dropdown>
                            <Menu.Label>{t("table.columns")}</Menu.Label>
                            {columns.map((c) => {
                                const hideable = c.hideable !== false;
                                const isVisible = !hiddenKeys.has(c.key);
                                const isLastVisible = isVisible && visibleColumns.length === 1;
                                return (
                                    <Menu.Item
                                        key={c.key}
                                        onClick={() => {
                                            if (!hideable) return;
                                            if (isLastVisible) return;
                                            toggleHidden(c.key);
                                        }}
                                        disabled={!hideable || isLastVisible}
                                    >
                                        <Text size="sm">
                                            {isVisible ? "✓ " : "  "}
                                            {c.header}
                                        </Text>
                                    </Menu.Item>
                                );
                            })}
                        </Menu.Dropdown>
                    </Menu>
                </Group>
            </Group>

            <Table striped withTableBorder highlightOnHover>
                <Table.Thead>
                    <Table.Tr>
                        {visibleColumns.map((c) => {
                            const sortable = c.sortable !== false;
                            const active = sort.key === c.key;
                            const Icon = !active
                                ? IconArrowsSort
                                : sort.direction === "asc"
                                    ? IconChevronUp
                                    : IconChevronDown;
                            return (
                                <Table.Th
                                    key={c.key}
                                    style={{
                                        textAlign: c.align ?? "left",
                                        cursor: sortable ? "pointer" : "default",
                                        userSelect: "none",
                                    }}
                                    onClick={sortable ? () => toggleSort(c.key) : undefined}
                                    aria-sort={
                                        active
                                            ? sort.direction === "asc"
                                                ? "ascending"
                                                : "descending"
                                            : "none"
                                    }
                                    scope="col"
                                >
                                    <Group gap={4} wrap="nowrap" justify={c.align === "right" ? "flex-end" : "flex-start"}>
                                        <span>{c.header}</span>
                                        {sortable ? <Icon size={14} opacity={active ? 1 : 0.4} /> : null}
                                    </Group>
                                </Table.Th>
                            );
                        })}
                    </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                    {pagedRows.length === 0 ? (
                        <Table.Tr>
                            <Table.Td colSpan={visibleColumns.length}>
                                <Text c="dimmed" ta="center">
                                    {t("table.noRows")}
                                </Text>
                            </Table.Td>
                        </Table.Tr>
                    ) : (
                        pagedRows.map((row) => (
                            <Table.Tr key={rowKey(row)}>
                                {visibleColumns.map((c) => {
                                    const raw = c.accessor(row);
                                    const cell = c.formatter ? c.formatter(raw, row) : formatDefault(raw);
                                    return (
                                        <Table.Td key={c.key} style={{ textAlign: c.align ?? "left" }}>
                                            {cell}
                                        </Table.Td>
                                    );
                                })}
                            </Table.Tr>
                        ))
                    )}
                </Table.Tbody>
            </Table>

            <Group justify="space-between" mt="xs">
                <Text size="sm" c="dimmed">
                    {t("table.rowCount", { count: sortedRows.length })}
                </Text>
                {showPagination ? (
                    <Group gap="sm" className="no-print">
                        <Select
                            aria-label={t("table.pageSize")}
                            size="xs"
                            w={90}
                            data={pageSizes.map((n) => String(n))}
                            value={String(pageSize)}
                            onChange={(v) => {
                                const n = v ? Number(v) : initialPageSize;
                                setPageSize(Number.isFinite(n) ? n : initialPageSize);
                                setPage(1);
                            }}
                            allowDeselect={false}
                        />
                        <Pagination
                            total={totalPages}
                            value={currentPage}
                            onChange={setPage}
                            size="sm"
                        />
                    </Group>
                ) : null}
            </Group>
        </div>
    );
}

function formatDefault(v: string | number | null | undefined): React.ReactNode {
    if (v === null || v === undefined) return "";
    return String(v);
}
