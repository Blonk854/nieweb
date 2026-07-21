import { redirect } from "@tanstack/react-router";

import { useSessionStore } from "../state/session";

/**
 * Path of the forced-password-rotation screen. Kept as a constant so
 * the guard, the router registration, and the login redirect all
 * agree on it.
 */
export const CHANGE_PASSWORD_PATH = "/account/password" as const;

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
 *
 * Password-rotation guard: if the signed-in user carries the
 * `mustRotatePassword` flag we bounce them to the change-password
 * screen regardless of where they were trying to go. This is a
 * belt-and-braces companion to the server-side enforcement — the API
 * still refuses to serve reports until the flag is cleared via
 * `POST /auth/change-password`.
 */
export function requireAuthentication(currentPath: string): void {
    const user = useSessionStore.getState().user;
    if (!user) {
        throw redirect({
            to: "/login",
            search: { redirect: currentPath },
        });
    }
    if (user.mustRotatePassword) {
        throw redirect({ to: CHANGE_PASSWORD_PATH });
    }
}
