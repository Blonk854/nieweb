import { apiFetch } from "./client";

/**
 * `/api/saved-views` response item. Matches SavedViewEndpoints.SavedViewDto
 * (System.Text.Json camelCase serialization).
 */
export type SavedView = {
    id: number;
    name: string;
    reportKey: string;
    /** Opaque JSON string; report-specific shape (parsed by the report). */
    filterJson: string;
    isShared: boolean;
    /** True when the current user is the row's owner. Only owners may edit/delete. */
    isOwner: boolean;
    createdUtc: string;
    lastModifiedUtc: string;
};

/** `POST /api/saved-views` payload. */
export type CreateSavedViewRequest = {
    name: string;
    reportKey: string;
    filterJson: string;
    isShared: boolean;
};

/** `PUT /api/saved-views/{id}` payload. */
export type UpdateSavedViewRequest = {
    name: string;
    filterJson: string;
    isShared: boolean;
};

/**
 * List every saved view visible to the current user for the given
 * report (own views + shared views from other users). Rows are already
 * sorted by name on the server.
 */
export function fetchSavedViews(reportKey: string): Promise<SavedView[]> {
    const q = new URLSearchParams({ reportKey });
    return apiFetch<SavedView[]>(`/api/saved-views?${q.toString()}`);
}

/** Create a new saved view. Returns the persisted row. */
export function createSavedView(body: CreateSavedViewRequest): Promise<SavedView> {
    return apiFetch<SavedView>("/api/saved-views", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

/** Update an existing saved view. Owner only; non-owners get 403. */
export function updateSavedView(id: number, body: UpdateSavedViewRequest): Promise<SavedView> {
    return apiFetch<SavedView>(`/api/saved-views/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

/** Delete a saved view. Owner only; non-owners get 403. */
export function deleteSavedView(id: number): Promise<void> {
    return apiFetch<void>(`/api/saved-views/${id}`, { method: "DELETE" });
}
