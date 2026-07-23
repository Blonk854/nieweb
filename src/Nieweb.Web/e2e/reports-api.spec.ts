import { expect, test } from "@playwright/test";
import {
    FIXTURE_END_UTC,
    FIXTURE_SOURCE_ID,
    FIXTURE_START_UTC,
    loginForToken,
} from "./support";

/**
 * API-only happy-path smokes for the report types that don't have
 * their own SPA route (phase-2.md T4). DPMO table, Trend, and
 * Deviation render as tiles inside the admin report editor's
 * canvas — smoking them at the endpoint level exercises the same
 * report pipeline the tiles depend on without needing a full
 * canvas fixture.
 *
 * Every assertion is against the FakeAoiSource fixture (ten panels
 * on 2026-01-15 UTC, 15 defect-bits across 200 opportunities;
 * FPY = 50%, DPMO = 75 000 PPM).
 */

test.describe("Report endpoint API smokes", () => {
    test("DPMO table (groupBy=Defect) reports 15 defect-bits", async ({
        request,
    }) => {
        const token = await loginForToken(request);
        const qs = new URLSearchParams({
            sourceId: FIXTURE_SOURCE_ID,
            startUtc: FIXTURE_START_UTC,
            endUtc: FIXTURE_END_UTC,
            groupBy: "Defect",
            numerator: "Aoi",
            opportunity: "All",
        }).toString();

        const resp = await request.get(`/api/reports/dpmo-table?${qs}`, {
            headers: { Authorization: `Bearer ${token}` },
        });
        expect(resp.status(), "dpmo-table should return 200").toBe(200);
        const body = (await resp.json()) as {
            overall: {
                defectBitCount: number;
                opportunityCount: number;
                dpmoPpm: number;
            };
            rows: unknown[];
        };
        expect(body.overall.defectBitCount).toBe(15);
        expect(body.overall.opportunityCount).toBe(200);
        expect(Math.round(body.overall.dpmoPpm)).toBe(75_000);
        // Five distinct defect bits set on the fixture.
        expect(body.rows.length).toBe(5);
    });

    test("Trend (FpyAoi, hourly) buckets the fixture panels", async ({
        request,
    }) => {
        const token = await loginForToken(request);
        const qs = new URLSearchParams({
            sourceId: FIXTURE_SOURCE_ID,
            startUtc: FIXTURE_START_UTC,
            endUtc: FIXTURE_END_UTC,
            bucket: "Hour1",
            metrics: "FpyAoi",
        }).toString();

        const resp = await request.get(`/api/reports/trend?${qs}`, {
            headers: { Authorization: `Bearer ${token}` },
        });
        expect(resp.status(), "trend should return 200").toBe(200);
        const body = (await resp.json()) as {
            bucket: string;
            series: Array<{ metric: string; displayName: string; unit: string }>;
            buckets: Array<{
                label: string;
                startUtc: string;
                endUtcExclusive: string;
                values: Record<string, number | null>;
            }>;
        };
        expect(body.bucket).toBe("Hour1");
        expect(body.series.length).toBe(1);
        expect(body.series[0].metric).toBe("FpyAoi");
        expect(body.buckets.length).toBeGreaterThan(0);
        // Every bucket that has at least one panel must have a
        // non-null FPY value; the fixture spans hours 08..10 UTC
        // and every panel is either 100% or 0% clean, so at least
        // one bucket should contain a finite FPY number.
        const finite = body.buckets
            .map((b) => b.values.FpyAoi)
            .filter(
                (v): v is number => typeof v === "number" && Number.isFinite(v),
            );
        expect(finite.length).toBeGreaterThan(0);
    });

    test("Deviation (DeltaX, components) returns a histogram", async ({
        request,
    }) => {
        const token = await loginForToken(request);
        const qs = new URLSearchParams({
            sourceId: FIXTURE_SOURCE_ID,
            startUtc: FIXTURE_START_UTC,
            endUtc: FIXTURE_END_UTC,
            axis: "DeltaX",
            opportunity: "Components",
        }).toString();

        const resp = await request.get(`/api/reports/deviation?${qs}`, {
            headers: { Authorization: `Bearer ${token}` },
        });
        expect(resp.status(), "deviation should return 200").toBe(200);
        const body = (await resp.json()) as {
            axis: string;
            sampleCount: number;
            bins: Array<{ index: number; count: number }>;
        };
        expect(body.axis).toBe("DeltaX");
        // 10 panels × 16 components = 160 component-level tested
        // objects contribute to the DeltaX histogram.
        expect(body.sampleCount).toBe(160);
        expect(body.bins.length).toBeGreaterThan(0);
    });
});
