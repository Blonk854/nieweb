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
import { FpyTrendRoute } from "../routes/fpy-trend";
import { validateFpyTrendSearch } from "../routes/fpy-trend.search";
import { ParetoRoute } from "../routes/pareto";
import { validateParetoSearch } from "../routes/pareto.search";
import { SkipSummaryRoute } from "../routes/skip-summary";
import { validateSkipSummarySearch } from "../routes/skip-summary.search";
import { DpmoRoute } from "../routes/dpmo";
import { validateDpmoSearch } from "../routes/dpmo.search";
import { DpmoTrendRoute } from "../routes/dpmo-trend";
import { validateDpmoTrendSearch } from "../routes/dpmo-trend.search";
import { FpyRoute } from "../routes/fpy";
import { validateFpySearch } from "../routes/fpy.search";
import { CanvasDemoRoute } from "../routes/canvas-demo";
import { validateCanvasDemoSearch } from "../routes/canvas-demo.search";
import { TraceabilityBoardRoute } from "../routes/traceability-board";
import { validateTraceabilityBoardSearch } from "../routes/traceability-board.search";
import { LoginRoute } from "../routes/login";
import { validateLoginSearch } from "../routes/login.search";
import { OidcReturnRoute } from "../routes/oidc-return";
import { AdminUsersRoute } from "../routes/admin-users";
import { AdminAuditRoute } from "../routes/admin-audit";
import { AdminReportsRoute } from "../routes/admin-reports";
import { AdminReportEditorRoute, MyReportEditorRoute } from "../routes/admin-report-editor";
import { MyReportsRoute } from "../routes/my-reports";
import { OldSchoolReportsRoute } from "../routes/old-school-reports";
import { OldSchoolLayoutRoute } from "../routes/old-school-layout";
import { OldSchoolNewEntityRoute } from "../routes/old-school-new-entity";
import { OldSchoolEntityRoute } from "../routes/old-school-entity";
import { OldSchoolViewRoute } from "../routes/old-school-view";
import { AdminBoardSvgsRoute } from "../routes/admin-board-svgs";
import { AdminParametersRoute } from "../routes/admin-parameters";
import { AdminSkipClassificationRoute } from "../routes/admin-skip-classification";
import { AdminProductionLinesRoute } from "../routes/admin-production-lines";
import { AdminShiftsRoute } from "../routes/admin-shifts";
import { ChangePasswordRoute } from "../routes/change-password";
import { SettingsTimezoneRoute } from "../routes/settings-timezone";
import { SettingsDatabasesRoute } from "../routes/settings-databases";
import { AnalyseRoute } from "../routes/analyse";
import { AnalyseProductDetailRoute } from "../routes/analyse-product-detail";
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

const paretoRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/report/pareto",
    component: ParetoRoute,
    validateSearch: validateParetoSearch,
    // Same auth-gating story as panelYieldRoute.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const fpyTrendRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/report/fpy-trend",
    component: FpyTrendRoute,
    validateSearch: validateFpyTrendSearch,
    // Same auth-gating story as the other report routes.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const skipSummaryRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/report/skip-summary",
    component: SkipSummaryRoute,
    validateSearch: validateSkipSummarySearch,
    // Same auth-gating story as the other report routes.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const dpmoRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/report/dpmo",
    component: DpmoRoute,
    validateSearch: validateDpmoSearch,
    // Same auth-gating story as the other report routes.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const dpmoTrendRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/report/dpmo-trend",
    component: DpmoTrendRoute,
    validateSearch: validateDpmoTrendSearch,
    // Same auth-gating story as the other report routes.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const fpyRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/report/fpy",
    component: FpyRoute,
    validateSearch: validateFpySearch,
    // Same auth-gating story as the other report routes.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const canvasDemoRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/report/canvas-demo",
    component: CanvasDemoRoute,
    validateSearch: validateCanvasDemoSearch,
    // Same auth-gating story as the other report routes.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const traceabilityBoardRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/traceability/board",
    component: TraceabilityBoardRoute,
    validateSearch: validateTraceabilityBoardSearch,
    // TC3 board trace requires an authenticated user — the underlying
    // TC2 API is `RequireAuthorization()`.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const analyseRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/analyse",
    component: AnalyseRoute,
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const analyseProductDetailRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/analyse/product/$productId",
    component: AnalyseProductDetailRoute,
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

const adminAuditRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/admin/audit",
    component: AdminAuditRoute,
    // Same defence-in-depth story as adminUsersRoute: bounce anon
    // users to /login before the component mounts; the Admin role
    // check inside AdminAuditRoute renders a localised forbidden
    // panel for signed-in-but-not-admin callers.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const adminReportsRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/admin/reports",
    component: AdminReportsRoute,
    // Same defence-in-depth story as the other admin routes: the
    // component itself renders a localised forbidden alert for
    // signed-in-but-not-admin callers.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const adminReportEditorRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/admin/reports/$id",
    component: AdminReportEditorRoute,
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const adminBoardSvgsRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/admin/board-svgs",
    component: AdminBoardSvgsRoute,
    // Same defence-in-depth story as the other admin routes: bounce
    // anonymous visitors up front; the Admin role check lives in
    // the component so signed-in-but-not-admin users see a
    // localised forbidden panel.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const adminParametersRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/admin/parameters",
    component: AdminParametersRoute,
    // F13: same defence-in-depth story as the other admin routes.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const adminSkipClassificationRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/admin/skip-classification",
    component: AdminSkipClassificationRoute,
    // Same defence-in-depth story as the other admin routes.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const adminProductionLinesRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/admin/production-lines",
    component: AdminProductionLinesRoute,
    // F13: same defence-in-depth story as the other admin routes.
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const adminShiftsRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/admin/shifts",
    component: AdminShiftsRoute,
    // F13: same defence-in-depth story as the other admin routes.
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

// Purely browser-local preference: no API calls, no server state,
// no auth requirement. The Settings NavLink that surfaces this route
// is itself only shown to signed-in users (see RootLayout SideNav),
// but if someone bookmarks the URL and hits it while anonymous the
// page still works and their preference persists to localStorage.
const settingsTimezoneRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/settings/timezone",
    component: SettingsTimezoneRoute,
});

// Admin-only Databases screen (Phase C). The component renders a
// localised forbidden panel for signed-in-but-not-admin users, but
// bounce anonymous visitors up front for defence in depth.
const settingsDatabasesRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/settings/databases",
    component: SettingsDatabasesRoute,
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

// Self-service "My Reports" list for Author / Admin users (RC2). The
// Author-role check lives inside the component so signed-in non-authors
// see a localised forbidden panel rather than a silent bounce.
const myReportsRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/reports",
    component: MyReportsRoute,
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

// Author-facing editor for one of the caller's own reports. Reuses the
// shared report editor with the owner-scoped `/api/reports` adapter.
const myReportEditorRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/reports/$id",
    component: MyReportEditorRoute,
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

// Old-school (Vieweb-style) report designer. Author + Admin only; the
// role check lives inside each component. Routes are nested paths under
// /old-school/reports so the breadcrumb reads Reports list > ... .
const oldSchoolReportsRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/old-school/reports",
    component: OldSchoolReportsRoute,
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});
const oldSchoolLayoutRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/old-school/reports/$id",
    component: OldSchoolLayoutRoute,
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});
const oldSchoolNewEntityRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/old-school/reports/$id/new-entity",
    component: OldSchoolNewEntityRoute,
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});
const oldSchoolEntityRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/old-school/reports/$id/entity/$entityId",
    component: OldSchoolEntityRoute,
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});
const oldSchoolViewRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/old-school/reports/$id/view",
    component: OldSchoolViewRoute,
    beforeLoad: ({ location }) => requireAuthentication(location.href),
});

const routeTree = rootRoute.addChildren([
    homeRoute,
    panelYieldRoute,
    fpyTrendRoute,
    paretoRoute,
    skipSummaryRoute,
    dpmoRoute,
    dpmoTrendRoute,
    fpyRoute,
    canvasDemoRoute,
    traceabilityBoardRoute,
    analyseRoute,
    analyseProductDetailRoute,
    loginRoute,
    oidcReturnRoute,
    adminUsersRoute,
    adminAuditRoute,
    adminReportsRoute,
    adminReportEditorRoute,
    myReportsRoute,
    myReportEditorRoute,
    oldSchoolReportsRoute,
    oldSchoolLayoutRoute,
    oldSchoolNewEntityRoute,
    oldSchoolEntityRoute,
    oldSchoolViewRoute,
    adminBoardSvgsRoute,
    adminParametersRoute,
    adminSkipClassificationRoute,
    adminProductionLinesRoute,
    adminShiftsRoute,
    changePasswordRoute,
    settingsTimezoneRoute,
    settingsDatabasesRoute,
]);

// Re-export the typed route so components (../routes/panel-yield.tsx)
// can call `panelYieldRoute.useSearch()` and get the fully-typed
// PanelYieldSearch back.
export { panelYieldRoute };
export { paretoRoute };
export { skipSummaryRoute };
export { dpmoRoute };
export { fpyRoute };
export { canvasDemoRoute };
export { traceabilityBoardRoute };

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
