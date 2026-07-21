import { QueryClient } from "@tanstack/react-query";

/**
 * Shared TanStack Query client for the SPA. Defaults are tuned for a
 * reporting app: retries are off (a failing /api call should surface
 * fast, not silently retry), stale time is 30 s (report data does not
 * churn faster than that), and refetch-on-focus is off (line engineers
 * flip between tabs constantly and don't want a re-query storm).
 */
export function createQueryClient(): QueryClient {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                staleTime: 30_000,
                refetchOnWindowFocus: false,
            },
        },
    });
}
