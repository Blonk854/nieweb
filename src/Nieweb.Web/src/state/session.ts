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
    /**
     * True when the account is flagged for forced password rotation
     * (bootstrap admin, freshly-created accounts, admin-initiated
     * resets). The router's auth guard bounces the user to
     * /account/password until they successfully rotate the password.
     */
    mustRotatePassword: boolean;
};

type SessionState = {
    user: SessionUser | null;
    token: string | null;
    setSession: (user: SessionUser, token: string) => void;
    setMustRotatePassword: (value: boolean) => void;
    clear: () => void;
};

export const useSessionStore = create<SessionState>()(
    persist(
        (set) => ({
            user: null,
            token: null,
            setSession: (user, token) => set({ user, token }),
            setMustRotatePassword: (value) =>
                set((state) =>
                    state.user
                        ? {
                              user: { ...state.user, mustRotatePassword: value },
                          }
                        : state,
                ),
            clear: () => set({ user: null, token: null }),
        }),
        {
            name: "nieweb.session.v1",
            storage: createJSONStorage(() => localStorage),
            // Only persist token + user - never persist derived helpers.
            partialize: (s) => ({ user: s.user, token: s.token }),
            // Older persisted sessions (pre-rotation) didn't carry the
            // mustRotatePassword flag. Default it to false so a browser
            // that upgrades the SPA doesn't get bounced to the
            // change-password screen unexpectedly; the next /auth/whoami
            // roundtrip will supply the authoritative value.
            migrate: (persistedState, _version) => {
                const state = persistedState as
                    | { user: (Partial<SessionUser> & { email: string }) | null; token: string | null }
                    | undefined;
                if (state?.user && state.user.mustRotatePassword === undefined) {
                    return {
                        user: {
                            email: state.user.email,
                            displayName: state.user.displayName ?? state.user.email,
                            roles: state.user.roles ?? [],
                            mustRotatePassword: false,
                        },
                        token: state.token,
                    };
                }
                return persistedState as
                    | { user: SessionUser | null; token: string | null }
                    | undefined;
            },
        },
    ),
);

/** Convenience selector for guarded routes and API calls. */
export const useAuthToken = () => useSessionStore((s) => s.token);
