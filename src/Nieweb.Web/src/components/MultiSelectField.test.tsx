import { render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { describe, expect, it } from "vitest";

import { MultiSelectField, TagsInputField } from "./MultiSelectField";

function wrap(ui: React.ReactNode) {
    return <MantineProvider>{ui}</MantineProvider>;
}

/**
 * Regression: Mantine leaves the placeholder visible beside the selected
 * pills, so a Line filter read "Line 3 x  Line 5 x  All lines".
 */
describe("MultiSelectField", () => {
    const data = [
        { value: "2", label: "Line 2" },
        { value: "3", label: "Line 3" },
    ];

    it("shows the placeholder while nothing is selected", () => {
        render(
            wrap(
                <MultiSelectField
                    label="Line"
                    placeholder="All lines"
                    data={data}
                    value={[]}
                    onChange={() => undefined}
                />,
            ),
        );
        expect(screen.getByPlaceholderText("All lines")).toBeInTheDocument();
    });

    it("drops the placeholder once a value is selected", () => {
        render(
            wrap(
                <MultiSelectField
                    label="Line"
                    placeholder="All lines"
                    data={data}
                    value={["3"]}
                    onChange={() => undefined}
                />,
            ),
        );
        expect(screen.queryByPlaceholderText("All lines")).toBeNull();
        // Mantine renders the pill plus a hidden <option>, hence getAllByText.
        expect(screen.getAllByText("Line 3").length).toBeGreaterThan(0);
    });
});

describe("TagsInputField", () => {
    it("drops the placeholder once a tag is entered", () => {
        const { rerender } = render(
            wrap(<TagsInputField placeholder="Add values" value={[]} onChange={() => undefined} />),
        );
        expect(screen.getByPlaceholderText("Add values")).toBeInTheDocument();

        rerender(
            wrap(<TagsInputField placeholder="Add values" value={["R12"]} onChange={() => undefined} />),
        );
        expect(screen.queryByPlaceholderText("Add values")).toBeNull();
    });
});
