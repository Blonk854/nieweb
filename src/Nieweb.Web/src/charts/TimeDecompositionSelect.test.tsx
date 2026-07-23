import { useState } from "react";
import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { userEvent } from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { I18nextProvider } from "react-i18next";
import i18n from "../i18n";
import { TimeDecompositionSelect } from "./TimeDecompositionSelect";
import type { TimeBucket } from "./timeDecomposition";

function Harness(props: {
    initial?: TimeBucket;
    exclude?: TimeBucket[];
    disable?: TimeBucket[];
    onEmit?: (v: TimeBucket) => void;
}) {
    const [value, setValue] = useState<TimeBucket>(props.initial ?? "Hour1");
    return (
        <TimeDecompositionSelect
            value={value}
            onChange={(v) => {
                setValue(v);
                props.onEmit?.(v);
            }}
            exclude={props.exclude}
            disable={props.disable}
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

async function openAndListOptions(user: ReturnType<typeof userEvent.setup>) {
    // Mantine 9 searchable Select puts our data-testid on the input
    // element itself and opens the popover on click. The options
    // don't carry `role="option"` — they're `[data-combobox-option]`
    // divs — so we hand back the collection queried that way.
    const input = screen.getByTestId("time-decomposition-select");
    await user.click(input);
    // Wait for the popover to render at least one option, then
    // grab all of them at once.
    await screen.findByText(/^By hour$/);
    const options = Array.from(
        document.querySelectorAll<HTMLElement>("[data-combobox-option]"),
    );
    return { input, options };
}

describe("TimeDecompositionSelect", () => {
    afterEach(() => cleanup());

    it("renders every bucket by default", async () => {
        const user = userEvent.setup();
        render(wrap(<Harness />));
        const { options } = await openAndListOptions(user);
        // 8 buckets in the enum.
        expect(options).toHaveLength(8);
    });

    it("emits the picked bucket when the user selects one", async () => {
        const user = userEvent.setup();
        const emitted: TimeBucket[] = [];
        render(wrap(<Harness onEmit={(v) => emitted.push(v)} />));
        await openAndListOptions(user);
        const dayOption = await screen.findByText(/^By day$/);
        await user.click(dayOption);
        expect(emitted).toEqual(["Day"]);
    });

    it("hides excluded buckets from the dropdown", async () => {
        const user = userEvent.setup();
        render(wrap(<Harness exclude={["Shift"]} />));
        const { options } = await openAndListOptions(user);
        // 8 - 1 excluded = 7 visible.
        expect(options).toHaveLength(7);
        // The Shift label should not appear.
        expect(screen.queryByText(/^By shift$/)).toBeNull();
    });

    it("surfaces a disabled current selection when it has been excluded", async () => {
        const user = userEvent.setup();
        render(wrap(<Harness initial="Shift" exclude={["Shift"]} />));
        const { options } = await openAndListOptions(user);
        // The current (excluded) value is re-rendered as a disabled
        // option with the "(unavailable)" suffix, so the total goes
        // back up to 8.
        expect(options).toHaveLength(8);
        expect(screen.getByText(/By shift.*unavailable/)).toBeInTheDocument();
    });

    it("keeps disabled buckets visible but non-selectable", async () => {
        const user = userEvent.setup();
        const emitted: TimeBucket[] = [];
        render(
            wrap(
                <Harness disable={["Week"]} onEmit={(v) => emitted.push(v)} />,
            ),
        );
        const { options } = await openAndListOptions(user);
        expect(options).toHaveLength(8);
        const weekOption = screen.getByText(/^By week$/);
        await user.click(weekOption);
        expect(emitted).toEqual([]);
    });
});
