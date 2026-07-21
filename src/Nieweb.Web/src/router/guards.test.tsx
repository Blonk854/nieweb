import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
    createMemoryHistory,
    createRootRoute,
    createRoute,
    createRouter,
    Outlet,
    RouterProvider,
} from "@tanstack/react-router";

import i18n from "../i18n";
import { useSessionStore } from "../state/session";
import { LoginRoute } from "../routes/login";
import { validateLoginSearch } from "../routes/login.search";
import { requireAuthentication } from "./guards";

/**
 * Exercises the auth guard end-to-end: an unauthenticated visit to a
 * protected route must be bounced to /login with the original URL
 * preserved in the `redirect` search param.
 */

function ProtectedRouteStub() {
    return <h1 data-testid="protected">Protected content</h1>;
}

function ChangePasswordStub() {
    return <h1 data-testid="change-password-stub">Change password stub</h1>;
}

function renderGuardedRouterAt(initialPath: string) {
    const rootRoute = createRootRoute({ component: Outlet });
    const login = createRoute({
        getParentRoute: () => rootRoute,
        path: "/login",
        component: LoginRoute,
        validateSearch: validateLoginSearch,
    });
    const protectedRoute = createRoute({
        getParentRoute: () => rootRoute,
        path: "/report/panel-yield",
        component: ProtectedRouteStub,
        beforeLoad: ({ location }) => requireAuthentication(location.href),
    });
    const changePassword = createRoute({
        getParentRoute: () => rootRoute,
        path: "/account/password",
        component: ChangePasswordStub,
    });
    const routeTree = rootRoute.addChildren([login, protectedRoute, changePassword]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: [initialPath] }),
    });
    const client = new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
        },
    });
    return {
        router,
        ...render(
            <MantineProvider>
                <QueryClientProvider client={client}>
                    <RouterProvider router={router} />
                </QueryClientProvider>
            </MantineProvider>,
        ),
    };
}

describe("requireAuthentication", () => {
    beforeEach(async () => {
        await i18n.changeLanguage("en");
        useSessionStore.getState().clear();
        window.localStorage.clear();
    });

    afterEach(() => {
        cleanup();
        vi.restoreAllMocks();
    });

    it("bounces unauthenticated visitors of /report/panel-yield to /login and preserves the target url", async () => {
        const { router } = renderGuardedRouterAt(
            "/report/panel-yield?sourceId=postreflow",
        );

        await waitFor(() =>
            expect(
                screen.getByRole("heading", { name: /sign in to nieweb/i }),
            ).toBeInTheDocument(),
        );
        expect(screen.queryByTestId("protected")).not.toBeInTheDocument();

        const location = router.state.location;
        expect(location.pathname).toBe("/login");
        expect((location.search as { redirect?: string }).redirect).toBe(
            "/report/panel-yield?sourceId=postreflow",
        );
    });

    it("lets signed-in visitors reach the protected route", async () => {
        useSessionStore.getState().setSession(
            {
                email: "reader@nieweb.local",
                displayName: "Reader",
                roles: ["Reader"],
                mustRotatePassword: false,
            },
            "existing-token",
        );

        renderGuardedRouterAt("/report/panel-yield");

        expect(await screen.findByTestId("protected")).toBeInTheDocument();
    });

    it("bounces a signed-in user with the mustRotatePassword flag to /account/password", async () => {
        useSessionStore.getState().setSession(
            {
                email: "rotator@nieweb.local",
                displayName: "Rotator",
                roles: ["Reader"],
                mustRotatePassword: true,
            },
            "existing-token",
        );

        renderGuardedRouterAt("/report/panel-yield?sourceId=postreflow");

        expect(
            await screen.findByTestId("change-password-stub"),
        ).toBeInTheDocument();
        expect(screen.queryByTestId("protected")).not.toBeInTheDocument();
    });
});

describe("validateLoginSearch", () => {
    it("returns an empty object when no redirect is supplied", () => {
        expect(validateLoginSearch({})).toEqual({});
    });

    it("keeps a plain relative path", () => {
        expect(validateLoginSearch({ redirect: "/report/panel-yield" })).toEqual({
            redirect: "/report/panel-yield",
        });
    });

    it("keeps a relative path with a query string", () => {
        expect(
            validateLoginSearch({
                redirect: "/report/panel-yield?sourceId=postreflow",
            }),
        ).toEqual({ redirect: "/report/panel-yield?sourceId=postreflow" });
    });

    it("drops absolute-URL redirect targets", () => {
        expect(
            validateLoginSearch({ redirect: "https://evil.example.com/steal" }),
        ).toEqual({});
    });

    it("drops protocol-relative redirect targets", () => {
        expect(
            validateLoginSearch({ redirect: "//evil.example.com/steal" }),
        ).toEqual({});
    });

    it("drops redirect targets containing a backslash", () => {
        expect(
            validateLoginSearch({ redirect: "/\\evil.example.com" }),
        ).toEqual({});
    });

    it("drops redirect targets that do not start with a slash", () => {
        expect(validateLoginSearch({ redirect: "report/panel-yield" })).toEqual(
            {},
        );
    });

    it("drops non-string redirect values", () => {
        expect(validateLoginSearch({ redirect: 42 })).toEqual({});
        expect(validateLoginSearch({ redirect: null })).toEqual({});
    });

    it("drops overly long redirect targets", () => {
        const long = "/" + "a".repeat(600);
        expect(validateLoginSearch({ redirect: long })).toEqual({});
    });
});
