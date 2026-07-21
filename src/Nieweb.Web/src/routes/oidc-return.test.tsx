import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import {
    createMemoryHistory,
    createRootRoute,
    createRoute,
    createRouter,
    Outlet,
    RouterProvider,
} from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import i18n from "../i18n";
import { OidcReturnRoute } from "./oidc-return";
import { useSessionStore } from "../state/session";

/**
 * Exercises the URL-fragment handoff between the API's OIDC callback
 * and the SPA. Success path: parse fragment, hydrate session store
 * via /auth/whoami, redirect to the returnUrl. Failure path: render
 * a localised error and clear the fragment so it can't be recovered
 * via Back.
 */

function HomeStub() {
    return <h1 data-testid="home-stub">Home stub</h1>;
}

function ReportStub() {
    return <h1 data-testid="report-stub">Panel yield stub</h1>;
}

function ChangePasswordStub() {
    return <h1 data-testid="change-password-stub">Change password stub</h1>;
}

function renderOidcReturn(fragment: string, returnPath: string = "/") {
    // window.location.hash needs to reflect the fragment BEFORE the
    // component mounts (the parser runs inside useMemo on first
    // render). We set it via history.replaceState so no navigation
    // occurs and no listeners fire.
    window.history.replaceState(null, "", "/oidc-return#" + fragment);

    const rootRoute = createRootRoute({ component: Outlet });
    const home = createRoute({
        getParentRoute: () => rootRoute,
        path: "/",
        component: HomeStub,
    });
    const oidcReturn = createRoute({
        getParentRoute: () => rootRoute,
        path: "/oidc-return",
        component: OidcReturnRoute,
    });
    const report = createRoute({
        getParentRoute: () => rootRoute,
        path: "/report/panel-yield",
        component: ReportStub,
    });
    const change = createRoute({
        getParentRoute: () => rootRoute,
        path: "/account/password",
        component: ChangePasswordStub,
    });
    const routeTree = rootRoute.addChildren([home, oidcReturn, report, change]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: [returnPath] }),
    });
    // Manually navigate to /oidc-return; memory history's initial
    // entry can't carry a fragment, so we simulate the arrival.
    router.navigate({ to: "/oidc-return" });
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    return render(
        <MantineProvider>
            <QueryClientProvider client={client}>
                <RouterProvider router={router} />
            </QueryClientProvider>
        </MantineProvider>,
    );
}

describe("OidcReturnRoute", () => {
    beforeEach(() => {
        void i18n.changeLanguage("en");
        useSessionStore.getState().clear();
        window.localStorage.clear();
    });

    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
        window.history.replaceState(null, "", "/");
    });

    it("parses the fragment, hydrates the session, and scrubs the URL", async () => {
        const fetchMock = vi.fn(async (url: RequestInfo | URL) => {
            const asString =
                typeof url === "string"
                    ? url
                    : url instanceof URL
                        ? url.toString()
                        : url.url;
            if (asString.endsWith("/auth/whoami")) {
                return new Response(
                    JSON.stringify({
                        userId: "user-sso-1",
                        email: "alice@contoso.com",
                        name: "Alice Contoso",
                        roles: ["Reader"],
                        mustRotatePassword: false,
                    }),
                    { status: 200, headers: { "Content-Type": "application/json" } },
                );
            }
            throw new Error(`Unexpected fetch: ${asString}`);
        });
        vi.stubGlobal("fetch", fetchMock);

        renderOidcReturn(
            "accessToken=jwt-sso-xyz"
                + "&expiresUtc=2099-01-01T00:00:00Z"
                + "&mustRotatePassword=false"
                + "&returnUrl=" + encodeURIComponent("/app/report/panel-yield"),
        );

        await waitFor(() => {
            const state = useSessionStore.getState();
            expect(state.token).toBe("jwt-sso-xyz");
            expect(state.user?.email).toBe("alice@contoso.com");
        });

        // Fragment must be scrubbed so a Back navigation can't reveal
        // the JWT.
        expect(window.location.hash).toBe("");
    });

    it("renders a localised error when the server hands back ?error=", async () => {
        renderOidcReturn(
            "error=LocalAccountConflict&message="
                + encodeURIComponent("Email is already registered locally."),
        );

        // No JWT is issued; session store must remain empty.
        expect(useSessionStore.getState().token).toBeNull();

        expect(
            await screen.findByText(/already registered as a local account/i),
        ).toBeInTheDocument();
        // Fragment must be scrubbed on the error path too.
        expect(window.location.hash).toBe("");
    });

    it("renders a generic error when the fragment lacks an access token", async () => {
        renderOidcReturn("returnUrl=" + encodeURIComponent("/app/"));

        expect(
            await screen.findByText(/single sign-on failed/i),
        ).toBeInTheDocument();
        expect(useSessionStore.getState().token).toBeNull();
    });
});
