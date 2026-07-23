import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, within } from "@testing-library/react";
import { userEvent } from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { I18nextProvider } from "react-i18next";
import i18n from "../i18n";
import { FilterBuilder } from "./FilterBuilder";
import type { FilterClause } from "./filterMetadata";

function Harness(props: { initial?: FilterClause[]; onEmit?: (c: FilterClause[]) => void }) {
    const [value, setValue] = useState<FilterClause[]>(props.initial ?? []);
    return (
        <FilterBuilder
            value={value}
            onChange={(next) => {
                setValue(next);
                props.onEmit?.(next);
            }}
        />
    );
}

function wrap(node: React.ReactNode) {
    return (
        <I18nextProvider i18n={i18n}>
            <MantineProvider>{node}</MantineProvider>
        </I18nextProvider>
    );
}

async function pickMantineSelect(user: ReturnType<typeof userEvent.setup>, input: HTMLElement, optionText: string) {
    await user.click(input);
    // Mantine 9 renders every dropdown into a shared portal; scope the
    // option lookup to the input's own combobox via aria-controls to
    // avoid matching options from a sibling Select in the same row.
    const listboxId = input.getAttribute("aria-controls");
    const listbox = listboxId ? document.getElementById(listboxId) : null;
    if (!listbox) {
        throw new Error(`Combobox listbox not found for input ${input.getAttribute("data-testid") ?? ""}`);
    }
    const option = await within(listbox as HTMLElement).findByText(optionText);
    await user.click(option);
}

// Mantine 9 Select renders its data-testid on the input element itself
// with role="combobox"; that is what tests click directly.

describe("FilterBuilder", () => {
    afterEach(() => {
        cleanup();
        vi.restoreAllMocks();
    });

    it("renders an empty-state message and an add button by default", () => {
        render(wrap(<Harness />));
        expect(screen.getByTestId("filter-builder-empty")).toBeInTheDocument();
        expect(screen.getByTestId("filter-builder-add")).toBeInTheDocument();
    });

    it("adds a default clause when Add is clicked", async () => {
        const user = userEvent.setup();
        const emitted: FilterClause[][] = [];
        render(wrap(<Harness onEmit={(c) => emitted.push(c)} />));
        await user.click(screen.getByTestId("filter-builder-add"));

        // Default clause = first field (BoardNumber, integer) with Equal + one blank value.
        expect(emitted.at(-1)).toEqual([
            { field: "BoardNumber", operator: "Equal", values: [""] },
        ]);
        expect(screen.getByTestId("filter-builder-row-0")).toBeInTheDocument();
    });

    it("restricts the operator dropdown to operators allowed on the field", async () => {
        const user = userEvent.setup();
        // PanelStatus only allows Equal.
        render(
            wrap(
                <Harness
                    initial={[
                        { field: "PanelStatus", operator: "Equal", values: ["1"] },
                    ]}
                />,
            ),
        );
        const opSelect = screen.getByTestId("filter-builder-operator-0");
        await user.click(opSelect);
        // The Combobox is a shared portal; scope the option lookup to
        // this input's own listbox (via aria-controls) so a sibling
        // Select's options can't leak into the assertion.
        const listboxId = opSelect.getAttribute("aria-controls");
        const listbox = listboxId
            ? document.getElementById(listboxId)
            : null;
        expect(listbox).not.toBeNull();
        // Mantine sometimes needs a beat before the options mount; poll.
        const options = await within(listbox as HTMLElement).findAllByText(
            /^(=|≠|in|not in|between|not between|like|not like|≤|≥)$/,
        );
        expect(options).toHaveLength(1);
        expect(options[0]).toHaveTextContent("=");
    });

    it("snaps operator to a legal default when the field changes to a stricter one", async () => {
        const user = userEvent.setup();
        const emitted: FilterClause[][] = [];
        render(
            wrap(
                <Harness
                    initial={[
                        {
                            field: "BoardNumber",
                            operator: "Between",
                            values: ["1", "10"],
                        },
                    ]}
                    onEmit={(c) => emitted.push(c)}
                />,
            ),
        );
        const fieldSelect = screen.getByTestId("filter-builder-field-0");
        await pickMantineSelect(user, fieldSelect, "Panel status");

        const last = emitted.at(-1)![0];
        expect(last.field).toBe("PanelStatus");
        expect(last.operator).toBe("Equal"); // snapped down
        expect(last.values).toHaveLength(1); // arity coerced to Single
    });

    it("renders two value inputs for a Range operator", () => {
        render(
            wrap(
                <Harness
                    initial={[
                        {
                            field: "BoardNumber",
                            operator: "Between",
                            values: ["1", "10"],
                        },
                    ]}
                />,
            ),
        );
        expect(screen.getByTestId("filter-builder-value-min-0")).toBeInTheDocument();
        expect(screen.getByTestId("filter-builder-value-max-0")).toBeInTheDocument();
    });

    it("renders a TagsInput for a List operator (In)", async () => {
        const user = userEvent.setup();
        const emitted: FilterClause[][] = [];
        render(
            wrap(
                <Harness
                    initial={[
                        { field: "Defect", operator: "In", values: [] },
                    ]}
                    onEmit={(c) => emitted.push(c)}
                />,
            ),
        );
        const input = screen.getByTestId("filter-builder-values-0");
        await user.type(input, "MISSING{enter}TOMBSTONE{enter}");

        const last = emitted.at(-1)![0];
        expect(last.values).toEqual(["MISSING", "TOMBSTONE"]);
    });

    it("removes a clause when the X button is clicked", async () => {
        const user = userEvent.setup();
        const emitted: FilterClause[][] = [];
        render(
            wrap(
                <Harness
                    initial={[
                        {
                            field: "PartNumber",
                            operator: "Equal",
                            values: ["R123"],
                        },
                    ]}
                    onEmit={(c) => emitted.push(c)}
                />,
            ),
        );
        await user.click(screen.getByTestId("filter-builder-remove-0"));
        expect(emitted.at(-1)).toEqual([]);
        expect(screen.getByTestId("filter-builder-empty")).toBeInTheDocument();
    });

    it("resizes values from Single to Range when operator changes", async () => {
        const user = userEvent.setup();
        const emitted: FilterClause[][] = [];
        render(
            wrap(
                <Harness
                    initial={[
                        {
                            field: "BoardNumber",
                            operator: "Equal",
                            values: ["5"],
                        },
                    ]}
                    onEmit={(c) => emitted.push(c)}
                />,
            ),
        );
        const opSelect = screen.getByTestId("filter-builder-operator-0");
        await pickMantineSelect(user, opSelect, "between");

        const last = emitted.at(-1)![0];
        expect(last.operator).toBe("Between");
        expect(last.values).toHaveLength(2);
        expect(last.values[0]).toBe("5"); // preserved
    });
});
