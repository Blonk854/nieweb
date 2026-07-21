import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { I18nextProvider } from "react-i18next";
import { FpyBarChart } from "./FpyBarChart";
import { FPY_BAND_COLORS } from "./fpyThresholds";
import type { PanelYieldByMachineRow } from "../api/reports";
import i18n from "../i18n";

// Mock echarts-for-react: jsdom has no real Canvas, and we don't need
// pixel-level assertions - we care that the component builds a valid
// option object and hands it to the chart. The mock captures the
// option so tests can inspect the coloured data + markLines.
type CapturedOption = { option: unknown };
const captured: CapturedOption = { option: null };
vi.mock("echarts-for-react", () => ({
    default: (props: { option: unknown }) => {
        captured.option = props.option;
        return <div data-testid="mock-echarts" />;
    },
}));

const wrapper = ({ children }: { children: React.ReactNode }) => (
    <I18nextProvider i18n={i18n}>
        <MantineProvider>{children}</MantineProvider>
    </I18nextProvider>
);

function mkRow(machineId: number, machineName: string | null, fpy: number): PanelYieldByMachineRow {
    return {
        machineId,
        machineName,
        kpi: {
            totalPanels: 100,
            inspectedPanels: 100,
            goodPanels: Math.round(fpy),
            faultyPanels: 100 - Math.round(fpy),
            notInspectedPanels: 0,
            fpyPercent: fpy,
        },
    };
}

describe("FpyBarChart", () => {
    it("renders an empty state placeholder when there are no rows", () => {
        render(<FpyBarChart rows={[]} overallFpyPercent={0} />, { wrapper });
        // Empty state has role img with aria-label, no chart is mounted.
        expect(screen.queryByTestId("mock-echarts")).toBeNull();
    });

    it("colours each bar by band (green/amber/red)", async () => {
        const rows = [
            mkRow(1, "L1-AOI", 99.9), // green
            mkRow(2, "L2-AOI", 98.5), // amber
            mkRow(3, "L3-AOI", 90.0), // red
        ];
        render(<FpyBarChart rows={rows} overallFpyPercent={96.13} />, { wrapper });

        expect(screen.getByTestId("mock-echarts")).toBeInTheDocument();
        const opt = captured.option as {
            series: [{ data: { itemStyle: { color: string } }[]; markLine: { data: unknown[] } }];
            yAxis: { min: number; max: number };
        };
        expect(opt.series[0].data[0].itemStyle.color).toBe(FPY_BAND_COLORS.green);
        expect(opt.series[0].data[1].itemStyle.color).toBe(FPY_BAND_COLORS.amber);
        expect(opt.series[0].data[2].itemStyle.color).toBe(FPY_BAND_COLORS.red);
        // Contains 3 markLines: overall, green threshold, amber threshold.
        expect(opt.series[0].markLine.data.length).toBe(3);
        // With a red bar in the mix, Y-axis stays at 0-100 (no zoom).
        expect(opt.yAxis.min).toBe(0);
        expect(opt.yAxis.max).toBe(100);
    });

    it("zooms the Y-axis when every value is in the amber band or better", () => {
        const rows = [
            mkRow(1, "L1", 99.9),
            mkRow(2, "L2", 99.7),
            mkRow(3, "L3", 99.4),
        ];
        render(<FpyBarChart rows={rows} overallFpyPercent={99.66} />, { wrapper });
        const opt = captured.option as { yAxis: { min: number; max: number } };
        // min value is 99.4 -> yMin = floor(99.4) - 1 = 98
        expect(opt.yAxis.min).toBe(98);
        expect(opt.yAxis.max).toBe(100);
    });

    it("falls back to '#<id>' when machineName is null", () => {
        const rows = [mkRow(42, null, 99.9)];
        render(<FpyBarChart rows={rows} overallFpyPercent={99.9} />, { wrapper });
        const opt = captured.option as { xAxis: { data: string[] } };
        expect(opt.xAxis.data[0]).toBe("#42");
    });
});
