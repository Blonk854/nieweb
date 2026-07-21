import {
    createRootRoute,
    createRoute,
    createRouter,
} from "@tanstack/react-router";

import { RootLayout } from "./RootLayout";
import { HomeRoute } from "../routes/home";
import { PanelYieldRoute } from "../routes/panel-yield";
import { validatePanelYieldSearch } from "../routes/panel-yield.search";
import { LoginRoute } from "../routes/login";
import { AdminUsersRoute } from "../routes/admin-users";

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
});

const loginRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/login",
    component: LoginRoute,
});

const adminUsersRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/admin/users",
    component: AdminUsersRoute,
});

const routeTree = rootRoute.addChildren([
    homeRoute,
    panelYieldRoute,
    loginRoute,
    adminUsersRoute,
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
