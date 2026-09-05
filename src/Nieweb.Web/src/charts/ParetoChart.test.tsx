import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { I18nextProvider } from "react-i18next";
import { ParetoChart } from "./ParetoChart";
import { PARETO_COLORS } from "./paretoColors";
import type { ParetoRow } from "../api/pareto";
import i18n from "../i18n";

// Mock echarts-for-react: jsdom has no real Canvas and we don't need
// pixel-level assertions - we care that the component builds a valid
// option object and forwards clicks to onBarClick. The mock captures
// both the option and the onEvents.click handler.
type Captured = {
    option: unknown;
    onEvents: Record<string, (params: unknown) => void> | undefined;
};
const captured: Captured = { option: null, onEvents: undefined };

vi.mock("echarts-for-react", () => ({
    default: (props: {
        option: unknown;
        onEvents?: Record<string, (params: unknown) => void>;
    }) => {
        captured.option = props.option;
        captured.onEvents = props.onEvents;
        return <div data-testid="mock-echarts" />;
    },
}));

const wrapper = ({ children }: { children: React.ReactNode }) => (
    <I18nextProvider i18n={i18n}>
        <MantineProvider>{children}</MantineProvider>
    </I18nextProvider>
);

function mkRow(
    groupKey: string | null,
    groupName: string | null,
    defectCount: number,
    opportunityCount: number,
    defectShare: number,
    cumulative: number,
    isVitalFew: boolean,
): ParetoRow {
    return {
        groupKey,
        groupName,
        defectCount,
        weightedScore: defectCount,
        opportunityCount,
        opportunitySharePercent: 0,
        dpmoPpm: opportunityCount > 0 ? (defectCount * 1_000_000) / opportunityCount : 0,
        defectSharePercent: defectShare,
        cumulativePercent: cumulative,
        isVitalFew,
        opportunitiesApplicable: true,
    };
}

describe("ParetoChart", () => {
    it("renders an empty state placeholder when there are no rows", () => {
        render(<ParetoChart rows={[]} axis="Defect" weight="Count" />, { wrapper });
        expect(screen.queryByTestId("mock-echarts")).toBeNull();
    });

    it("colours bars by vital-few / trivial-many bands", () => {
        const rows = [
            mkRow("1", "bit 1", 50, 1000, 50, 50, true),
            mkRow("3", "bit 3", 30, 1000, 30, 80, true),
            mkRow("5", "bit 5", 20, 1000, 20, 100, false),
        ];
        render(<ParetoChart rows={rows} axis="Defect" weight="Count" />, { wrapper });

        expect(screen.getByTestId("mock-echarts")).toBeInTheDocument();
        const opt = captured.option as {
            series: [
                { data: { itemStyle: { color: string } }[] },
                { data: number[]; markLine: { data: unknown[] } },
            ];
        };
        expect(opt.series[0].data[0].itemStyle.color).toBe(PARETO_COLORS.vitalFew);
        expect(opt.series[0].data[1].itemStyle.color).toBe(PARETO_COLORS.vitalFew);
        expect(opt.series[0].data[2].itemStyle.color).toBe(PARETO_COLORS.trivialMany);
        // Cumulative line data mirrors the row.cumulativePercent values.
        expect(opt.series[1].data).toEqual([50, 80, 100]);
    });

    it("appends the Others bucket as a distinct grey bar", () => {
        const rows = [
            mkRow("A", "Product A", 60, 1000, 60, 60, true),
            mkRow("B", "Product B", 30, 1000, 30, 90, true),
        ];
        const others = mkRow(null, null, 10, 1000, 10, 100, false);
        render(
            <ParetoChart rows={rows} othersBucket={others} axis="Product" weight="Count" />,
            { wrapper },
        );
        const opt = captured.option as {
            series: [{ data: { itemStyle: { color: string } }[] }, { data: number[] }];
            xAxis: { data: string[] };
        };
        expect(opt.series[0].data.length).toBe(3);
        expect(opt.series[0].data[2].itemStyle.color).toBe(PARETO_COLORS.others);
        expect(opt.series[1].data.length).toBe(3);
        // Others row has no groupName -> the category placeholder is used.
        expect(opt.xAxis.data[2]).toBe("—");
    });

    it("labels defect-axis rows with 'bit N' when groupName is null", () => {
        const rows = [mkRow("7", null, 5, 100, 100, 100, false)];
        render(<ParetoChart rows={rows} axis="Defect" weight="Count" />, { wrapper });
        const opt = captured.option as { xAxis: { data: string[] } };
        expect(opt.xAxis.data[0]).toBe("bit 7");
    });

    it("invokes onBarClick with the row when a real bar is clicked", () => {
        const rows = [
            mkRow("1", "bit 1", 10, 100, 100, 100, true),
        ];
        const onBarClick = vi.fn();
        render(
            <ParetoChart rows={rows} axis="Defect" weight="Count" onBarClick={onBarClick} />,
            { wrapper },
        );
        // Simulate ECharts bar click.
        captured.onEvents!.click({
            componentType: "series",
            seriesType: "bar",
            dataIndex: 0,
            data: {
                value: 10,
                itemStyle: { color: PARETO_COLORS.vitalFew },
                _row: rows[0],
                _isOthers: false,
            },
        });
        expect(onBarClick).toHaveBeenCalledTimes(1);
        expect(onBarClick).toHaveBeenCalledWith(rows[0], 0);
    });

    it("ignores clicks on the Others bar", () => {
        const others = mkRow(null, null, 5, 100, 100, 100, false);
        const onBarClick = vi.fn();
        render(
            <ParetoChart
                rows={[mkRow("1", "bit 1", 10, 100, 100, 100, true)]}
                othersBucket={others}
                axis="Defect"
                weight="Count"
                onBarClick={onBarClick}
            />,
            { wrapper },
        );
        captured.onEvents!.click({
            componentType: "series",
            seriesType: "bar",
            dataIndex: 1,
            data: {
                value: 5,
                itemStyle: { color: PARETO_COLORS.others },
                _row: others,
                _isOthers: true,
            },
        });
        expect(onBarClick).not.toHaveBeenCalled();
    });

    it("ignores clicks on the cumulative line series", () => {
        const rows = [mkRow("1", "bit 1", 10, 100, 100, 100, true)];
        const onBarClick = vi.fn();
        render(
            <ParetoChart rows={rows} axis="Defect" weight="Count" onBarClick={onBarClick} />,
            { wrapper },
        );
        captured.onEvents!.click({
            componentType: "series",
            seriesType: "line",
            dataIndex: 0,
            data: 100,
        });
        expect(onBarClick).not.toHaveBeenCalled();
    });

    it("renders the vital-few threshold as a markLine on the right axis", () => {
        const rows = [mkRow("1", "bit 1", 10, 100, 100, 100, true)];
        render(
            <ParetoChart rows={rows} axis="Defect" weight="Count" vitalFewThresholdPercent={90} />,
            { wrapper },
        );
        const opt = captured.option as {
            series: [unknown, { markLine: { data: { yAxis: number }[] } }];
        };
        expect(opt.series[1].markLine.data[0].yAxis).toBe(90);
    });

    it("plots weightedScore and omits the cumulative series in DPMO mode", () => {
        const rows = [
            mkRow("low-vol", "High DPMO", 2, 10, 20, 20, true),
            mkRow("high-vol", "High count", 50, 1000, 80, 100, false),
        ];
        rows[0] = { ...rows[0], weightedScore: 200_000, dpmoPpm: 200_000 };
        rows[1] = { ...rows[1], weightedScore: 50_000, dpmoPpm: 50_000 };
        render(<ParetoChart rows={rows} axis="Product" weight="Dpmo" />, { wrapper });
        const opt = captured.option as {
            series: { type: string; data: { value: number }[] }[];
        };
        expect(opt.series).toHaveLength(1);
        expect(opt.series[0].type).toBe("bar");
        expect(opt.series[0].data[0].value).toBe(200_000);
        expect(opt.series[0].data[1].value).toBe(50_000);
    });
});
