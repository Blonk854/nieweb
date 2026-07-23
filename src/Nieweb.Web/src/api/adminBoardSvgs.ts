import { apiFetch } from "./client";

/**
 * Admin-only board-SVG management API. Mirrors the .NET endpoints in
 * `Nieweb.Api/Endpoints/AdminBoardSvgSourcesEndpoints.cs` and
 * `Nieweb.Api/Endpoints/AdminBoardSvgOperationsEndpoints.cs`
 * (docs/phase-2.md §7.5 TC4).
 *
 * All calls require an authenticated Admin role — the API returns 403
 * otherwise and the client should route the user back to a
 * "forbidden" screen (see routes/admin-board-svgs.tsx).
 */

/** Row DTO for a configured board-SVG source (one AOI machine). */
export type BoardSvgSourceDto = {
    id: number;
    machineName: string;
    uncPath: string;
    isEnabled: boolean;
    lastSyncedUtc: string | null;
    lastSyncErrorUtc: string | null;
    lastSyncError: string | null;
    createdUtc: string;
    lastModifiedUtc: string;
};

export type CreateBoardSvgSourceRequest = {
    machineName: string;
    uncPath: string;
    isEnabled: boolean;
};

export type UpdateBoardSvgSourceRequest = {
    machineName: string;
    uncPath: string;
    isEnabled: boolean;
};

/** Aggregate status view for the cache + sources. */
export type BoardSvgStatusDto = {
    cacheDirectory: string;
    cacheDirectoryExists: boolean;
    intervalSeconds: number;
    syncEnabled: boolean;
    sources: BoardSvgStatusSourceDto[];
    cache: BoardSvgStatusCacheEntryDto[];
    knownProducts: string[];
    missingProducts: string[];
};

export type BoardSvgStatusSourceDto = {
    id: number;
    machineName: string;
    uncPath: string;
    isEnabled: boolean;
    lastSyncedUtc: string | null;
    lastSyncErrorUtc: string | null;
    lastSyncError: string | null;
};

export type BoardSvgStatusCacheEntryDto = {
    productName: string;
    fileName: string;
    sizeBytes: number;
    lastWriteTimeUtc: string;
};

/** Result of one on-demand sweep triggered via POST /sync. */
export type BoardSvgSyncResultDto = {
    startedUtc: string;
    completedUtc: string;
    cacheDirectory: string;
    sources: BoardSvgSyncSourceOutcome[];
    products: BoardSvgSyncProductOutcome[];
};

export type BoardSvgSyncSourceOutcome = {
    sourceId: number;
    machineName: string;
    uncPath: string;
    enabled: boolean;
    reachable: boolean;
    filesEnumerated: number;
    error: string | null;
};

export type BoardSvgSyncProductOutcome = {
    productName: string;
    alreadyCached: boolean;
    copied: boolean;
    sourceMachineName: string | null;
    sourceFileLastWriteUtc: string | null;
    bytesCopied: number | null;
    error: string | null;
};

// ------------------------------------------------------------- Sources CRUD

export function listBoardSvgSources(): Promise<BoardSvgSourceDto[]> {
    return apiFetch<BoardSvgSourceDto[]>("/api/admin/board-svgs/sources");
}

export function createBoardSvgSource(
    body: CreateBoardSvgSourceRequest,
): Promise<BoardSvgSourceDto> {
    return apiFetch<BoardSvgSourceDto>("/api/admin/board-svgs/sources", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

export function updateBoardSvgSource(
    id: number,
    body: UpdateBoardSvgSourceRequest,
): Promise<BoardSvgSourceDto> {
    return apiFetch<BoardSvgSourceDto>(
        `/api/admin/board-svgs/sources/${id}`,
        {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body),
        },
    );
}

export function deleteBoardSvgSource(id: number): Promise<void> {
    return apiFetch<void>(`/api/admin/board-svgs/sources/${id}`, {
        method: "DELETE",
    });
}

// ---------------------------------------------------------------- Operations

export function getBoardSvgStatus(): Promise<BoardSvgStatusDto> {
    return apiFetch<BoardSvgStatusDto>("/api/admin/board-svgs/status");
}

export function syncBoardSvgsNow(): Promise<BoardSvgSyncResultDto> {
    return apiFetch<BoardSvgSyncResultDto>("/api/admin/board-svgs/sync", {
        method: "POST",
    });
}
