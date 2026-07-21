import {
    createRootRoute,
    createRoute,
    createRouter,
} from "@tanstack/react-router";

import { RootLayout } from "./RootLayout";
import { requireAuthentication } from "./guards";
import { HomeRoute } from "../routes/home";
import { PanelYieldRoute } from "../routes/panel-yield";
import { validatePanelYieldSearch } from "../routes/panel-yield.search";
import { LoginRoute } from "../routes/login";
import { validateLoginSearch } from "../routes/login.search";
import { OidcReturnRoute } from "../routes/oidc-return";
import { AdminUsersRoute } from "../routes/admin-users";
import { ChangePasswordRoute } from "../routes/change-password";
import { useSessionStore } from "../state/session";
import { redirect } from "@tanstack/react-router";

const rootRoute = createRootRoute({ component: RootLayout });

const homeRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/",
    component: HomeRoute,
});

const panelYieldRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/report/panel-yield",
    component: PanelYieldRoute,
    validateSearch: validatePanelYieldSearch,
    // Reports are gated behind authentication; the API returns 401 for
    // anonymous callers, but bouncing to /login *before* the query
    // fires avoids a flash of an empty report page.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const loginRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/login",
    component: LoginRoute,
    validateSearch: validateLoginSearch,
});

// Landing page for the OIDC redirect handshake. The URL fragment
// carries the JWT and returnUrl; the route parses them, hydrates the
// session store, and bounces onward. Anonymous by design — the
// fragment IS the credential.
const oidcReturnRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/oidc-return",
    component: OidcReturnRoute,
});

const adminUsersRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/admin/users",
    component: AdminUsersRoute,
    // Authentication is enforced up-front here too; the Admin role
    // check lives inside AdminUsersRoute so we can render a localised
    // forbidden panel for signed-in-but-not-admin users instead of
    // silently bouncing them.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const changePasswordRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/account/password",
    component: ChangePasswordRoute,
    // The change-password screen needs a signed-in user (there is no
    // way to change a password anonymously in Nieweb — password
    // resets go through the admin), so bounce anonymous visitors to
    // the sign-in page. We intentionally do NOT reuse
    // `requireAuthentication` here because that helper would then
    // re-bounce a forced-rotation user straight back to this route
    // and cause a redirect loop.
    beforeLoad: ({ location }) => {
        const user = useSessionStore.getState().user;
        if (!user) {
            throw redirect({
                to: "/login",
                search: { redirect: location.href },
            });
        }
    },
});

const routeTree = rootRoute.addChildren([
    homeRoute,
    panelYieldRoute,
    loginRoute,
    oidcReturnRoute,
    adminUsersRoute,
    changePasswordRoute,
]);

// Re-export the typed route so components (../routes/panel-yield.tsx)
// can call `panelYieldRoute.useSearch()` and get the fully-typed
// PanelYieldSearch back.
export { panelYieldRoute };

export const router = createRouter({
    routeTree,
    defaultPreload: "intent",
    // The SPA is served from /app on the API host; TanStack Router uses
    // the browser History API and needs to know the base path so that
    // `<Link to="/">` produces `/app/` in production.
    basepath: import.meta.env.BASE_URL.replace(/\/$/, "") || "/",
});

// TanStack Router requires this augmentation for typed `Link`s and
// `useRouter()` returns.
declare module "@tanstack/react-router" {
    interface Register {
        router: typeof router;
    }
}
