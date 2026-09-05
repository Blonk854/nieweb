import { describe, expect, it } from "vitest";
import {
    augustWindowUtc,
    DPMO_L6_AUG_PRESETS,
    L6_PART_ANALYSIS_MACHINE_NAME,
    PARETO_L6_AUG_PRESETS,
} from "./l6PartAnalysis";
import { resolveMachineId } from "./resolveMachineId";

describe("resolveMachineId", () => {
    it("matches machine name case-insensitively", () => {
        const machines = [
            { id: 42, name: "L6PSTAOI", typeName: "AOI" },
            { id: 7, name: "L2PSTAOI", typeName: "AOI" },
        ];
        expect(resolveMachineId(machines, "l6pstaoi")).toBe(42);
        expect(resolveMachineId(machines, "L6PSTAOI ")).toBe(42);
        expect(resolveMachineId(machines, "missing")).toBeNull();
    });
});

describe("augustWindowUtc", () => {
    it("returns August bounds in UTC for America/New_York", () => {
        const w = augustWindowUtc("America/New_York", 2026);
        expect(w).not.toBeNull();
        // 2026-08-01 00:00 EDT = 04:00 UTC
        expect(w!.startUtc).toBe("2026-08-01T04:00:00.000Z");
        expect(w!.endUtc).toBe("2026-09-01T04:00:00.000Z");
    });
});

describe("L6 part-analysis presets", () => {
    const ctx = {
        machines: [{ id: 99, name: L6_PART_ANALYSIS_MACHINE_NAME, typeName: "AOI" }],
        timeZone: "UTC",
    };

    it("builds DPMO worst-parts preset with Clean + Real", () => {
        const built = DPMO_L6_AUG_PRESETS[0]!.build(ctx);
        expect(built).toMatchObject({
            sourceId: "postreflow",
            groupBy: "PartNumber",
            numerator: "Real",
            skipExclusion: "Clean",
            machineIds: [99],
        });
        expect(built!.startUtc).toMatch(/^2026-08-01/);
    });

    it("builds Pareto worst-parts preset with Clean + Real", () => {
        const built = PARETO_L6_AUG_PRESETS.find(
            (p) => p.id === "l6-aug-pareto-worst-parts",
        )!.build(ctx);
        expect(built).toMatchObject({
            axis: "PartNumber",
            skipExclusion: "Clean",
            numerator: "Real",
            machineIds: [99],
        });
    });

    it("returns null when L6PSTAOI is not in the machine list", () => {
        const built = DPMO_L6_AUG_PRESETS[0]!.build({ machines: [], timeZone: "UTC" });
        expect(built).toBeNull();
    });
});
