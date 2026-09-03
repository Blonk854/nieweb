import { apiFetch } from "./client";

export type AnalyseDashboardId =
    | "Live"
    | "LinePerformance"
    | "Product"
    | "Panel"
    | "CpCpk";

export type AnalyseFeatureAvailability = {
    featureId: string;
    supported: boolean;
    missingCapability: string | null;
    note: string | null;
};

export type AnalyseDashboardAvailability = {
    dashboard: AnalyseDashboardId;
    supported: boolean;
    missingCapabilities: string[];
    features: AnalyseFeatureAvailability[];
};

export type AnalyseContractsResult = {
    source: {
        id: string;
        displayName: string;
        schemaVersion: string;
        caps: number | string;
    };
    filter: {
        window: {
            startUtc: string;
            endUtcExclusive: string;
            startEpochSeconds: number;
            endEpochSecondsExclusive: number;
        };
        machineIds: number[] | null;
        productIds: number[] | null;
        onlyLastInspection: boolean;
    };
    dashboards: AnalyseDashboardAvailability[];
};

export type AnalyseLiveSummaryResult = {
    source: {
        id: string;
        displayName: string;
        schemaVersion: string;
        caps: number | string;
    };
    filter: {
        window: {
            startUtc: string;
            endUtcExclusive: string;
            startEpochSeconds: number;
            endEpochSecondsExclusive: number;
        };
        machineIds: number[] | null;
        productIds: number[] | null;
        onlyLastInspection: boolean;
    };
    kpi: {
        totalPanels: number;
        inspectedPanels: number;
        goodPanels: number;
        faultyPanels: number;
        notInspectedPanels: number;
        fpyPercent: number;
    };
    dedupeAppliedInMemory: boolean;
    dedupeNote: string | null;
};

export type AnalyseLinePerformanceResult = {
    source: {
        id: string;
        displayName: string;
        schemaVersion: string;
        caps: number | string;
    };
    filter: {
        window: {
            startUtc: string;
            endUtcExclusive: string;
            startEpochSeconds: number;
            endEpochSecondsExclusive: number;
        };
        machineIds: number[] | null;
        productIds: number[] | null;
        onlyLastInspection: boolean;
    };
    overallYield: {
        totalPanels: number;
        inspectedPanels: number;
        goodPanels: number;
        faultyPanels: number;
        notInspectedPanels: number;
        fpyPercent: number;
    };
    overallDpmo: {
        testedObjectCount: number;
        opportunityCount: number;
        defectBitCount: number;
        dpmoPpm: number;
    };
    byMachine: Array<{
        machineId: number;
        machineName: string | null;
        yield: {
            totalPanels: number;
            inspectedPanels: number;
            goodPanels: number;
            faultyPanels: number;
            notInspectedPanels: number;
            fpyPercent: number;
        };
        dpmo: {
            testedObjectCount: number;
            opportunityCount: number;
            defectBitCount: number;
            dpmoPpm: number;
        };
    }>;
    dedupeAppliedInMemory: boolean;
    dedupeNote: string | null;
};

export type AnalyseProductSummaryResult = {
    source: {
        id: string;
        displayName: string;
        schemaVersion: string;
        caps: number | string;
    };
    filter: {
        window: {
            startUtc: string;
            endUtcExclusive: string;
            startEpochSeconds: number;
            endEpochSecondsExclusive: number;
        };
        machineIds: number[] | null;
        productIds: number[] | null;
        onlyLastInspection: boolean;
    };
    overallYield: {
        totalPanels: number;
        inspectedPanels: number;
        goodPanels: number;
        faultyPanels: number;
        notInspectedPanels: number;
        fpyPercent: number;
    };
    overallDpmo: {
        testedObjectCount: number;
        opportunityCount: number;
        defectBitCount: number;
        dpmoPpm: number;
    };
    products: Array<{
        productId: number;
        productName: string | null;
        yield: {
            totalPanels: number;
            inspectedPanels: number;
            goodPanels: number;
            faultyPanels: number;
            notInspectedPanels: number;
            fpyPercent: number;
        };
        dpmo: {
            testedObjectCount: number;
            opportunityCount: number;
            defectBitCount: number;
            dpmoPpm: number;
        };
        defectBitCount: number;
        topDefectBits: Array<{ bitNumber: number; count: number }>;
    }>;
    dedupeAppliedInMemory: boolean;
    dedupeNote: string | null;
};

export type AnalyseProductDetailResult = {
    source: {
        id: string;
        displayName: string;
        schemaVersion: string;
        caps: number | string;
    };
    filter: {
        window: {
            startUtc: string;
            endUtcExclusive: string;
            startEpochSeconds: number;
            endEpochSecondsExclusive: number;
        };
        productId: number;
        bucket: "Day" | "Week";
        machineIds: number[] | null;
        onlyLastInspection: boolean;
    };
    productId: number;
    productName: string | null;
    overallYield: {
        totalPanels: number;
        inspectedPanels: number;
        goodPanels: number;
        faultyPanels: number;
        notInspectedPanels: number;
        fpyPercent: number;
    };
    overallDpmo: {
        testedObjectCount: number;
        opportunityCount: number;
        defectBitCount: number;
        dpmoPpm: number;
    };
    buckets: Array<{
        index: number;
        label: string;
        startUtc: string;
        endUtcExclusive: string;
    }>;
    trend: Array<{
        bucketIndex: number;
        label: string;
        yield: {
            totalPanels: number;
            inspectedPanels: number;
            goodPanels: number;
            faultyPanels: number;
            notInspectedPanels: number;
            fpyPercent: number;
        };
        dpmo: {
            testedObjectCount: number;
            opportunityCount: number;
            defectBitCount: number;
            dpmoPpm: number;
        };
        defectBitCount: number;
        topDefectBits: Array<{ bitNumber: number; count: number }>;
    }>;
    topDefectBits: Array<{ bitNumber: number; count: number }>;
    dedupeAppliedInMemory: boolean;
    dedupeNote: string | null;
};

export type AnalyseContractsQuery = {
    sourceId?: string;
    startUtc?: string;
    endUtc?: string;
    machineIds?: number[];
    productIds?: number[];
    onlyLastInspection?: boolean;
};

export type AnalyseProductDetailQuery = {
    sourceId?: string;
    startUtc?: string;
    endUtc?: string;
    machineIds?: number[];
    onlyLastInspection?: boolean;
    bucket?: "Day" | "Week";
};

export async function fetchAnalyseContracts(
    query: AnalyseContractsQuery,
): Promise<AnalyseContractsResult> {
    const qs = new URLSearchParams();
    if (query.sourceId) qs.set("sourceId", query.sourceId);
    if (query.startUtc) qs.set("startUtc", query.startUtc);
    if (query.endUtc) qs.set("endUtc", query.endUtc);
    if (query.machineIds && query.machineIds.length > 0) {
        qs.set("machineIds", query.machineIds.join(","));
    }
    if (query.productIds && query.productIds.length > 0) {
        qs.set("productIds", query.productIds.join(","));
    }
    if (query.onlyLastInspection !== undefined) {
        qs.set("onlyLastInspection", query.onlyLastInspection ? "true" : "false");
    }

    const suffix = qs.toString();
    return apiFetch<AnalyseContractsResult>(
        suffix ? `/api/analyse/contracts?${suffix}` : "/api/analyse/contracts",
    );
}

export async function fetchAnalyseLiveSummary(
    query: AnalyseContractsQuery,
): Promise<AnalyseLiveSummaryResult> {
    const qs = new URLSearchParams();
    if (query.sourceId) qs.set("sourceId", query.sourceId);
    if (query.startUtc) qs.set("startUtc", query.startUtc);
    if (query.endUtc) qs.set("endUtc", query.endUtc);
    if (query.machineIds && query.machineIds.length > 0) {
        qs.set("machineIds", query.machineIds.join(","));
    }
    if (query.productIds && query.productIds.length > 0) {
        qs.set("productIds", query.productIds.join(","));
    }
    if (query.onlyLastInspection !== undefined) {
        qs.set("onlyLastInspection", query.onlyLastInspection ? "true" : "false");
    }

    const suffix = qs.toString();
    return apiFetch<AnalyseLiveSummaryResult>(
        suffix ? `/api/analyse/live-summary?${suffix}` : "/api/analyse/live-summary",
    );
}

export async function fetchAnalyseLinePerformanceSummary(
    query: AnalyseContractsQuery,
): Promise<AnalyseLinePerformanceResult> {
    const qs = new URLSearchParams();
    if (query.sourceId) qs.set("sourceId", query.sourceId);
    if (query.startUtc) qs.set("startUtc", query.startUtc);
    if (query.endUtc) qs.set("endUtc", query.endUtc);
    if (query.machineIds && query.machineIds.length > 0) {
        qs.set("machineIds", query.machineIds.join(","));
    }
    if (query.productIds && query.productIds.length > 0) {
        qs.set("productIds", query.productIds.join(","));
    }
    if (query.onlyLastInspection !== undefined) {
        qs.set("onlyLastInspection", query.onlyLastInspection ? "true" : "false");
    }

    const suffix = qs.toString();
    return apiFetch<AnalyseLinePerformanceResult>(
        suffix ? `/api/analyse/line-performance-summary?${suffix}` : "/api/analyse/line-performance-summary",
    );
}

export async function fetchAnalyseProductSummary(
    query: AnalyseContractsQuery,
): Promise<AnalyseProductSummaryResult> {
    const qs = new URLSearchParams();
    if (query.sourceId) qs.set("sourceId", query.sourceId);
    if (query.startUtc) qs.set("startUtc", query.startUtc);
    if (query.endUtc) qs.set("endUtc", query.endUtc);
    if (query.machineIds && query.machineIds.length > 0) {
        qs.set("machineIds", query.machineIds.join(","));
    }
    if (query.productIds && query.productIds.length > 0) {
        qs.set("productIds", query.productIds.join(","));
    }
    if (query.onlyLastInspection !== undefined) {
        qs.set("onlyLastInspection", query.onlyLastInspection ? "true" : "false");
    }

    const suffix = qs.toString();
    return apiFetch<AnalyseProductSummaryResult>(
        suffix ? `/api/analyse/product-summary?${suffix}` : "/api/analyse/product-summary",
    );
}

export async function fetchAnalyseProductDetail(
    productId: number,
    query: AnalyseProductDetailQuery,
): Promise<AnalyseProductDetailResult> {
    const qs = new URLSearchParams();
    if (query.sourceId) qs.set("sourceId", query.sourceId);
    if (query.startUtc) qs.set("startUtc", query.startUtc);
    if (query.endUtc) qs.set("endUtc", query.endUtc);
    if (query.machineIds && query.machineIds.length > 0) {
        qs.set("machineIds", query.machineIds.join(","));
    }
    if (query.onlyLastInspection !== undefined) {
        qs.set("onlyLastInspection", query.onlyLastInspection ? "true" : "false");
    }
    if (query.bucket) {
        qs.set("bucket", query.bucket);
    }

    const suffix = qs.toString();
    return apiFetch<AnalyseProductDetailResult>(
        suffix
            ? `/api/analyse/product-detail/${productId}?${suffix}`
            : `/api/analyse/product-detail/${productId}`,
    );
}
