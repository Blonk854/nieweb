import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { I18nextProvider } from "react-i18next";
import { KpiCards } from "./KpiCards";
import { FPY_BAND_COLORS } from "../charts/fpyThresholds";
import i18n from "../i18n";

const NOW = new Date("2026-07-21T12:00:00Z");

const wrapper = ({ children }: { children: React.ReactNode }) => (
    <I18nextProvider i18n={i18n}>
        <MantineProvider>{children}</MantineProvider>
    </I18nextProvider>
);

describe("KpiCards", () => {
    it("renders total panels with locale grouping and both other KPIs", () => {
        render(
            <KpiCards
                totalPanels={12345}
                overallFpyPercent={99.87}
                latestPanelUtc="2026-07-21T11:45:00Z"
                sourceDisplayName="HLYAOI2024"
                now={NOW}
            />,
            { wrapper },
        );
        // en-US grouping.
        expect(screen.getByText("12,345")).toBeInTheDocument();
        // FPY value rendered to 2 decimals with the % sign.
        expect(screen.getByText("99.87%")).toBeInTheDocument();
        // "15 minutes ago" (relative).
        expect(screen.getByText(/15 minutes ago/i)).toBeInTheDocument();
    });

    it("colours the overall-FPY value by band (green >= 99.5, amber 98-99.5, red < 98)", () => {
        // Hex -> rgb because jsdom serialises inline colour styles as rgb(...).
        const hexToRgb = (hex: string) => {
            const n = parseInt(hex.slice(1), 16);
            return `rgb(${(n >> 16) & 0xff}, ${(n >> 8) & 0xff}, ${n & 0xff})`;
        };
        const cases: [number, string][] = [
            [99.9, hexToRgb(FPY_BAND_COLORS.green)],
            [98.5, hexToRgb(FPY_BAND_COLORS.amber)],
            [90.0, hexToRgb(FPY_BAND_COLORS.red)],
        ];
        for (const [fpy, expectedRgb] of cases) {
            const { unmount } = render(
                <KpiCards
                    totalPanels={1}
                    overallFpyPercent={fpy}
                    latestPanelUtc="2026-07-21T11:45:00Z"
                    sourceDisplayName="src"
                    now={NOW}
                />,
                { wrapper },
            );
            const el = screen.getByText(`${fpy.toFixed(2)}%`);
            expect(el.style.color).toBe(expectedRgb);
            unmount();
        }
    });

    it("shows 'unknown' when the source has no PANELS rows", () => {
        render(
            <KpiCards
                totalPanels={0}
                overallFpyPercent={0}
                latestPanelUtc={null}
                sourceDisplayName="Empty source"
                now={NOW}
            />,
            { wrapper },
        );
        expect(screen.getByText(/unknown/i)).toBeInTheDocument();
    });
});
