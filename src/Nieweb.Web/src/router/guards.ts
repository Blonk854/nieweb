import { redirect } from "@tanstack/react-router";

import { useSessionStore } from "../state/session";

/**
 * Route guards enforced in TanStack Router `beforeLoad` hooks.
 *
 * These give the SPA a defence-in-depth story on top of the API's
 * `[Authorize]` policies: unauthenticated users are bounced to the
 * sign-in page before any protected component ever mounts (so the
 * network doesn't see the resulting 401), and the URL they were
 * trying to reach is preserved via `?redirect=<path>` so the login
 * route can send them back after a successful sign-in.
 *
 * Because the session store is Zustand-with-localStorage and its
 * storage adapter is synchronous, `useSessionStore.getState()` returns
 * fully-rehydrated state on the first tick — safe to read from
 * `beforeLoad` without awaiting anything.
 *
 * Role checks intentionally live inside route components (see
 * `AdminUsersRoute` reading `useSessionStore((s) => s.user?.roles)`)
 * so each feature can render its own localised forbidden panel rather
 * than relying on a generic denial screen.
 */
export function requireAuthentication(currentPath: string): void {
    const user = useSessionStore.getState().user;
    if (!user) {
        throw redirect({
            to: "/login",
            search: { redirect: currentPath },
        });
    }
}
