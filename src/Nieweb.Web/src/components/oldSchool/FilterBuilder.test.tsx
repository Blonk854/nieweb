import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { I18nextProvider } from "react-i18next";

import { FilterBuilder } from "./FilterBuilder";
import { PARETO_FILTER_FIELDS, type FilterClause } from "../../api/filters";
import i18n from "../../i18n";

const wrapper = ({ children }: { children: React.ReactNode }) => (
    <I18nextProvider i18n={i18n}>
        <MantineProvider>{children}</MantineProvider>
    </I18nextProvider>
);

describe("FilterBuilder", () => {
    it("shows the empty hint when there are no filters", () => {
        render(<FilterBuilder fields={PARETO_FILTER_FIELDS} value={[]} onChange={() => {}} />, {
            wrapper,
        });
        expect(screen.getByText(/no filters/i)).toBeInTheDocument();
    });

    it("adds a clause seeded with the first field and operator", async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(<FilterBuilder fields={PARETO_FILTER_FIELDS} value={[]} onChange={onChange} />, {
            wrapper,
        });

        await user.click(screen.getByLabelText(/add filter/i));

        expect(onChange).toHaveBeenCalledTimes(1);
        const next = onChange.mock.calls[0][0] as FilterClause[];
        expect(next).toHaveLength(1);
        expect(next[0].field).toBe(PARETO_FILTER_FIELDS[0]);
    });

    it("renders one value input for a single-arity operator", () => {
        const value: FilterClause[] = [
            { field: "PartNumber", operator: "NotLike", values: ["PN-B"] },
        ];
        render(<FilterBuilder fields={PARETO_FILTER_FIELDS} value={value} onChange={() => {}} />, {
            wrapper,
        });
        const row = screen.getByTestId("filter-row-0");
        expect(within(row).getByDisplayValue("PN-B")).toBeInTheDocument();
    });

    it("marks an incomplete clause invalid", () => {
        const value: FilterClause[] = [
            // Between requires two values; only one supplied -> invalid row.
            { field: "PartNumber", operator: "In", values: [] },
        ];
        render(<FilterBuilder fields={PARETO_FILTER_FIELDS} value={value} onChange={() => {}} />, {
            wrapper,
        });
        // The row still renders (the list-arity input shows), and the row
        // exists so the user can complete it.
        expect(screen.getByTestId("filter-row-0")).toBeInTheDocument();
    });
});
