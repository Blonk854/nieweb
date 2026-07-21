import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Mock } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { userEvent } from "@testing-library/user-event";
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
import { ChangePasswordRoute } from "./change-password";
import { useSessionStore } from "../state/session";

/**
 * Component-level tests for the /account/password screen. Covers
 * client-side validation (required fields, mismatched confirmation,
 * same-as-current), happy-path rotation (POST /auth/change-password
 * then GET /auth/whoami with the flag cleared), and server-side
 * error surfacing (wrong current password, Identity policy failures).
 */

type FetchResponse = {
    match: (url: string, init?: RequestInit) => boolean;
    status: number;
    body?: unknown;
};

function stubFetch(responses: FetchResponse[]): Mock {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url =
            typeof input === "string"
                ? input
                : input instanceof URL
                    ? input.toString()
                    : input.url;
        const hit = responses.find((r) => r.match(url, init));
        if (!hit) {
            throw new Error(`Unexpected fetch: ${init?.method ?? "GET"} ${url}`);
        }
        const status = hit.status;
        const statusText =
            status === 200
                ? "OK"
                : status === 204
                    ? "No Content"
                    : status === 400
                        ? "Bad Request"
                        : status === 401
                            ? "Unauthorized"
                            : "Error";
        const bodyText =
            hit.body === undefined
                ? ""
                : typeof hit.body === "string"
                    ? hit.body
                    : JSON.stringify(hit.body);
        return new Response(status === 204 ? null : bodyText, {
            status,
            statusText,
            headers: { "Content-Type": "application/json" },
        });
    });
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock as Mock;
}

function renderRoute() {
    const rootRoute = createRootRoute({ component: Outlet });
    const change = createRoute({
        getParentRoute: () => rootRoute,
        path: "/account/password",
        component: ChangePasswordRoute,
    });
    // A home stub so the "Continue to Home" link has somewhere to land.
    const home = createRoute({
        getParentRoute: () => rootRoute,
        path: "/",
        component: () => <h1 data-testid="home-stub">Home</h1>,
    });
    const routeTree = rootRoute.addChildren([change, home]);
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: ["/account/password"] }),
    });
    const client = new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
        },
    });
    return render(
        <MantineProvider>
            <QueryClientProvider client={client}>
                <RouterProvider router={router} />
            </QueryClientProvider>
        </MantineProvider>,
    );
}

function signIn(mustRotate: boolean) {
    useSessionStore.getState().setSession(
        {
            email: "user@nieweb.test",
            displayName: "User",
            roles: ["Reader"],
            mustRotatePassword: mustRotate,
        },
        "existing-token",
    );
}

describe("ChangePasswordRoute", () => {
    beforeEach(async () => {
        await i18n.changeLanguage("en");
        useSessionStore.getState().clear();
        window.localStorage.clear();
    });

    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it("shows the forced-rotation banner when the flag is set", async () => {
        signIn(true);
        renderRoute();
        expect(
            await screen.findByText(
                /you must set a new password before you can continue/i,
            ),
        ).toBeInTheDocument();
        // No cancel link for forced-rotation users.
        expect(screen.queryByText(/^cancel$/i)).not.toBeInTheDocument();
    });

    it("shows a cancel link when the user is not forced to rotate", async () => {
        signIn(false);
        renderRoute();
        expect(
            await screen.findByRole("link", { name: /cancel/i }),
        ).toBeInTheDocument();
    });

    it("validates that all three fields are required", async () => {
        signIn(false);
        renderRoute();
        const user = userEvent.setup();
        await user.click(
            await screen.findByRole("button", { name: /change password/i }),
        );
        expect(
            await screen.findByText(/current password is required/i),
        ).toBeInTheDocument();
        expect(
            screen.getByText(/new password is required/i),
        ).toBeInTheDocument();
        expect(
            screen.getByText(/please confirm the new password/i),
        ).toBeInTheDocument();
    });

    it("rejects a new password that matches the current one", async () => {
        signIn(false);
        renderRoute();
        const user = userEvent.setup();
        await user.type(
            await screen.findByPlaceholderText(/enter your current password/i),
            "sameOne123",
        );
        await user.type(
            screen.getByPlaceholderText(/enter a new password/i),
            "sameOne123",
        );
        await user.type(
            screen.getByPlaceholderText(/re-enter the new password/i),
            "sameOne123",
        );
        await user.click(
            screen.getByRole("button", { name: /change password/i }),
        );
        expect(
            await screen.findByText(
                /new password must be different from the current one/i,
            ),
        ).toBeInTheDocument();
    });

    it("rejects a mismatched confirmation", async () => {
        signIn(false);
        renderRoute();
        const user = userEvent.setup();
        await user.type(
            await screen.findByPlaceholderText(/enter your current password/i),
            "oldPass123",
        );
        await user.type(
            screen.getByPlaceholderText(/enter a new password/i),
            "brandnewpass456",
        );
        await user.type(
            screen.getByPlaceholderText(/re-enter the new password/i),
            "typo-here-789",
        );
        await user.click(
            screen.getByRole("button", { name: /change password/i }),
        );
        expect(
            await screen.findByText(/two new passwords do not match/i),
        ).toBeInTheDocument();
    });

    it("posts the change, clears the rotation flag, and shows the success screen", async () => {
        signIn(true);
        const fetchMock = stubFetch([
            {
                match: (u, init) =>
                    u.endsWith("/auth/change-password") && init?.method === "POST",
                status: 204,
            },
            {
                match: (u) => u.endsWith("/auth/whoami"),
                status: 200,
                body: {
                    userId: "user-1",
                    email: "user@nieweb.test",
                    name: "User",
                    roles: ["Reader"],
                    mustRotatePassword: false,
                },
            },
        ]);
        renderRoute();
        const user = userEvent.setup();
        await user.type(
            await screen.findByPlaceholderText(/enter your current password/i),
            "oldPass123",
        );
        await user.type(
            screen.getByPlaceholderText(/enter a new password/i),
            "brandnewpass456",
        );
        await user.type(
            screen.getByPlaceholderText(/re-enter the new password/i),
            "brandnewpass456",
        );
        await user.click(
            screen.getByRole("button", { name: /change password/i }),
        );

        // Success view.
        expect(
            await screen.findByText(/your password has been updated/i),
        ).toBeInTheDocument();

        // Local session flag now reflects the cleared state.
        await waitFor(() => {
            expect(
                useSessionStore.getState().user?.mustRotatePassword,
            ).toBe(false);
        });

        // Both /auth/change-password and /auth/whoami were called.
        expect(fetchMock).toHaveBeenCalledTimes(2);
        const changeCall = fetchMock.mock.calls[0];
        const init = changeCall[1] as RequestInit;
        expect(init.method).toBe("POST");
        const payload = JSON.parse(init.body as string) as {
            currentPassword: string;
            newPassword: string;
        };
        expect(payload.currentPassword).toBe("oldPass123");
        expect(payload.newPassword).toBe("brandnewpass456");
    });

    it("surfaces the PasswordMismatch code as 'wrong current password'", async () => {
        signIn(false);
        stubFetch([
            {
                match: (u) => u.endsWith("/auth/change-password"),
                status: 400,
                body: {
                    type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                    title: "One or more validation errors occurred.",
                    status: 400,
                    errors: {
                        PasswordMismatch: [
                            "Incorrect password.",
                        ],
                    },
                },
            },
        ]);
        renderRoute();
        const user = userEvent.setup();
        await user.type(
            await screen.findByPlaceholderText(/enter your current password/i),
            "wrongOne",
        );
        await user.type(
            screen.getByPlaceholderText(/enter a new password/i),
            "brandnewpass456",
        );
        await user.type(
            screen.getByPlaceholderText(/re-enter the new password/i),
            "brandnewpass456",
        );
        await user.click(
            screen.getByRole("button", { name: /change password/i }),
        );

        const alert = await screen.findByRole("alert");
        expect(alert).toHaveTextContent(/current password is incorrect/i);
    });

    it("shows the server's validation detail lines on other 400 codes", async () => {
        signIn(false);
        stubFetch([
            {
                match: (u) => u.endsWith("/auth/change-password"),
                status: 400,
                body: {
                    status: 400,
                    errors: {
                        PasswordTooShort: [
                            "Passwords must be at least 8 characters.",
                        ],
                    },
                },
            },
        ]);
        renderRoute();
        const user = userEvent.setup();
        await user.type(
            await screen.findByPlaceholderText(/enter your current password/i),
            "oldPass123",
        );
        await user.type(
            screen.getByPlaceholderText(/enter a new password/i),
            "shortie",
        );
        await user.type(
            screen.getByPlaceholderText(/re-enter the new password/i),
            "shortie",
        );
        await user.click(
            screen.getByRole("button", { name: /change password/i }),
        );

        const alert = await screen.findByRole("alert");
        expect(alert).toHaveTextContent(/server rejected the new password/i);
        expect(alert).toHaveTextContent(/at least 8 characters/i);
    });
});
