import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { I18nextProvider } from "react-i18next";

import { TileConfigForm } from "./TileConfigForm";
import { parseParetoTileConfig } from "./tileConfig";
import i18n from "../../i18n";

const wrapper = ({ children }: { children: React.ReactNode }) => (
    <I18nextProvider i18n={i18n}>
        <MantineProvider>{children}</MantineProvider>
    </I18nextProvider>
);

describe("TileConfigForm", () => {
    it("renders nothing for a tile type without a schema", () => {
        render(
            <TileConfigForm tileType="comment" value="{}" onChange={() => {}} />,
            { wrapper },
        );
        expect(screen.queryByTestId("tile-config-form-comment")).toBeNull();
    });

    it("renders the pareto fields seeded from the current config", () => {
        render(
            <TileConfigForm
                tileType="pareto"
                value={JSON.stringify({ axis: "Product", numerator: "Aoi" })}
                onChange={() => {}}
            />,
            { wrapper },
        );
        // The axis select shows the plain-language label for "Product".
        expect(screen.getByTestId("tile-config-axis")).toHaveValue("Product");
        // Plain-language numerator label, not the raw enum name.
        expect(screen.getByTestId("tile-config-numerator")).toHaveValue(
            "AOI defects (as inspected)",
        );
    });

    it("emits normalised configJson when a select changes", async () => {
        const onChange = vi.fn();
        render(
            <TileConfigForm tileType="pareto" value="{}" onChange={onChange} />,
            { wrapper },
        );

        const user = userEvent.setup();
        // Change the "Bar height" (weight) select to the DPMO rate view.
        const weight = screen.getByTestId("tile-config-weight");
        await user.click(weight);
        await user.click(await screen.findByText("DPMO (rate)"));

        expect(onChange).toHaveBeenCalled();
        const emitted = onChange.mock.calls.at(-1)![0] as string;
        const cfg = parseParetoTileConfig(emitted);
        expect(cfg.weight).toBe("Dpmo");
        // Untouched fields keep their canonical defaults.
        expect(cfg.axis).toBe("Defect");
        expect(cfg.numerator).toBe("Real");
    });

    it("toggles the panelYield checkbox to emit onlyLastInspection=false", async () => {
        const onChange = vi.fn();
        render(
            <TileConfigForm
                tileType="panelYield"
                value={JSON.stringify({ onlyLastInspection: true })}
                onChange={onChange}
            />,
            { wrapper },
        );

        const user = userEvent.setup();
        await user.click(screen.getByTestId("tile-config-onlyLastInspection"));

        const emitted = onChange.mock.calls.at(-1)![0] as string;
        expect(JSON.parse(emitted)).toEqual({ onlyLastInspection: false });
    });
});
