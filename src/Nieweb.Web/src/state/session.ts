import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";

/**
 * Session store: holds the currently-signed-in user and the raw JWT
 * bearer token issued by /api/auth/token. Persisted to localStorage so
 * a page reload keeps the user signed in until the token expires.
 *
 * F2 wires the shell only - real auth flow (login form, refresh, sign-out)
 * lands in a later frontend backlog item alongside the /login route work.
 */
export type SessionUser = {
    email: string;
    displayName: string;
    roles: readonly string[];
};

type SessionState = {
    user: SessionUser | null;
    token: string | null;
    setSession: (user: SessionUser, token: string) => void;
    clear: () => void;
};

export const useSessionStore = create<SessionState>()(
    persist(
        (set) => ({
            user: null,
            token: null,
            setSession: (user, token) => set({ user, token }),
            clear: () => set({ user: null, token: null }),
        }),
        {
            name: "nieweb.session.v1",
            storage: createJSONStorage(() => localStorage),
            // Only persist token + user - never persist derived helpers.
            partialize: (s) => ({ user: s.user, token: s.token }),
        },
    ),
);

/** Convenience selector for guarded routes and API calls. */
export const useAuthToken = () => useSessionStore((s) => s.token);
