import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import i18n from "../../i18n";
import { FailedObjectsTable } from "./FailedObjectsTable";
import type { TestedObjectRow } from "../../api/traceability";
import { usePreferencesStore, AUTO_TIME_ZONE } from "../../state/preferences";

/**
 * Unit tests for the TC5 Phase D `<FailedObjectsTable>` primitive.
 * Isolated from route + i18n glue; the caller supplies rows and a
 * primary highlight and receives row-click events.
 */

function baseRow(overrides: Partial<TestedObjectRow> = {}): TestedObjectRow {
    return {
        panelId: 1001,
        cardIdOnPanel: 0,
        objectId: 42,
        objectTypeId: 1,
        errorTable: 1,
        errorTableAr: 1, // Object missing
        status: 1,
        machineId: 1,
        productId: 5,
        panelNumericDate: 1_780_660_800,
        topology: "R100",
        partNumberName: "RES-10K",
        jedecName: "0402",
        deltaXUm: 10.5,
        deltaYUm: -7.2,
        deltaThetaDeg: 1.23,
        deltaThicknessUm: null,
        deltaSurface: 55.5,
        face: "Top",
        faceNumber: 1,
        feederName: "F12",
        repairState: 1, // Repaired
        repairUtc: 1_780_664_400,
        repairButtonComment: "Repaired",
        repairErrorComment: null,
        repairOperatorComment: "OK",
        repairOperatorId: 7,
        ...overrides,
    };
}

function renderTable(children: React.ReactNode) {
    return render(<MantineProvider>{children}</MantineProvider>);
}

describe("FailedObjectsTable", () => {
    beforeEach(() => {
        void i18n.changeLanguage("en");
        // Pin the timezone so review-date assertions are stable
        // regardless of the host machine's clock.
        usePreferencesStore.setState({ timeZone: "UTC" });
    });
    afterEach(() => {
        cleanup();
        usePreferencesStore.setState({ timeZone: AUTO_TIME_ZONE });
    });

    it("renders the empty-state hint when there are no rows", () => {
        renderTable(
            <FailedObjectsTable
                objects={[]}
                heading="Post-reflow AOI"
                testIdRoot="tbl"
            />,
        );
        expect(screen.getByTestId("tbl-empty")).toBeInTheDocument();
    });

    it("renders one row per tested object with the decoded error type", () => {
        renderTable(
            <FailedObjectsTable
                objects={[
                    baseRow({ cardIdOnPanel: 0, objectId: 1, errorTableAr: 1 }), // Object missing
                    baseRow({ cardIdOnPanel: 1, objectId: 2, errorTableAr: 3 }), // Object missing + Polarity
                ]}
                testIdRoot="tbl"
            />,
        );
        // Two data rows (both have topology so both are click-eligible).
        expect(screen.getByTestId("tbl-row-0-1")).toBeInTheDocument();
        expect(screen.getByTestId("tbl-row-1-2")).toBeInTheDocument();
        // Multi-bit decoding produces a joined label.
        expect(screen.getByText("Object missing + Polarity error")).toBeInTheDocument();
    });

    it("fires onRowClick with the row's highlight identifier", () => {
        const onRowClick = vi.fn();
        renderTable(
            <FailedObjectsTable
                objects={[baseRow({ topology: "U5", cardIdOnPanel: 2, objectId: 99 })]}
                onRowClick={onRowClick}
                testIdRoot="tbl"
            />,
        );
        fireEvent.click(screen.getByTestId("tbl-row-2-99"));
        expect(onRowClick).toHaveBeenCalledTimes(1);
        expect(onRowClick).toHaveBeenCalledWith({
            subpanelIndex: 2,
            reference: "U5",
        });
    });

    it("marks the row matching the primary highlight as selected", () => {
        renderTable(
            <FailedObjectsTable
                objects={[
                    baseRow({ cardIdOnPanel: 0, objectId: 1, topology: "R1" }),
                    baseRow({ cardIdOnPanel: 0, objectId: 2, topology: "R2" }),
                ]}
                primaryHighlight={{ subpanelIndex: 0, reference: "R2" }}
                onRowClick={() => {}}
                testIdRoot="tbl"
            />,
        );
        expect(screen.getByTestId("tbl-row-0-1")).not.toHaveAttribute("data-selected");
        expect(screen.getByTestId("tbl-row-0-2")).toHaveAttribute("data-selected", "true");
    });

    it("skips row-click on rows with no topology (nothing to bind to)", () => {
        const onRowClick = vi.fn();
        renderTable(
            <FailedObjectsTable
                objects={[baseRow({ topology: null, cardIdOnPanel: 3, objectId: 44 })]}
                onRowClick={onRowClick}
                testIdRoot="tbl"
            />,
        );
        fireEvent.click(screen.getByTestId("tbl-row-3-44"));
        expect(onRowClick).not.toHaveBeenCalled();
    });

    it("shows a loading indicator when isLoading is true", () => {
        renderTable(
            <FailedObjectsTable
                objects={[]}
                isLoading
                testIdRoot="tbl"
            />,
        );
        expect(screen.getByTestId("tbl-loading")).toBeInTheDocument();
    });

    it("shows an error alert when error is populated", () => {
        renderTable(
            <FailedObjectsTable
                objects={[]}
                error="Boom"
                testIdRoot="tbl"
            />,
        );
        expect(screen.getByTestId("tbl-error")).toBeInTheDocument();
        expect(screen.getByText("Boom")).toBeInTheDocument();
    });

    it("decodes the repair state via the i18n key", () => {
        renderTable(
            <FailedObjectsTable
                objects={[baseRow({ repairState: 2, objectId: 77 })]}
                testIdRoot="tbl"
            />,
        );
        // repairState=2 → "False call" in the EN catalogue.
        expect(screen.getByText(/false call/i)).toBeInTheDocument();
    });

    it("resolves the review operator id to a name via operatorLookup", () => {
        renderTable(
            <FailedObjectsTable
                objects={[baseRow({ repairOperatorId: 7 })]}
                operatorLookup={(id) => (id === 7 ? "Alice Anderson" : undefined)}
                testIdRoot="tbl"
            />,
        );
        expect(screen.getByText("Alice Anderson")).toBeInTheDocument();
    });

    it("falls back to the raw operator id when the lookup returns undefined", () => {
        renderTable(
            <FailedObjectsTable
                objects={[baseRow({ repairOperatorId: 999 })]}
                operatorLookup={() => undefined}
                testIdRoot="tbl"
            />,
        );
        expect(screen.getByText("999")).toBeInTheDocument();
    });

    it("uses the raw operator id when no operatorLookup is provided", () => {
        renderTable(
            <FailedObjectsTable
                objects={[baseRow({ repairOperatorId: 42 })]}
                testIdRoot="tbl"
            />,
        );
        expect(screen.getByText("42")).toBeInTheDocument();
    });

    it("formats the review date with the user's timezone preference", () => {
        // repairUtc = 1_780_664_400 → Fri Jun 05 2026 13:00:00 UTC.
        // With the UTC timezone pin from beforeEach + en-US locale +
        // dateStyle:short + timeStyle:medium + hour12:true we get
        // "6/5/26, 1:00:00 PM". A tolerant regex avoids coupling to
        // exact Intl formatting quirks between Node/browser versions.
        renderTable(
            <FailedObjectsTable
                objects={[baseRow({ repairUtc: 1_780_664_400 })]}
                testIdRoot="tbl"
            />,
        );
        expect(screen.getByText(/6\/5\/26.*1:00:00\s*PM/i)).toBeInTheDocument();
    });

    it("renders an em-dash for missing review dates", () => {
        renderTable(
            <FailedObjectsTable
                objects={[baseRow({ repairUtc: 0 })]}
                testIdRoot="tbl"
            />,
        );
        // The em-dash appears in several cells; presence is enough.
        expect(screen.getAllByText("—").length).toBeGreaterThan(0);
    });

    it("omits the trimmed columns (Panel ID, Package, Dev S, Dev Thickness)", () => {
        renderTable(
            <FailedObjectsTable
                objects={[baseRow()]}
                testIdRoot="tbl"
            />,
        );
        // The removed column headers must not appear anymore. Match
        // the exact EN labels so a regression that re-adds any of
        // them fails fast.
        expect(screen.queryByRole("columnheader", { name: /^Panel ID$/i }))
            .not.toBeInTheDocument();
        expect(screen.queryByRole("columnheader", { name: /^Package$/i }))
            .not.toBeInTheDocument();
        expect(screen.queryByRole("columnheader", { name: /^Dev S \(%\)$/i }))
            .not.toBeInTheDocument();
        expect(screen.queryByRole("columnheader", { name: /^Dev Thickness \(µm\)$/i }))
            .not.toBeInTheDocument();
    });
});
