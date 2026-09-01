import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Mock } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { userEvent } from "@testing-library/user-event";
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
import { LoginRoute } from "./login";
import { validateLoginSearch } from "./login.search";
import { useSessionStore } from "../state/session";

/**
 * These tests exercise the sign-in route end-to-end at the component
 * level: form validation, POST /auth/login + GET /auth/whoami wiring,
 * session-store hydration, 401 error surfacing, and the signed-in
 * "sign out" affordance. `fetch` is stubbed per-test so no real HTTP
 * goes out.
 */

function HomeStub() {
    return <h1>Home stub</h1>;
}

function ReportStub() {
    return <h1 data-testid="report-stub">Panel yield stub</h1>;
}

function ChangePasswordStub() {
    return <h1 data-testid="change-password-stub">Change password stub</h1>;
}

function renderLogin(initialPath: string = "/login") {
    const rootRoute = createRootRoute({ component: Outlet });
    const home = createRoute({
        getParentRoute: () => rootRoute,
        path: "/",
        component: HomeStub,
    });
    const login = createRoute({
        getParentRoute: () => rootRoute,
        path: "/login",
        component: LoginRoute,
        validateSearch: validateLoginSearch,
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
    const routeTree = rootRoute.addChildren([home, login, report, change]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: [initialPath] }),
    });
    // Fresh QueryClient per render so mutations don't leak across tests.
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

function stubFetch(
    responses: Array<{
        match: (url: string, init?: RequestInit) => boolean;
        status: number;
        body: unknown;
    }>,
) {
    // Every test transparently gets a default /auth/config response
    // reporting SSO disabled so we don't have to duplicate the fixture
    // in every case. Explicit stubs override it because they match
    // first (findIndex before the default).
    const effective = [
        ...responses,
        {
            match: (u: string) => u.endsWith("/auth/config"),
            status: 200,
            body: {
                oidcEnabled: false,
                oidcButtonLabel: "",
                oidcChallengePath: "",
                analyseEnabled: true,
            },
        },
    ];
    const fetchMock = vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
        const asString =
            typeof url === "string"
                ? url
                : url instanceof URL
                    ? url.toString()
                    : url.url;
        const hit = effective.find((r) => r.match(asString, init));
        if (!hit) {
            throw new Error(`Unexpected fetch: ${asString}`);
        }
        const bodyText =
            typeof hit.body === "string" ? hit.body : JSON.stringify(hit.body);
        return new Response(bodyText, {
            status: hit.status,
            statusText: hit.status === 200 ? "OK" : "Unauthorized",
            headers: { "Content-Type": "application/json" },
        });
    });
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock as Mock;
}

describe("LoginRoute", () => {
    beforeEach(() => {
        void i18n.changeLanguage("en");
        useSessionStore.getState().clear();
        window.localStorage.clear();
    });

    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it("renders the form when not signed in", async () => {
        renderLogin();
        expect(
            await screen.findByRole("heading", { name: /sign in to nieweb/i }),
        ).toBeInTheDocument();
        expect(screen.getByPlaceholderText(/you@example\.com/)).toBeInTheDocument();
        expect(
            screen.getByPlaceholderText("Enter your password"),
        ).toBeInTheDocument();
        expect(screen.getByRole("button", { name: /sign in/i })).toBeInTheDocument();
    });

    it("shows validation errors when submitting an empty form", async () => {
        renderLogin();
        const user = userEvent.setup();
        await user.click(await screen.findByRole("button", { name: /sign in/i }));
        expect(await screen.findByText(/email is required/i)).toBeInTheDocument();
        expect(screen.getByText(/password is required/i)).toBeInTheDocument();
    });

    it("hydrates the session store on a successful sign-in", async () => {
        const fetchMock = stubFetch([
            {
                match: (u) => u.endsWith("/auth/login"),
                status: 200,
                body: {
                    accessToken: "jwt-token-abc",
                    tokenType: "Bearer",
                    expiresUtc: "2099-01-01T00:00:00Z",
                    mustRotatePassword: false,
                },
            },
            {
                match: (u) => u.endsWith("/auth/whoami"),
                status: 200,
                body: {
                    userId: "user-1",
                    email: "admin@nieweb.local",
                    name: "Administrator",
                    roles: ["Admin"],
                    mustRotatePassword: false,
                },
            },
        ]);
        renderLogin();
        const user = userEvent.setup();
        await user.type(
            await screen.findByPlaceholderText(/you@example\.com/),
            "admin@nieweb.local",
        );
        // PasswordInput renders both the input and a "Show password" toggle;
        // the placeholder is unambiguous.
        await user.type(
            screen.getByPlaceholderText("Enter your password"),
            "AdminPass123",
        );
        await user.click(screen.getByRole("button", { name: /sign in/i }));

        await waitFor(() => {
            const state = useSessionStore.getState();
            expect(state.token).toBe("jwt-token-abc");
            expect(state.user?.email).toBe("admin@nieweb.local");
            expect(state.user?.displayName).toBe("Administrator");
            expect(state.user?.roles).toEqual(["Admin"]);
        });

        // Three calls: /auth/config (rendered on mount), /auth/login,
        // then /auth/whoami with Bearer header. Only the last two are
        // ordered relative to each other; auth/config may fire before
        // or during the login mutation.
        expect(fetchMock).toHaveBeenCalledTimes(3);
        const nonConfigCalls = fetchMock.mock.calls.filter((c) => {
            const url = typeof c[0] === "string" ? c[0] : (c[0] as URL).toString();
            return !url.endsWith("/auth/config");
        });
        const whoamiCall = nonConfigCalls.find((c) => {
            const url = typeof c[0] === "string" ? c[0] : (c[0] as URL).toString();
            return url.endsWith("/auth/whoami");
        });
        expect(whoamiCall).toBeDefined();
        const init = whoamiCall![1] as RequestInit;
        const headers = new Headers(init.headers);
        expect(headers.get("Authorization")).toBe("Bearer jwt-token-abc");
    });

    it("surfaces an invalid-credentials alert on 401", async () => {
        stubFetch([
            {
                match: (u) => u.endsWith("/auth/login"),
                status: 401,
                body: { error: "invalid" },
            },
        ]);
        renderLogin();
        const user = userEvent.setup();
        await user.type(
            await screen.findByPlaceholderText(/you@example\.com/),
            "admin@nieweb.local",
        );
        await user.type(
            screen.getByPlaceholderText("Enter your password"),
            "wrong",
        );
        await user.click(screen.getByRole("button", { name: /sign in/i }));

        const alert = await screen.findByRole("alert");
        expect(alert).toHaveTextContent(/invalid email or password/i);
        expect(useSessionStore.getState().user).toBeNull();
        expect(useSessionStore.getState().token).toBeNull();
    });

    it("renders the signed-in state with a working sign-out button", async () => {
        useSessionStore.getState().setSession(
            {
                email: "admin@nieweb.local",
                displayName: "Administrator",
                roles: ["Admin"],
                mustRotatePassword: false,
            },
            "existing-token",
        );
        renderLogin();
        expect(
            await screen.findByText(/signed in as/i),
        ).toBeInTheDocument();
        const user = userEvent.setup();
        await user.click(screen.getByRole("button", { name: /sign out/i }));
        await waitFor(() => {
            expect(useSessionStore.getState().user).toBeNull();
            expect(useSessionStore.getState().token).toBeNull();
        });
    });

    it("navigates to the ?redirect target after a successful sign-in", async () => {
        stubFetch([
            {
                match: (u) => u.endsWith("/auth/login"),
                status: 200,
                body: {
                    accessToken: "jwt-token-xyz",
                    tokenType: "Bearer",
                    expiresUtc: "2099-01-01T00:00:00Z",
                    mustRotatePassword: false,
                },
            },
            {
                match: (u) => u.endsWith("/auth/whoami"),
                status: 200,
                body: {
                    userId: "user-2",
                    email: "reader@nieweb.local",
                    name: "Reader",
                    roles: ["Reader"],
                    mustRotatePassword: false,
                },
            },
        ]);
        renderLogin(
            "/login?redirect=%2Freport%2Fpanel-yield%3FsourceId%3Dpostreflow",
        );
        const user = userEvent.setup();
        await user.type(
            await screen.findByPlaceholderText(/you@example\.com/),
            "reader@nieweb.local",
        );
        await user.type(
            screen.getByPlaceholderText("Enter your password"),
            "ReaderPass123",
        );
        await user.click(screen.getByRole("button", { name: /sign in/i }));

        expect(await screen.findByTestId("report-stub")).toBeInTheDocument();
    });

    it("auto-redirects an already-signed-in visitor arriving with ?redirect=", async () => {
        useSessionStore.getState().setSession(
            {
                email: "reader@nieweb.local",
                displayName: "Reader",
                roles: ["Reader"],
                mustRotatePassword: false,
            },
            "existing-token",
        );
        renderLogin("/login?redirect=%2Freport%2Fpanel-yield");

        expect(await screen.findByTestId("report-stub")).toBeInTheDocument();
    });

    it("sends a forced-rotation user to /account/password after sign-in, ignoring any ?redirect", async () => {
        stubFetch([
            {
                match: (u) => u.endsWith("/auth/login"),
                status: 200,
                body: {
                    accessToken: "jwt-token-rot",
                    tokenType: "Bearer",
                    expiresUtc: "2099-01-01T00:00:00Z",
                    mustRotatePassword: true,
                },
            },
            {
                match: (u) => u.endsWith("/auth/whoami"),
                status: 200,
                body: {
                    userId: "user-rot",
                    email: "rotator@nieweb.local",
                    name: "Rotator",
                    roles: ["Reader"],
                    mustRotatePassword: true,
                },
            },
        ]);
        renderLogin("/login?redirect=%2Freport%2Fpanel-yield");
        const user = userEvent.setup();
        await user.type(
            await screen.findByPlaceholderText(/you@example\.com/),
            "rotator@nieweb.local",
        );
        await user.type(
            screen.getByPlaceholderText("Enter your password"),
            "TempPass123",
        );
        await user.click(screen.getByRole("button", { name: /sign in/i }));

        expect(
            await screen.findByTestId("change-password-stub"),
        ).toBeInTheDocument();
    });

    it("auto-redirects a signed-in forced-rotation user to /account/password", async () => {
        useSessionStore.getState().setSession(
            {
                email: "rotator@nieweb.local",
                displayName: "Rotator",
                roles: ["Reader"],
                mustRotatePassword: true,
            },
            "existing-token",
        );
        renderLogin("/login?redirect=%2Freport%2Fpanel-yield");

        expect(
            await screen.findByTestId("change-password-stub"),
        ).toBeInTheDocument();
    });

    it("renders the SSO button when /auth/config reports oidcEnabled", async () => {
        // Explicit stub for auth/config that OVERRIDES the default
        // disabled one. Since findIndex matches the first hit and our
        // explicit responses land before the default, an explicit
        // /auth/config here wins.
        stubFetch([
            {
                match: (u) => u.endsWith("/auth/config"),
                status: 200,
                body: {
                    oidcEnabled: true,
                    oidcButtonLabel: "Contoso SSO",
                    oidcChallengePath: "/auth/oidc/challenge",
                    analyseEnabled: true,
                },
            },
        ]);
        renderLogin("/login?redirect=%2Freport%2Fpanel-yield");

        const ssoButton = await screen.findByRole("link", {
            name: /contoso sso/i,
        });
        // Verify the href carries a /app-prefixed returnUrl (open-
        // redirect defence on the server insists on it).
        const href = ssoButton.getAttribute("href");
        expect(href).toContain("/auth/oidc/challenge?returnUrl=");
        expect(href).toContain(
            encodeURIComponent("/app/report/panel-yield"),
        );
    });

    it("hides the SSO button when /auth/config reports oidcEnabled=false", async () => {
        // Default stub already reports disabled.
        stubFetch([]);
        renderLogin();

        await screen.findByRole("button", { name: /sign in/i });
        // No SSO button rendered when disabled.
        expect(
            screen.queryByRole("link", { name: /sso|single sign-on/i }),
        ).toBeNull();
    });
});
