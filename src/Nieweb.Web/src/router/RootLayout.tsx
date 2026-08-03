import { Link, Outlet, useNavigate, useRouterState } from "@tanstack/react-router";
import { TanStackRouterDevtools } from "@tanstack/react-router-devtools";
import {
    AppShell,
    Box,
    Burger,
    Container,
    Group,
    NavLink,
    ScrollArea,
    Select,
    Text,
    UnstyledButton,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import {
    IconAdjustments,
    IconChartBar,
    IconClipboardList,
    IconClock,
    IconDatabase,
    IconHome,
    IconKey,
    IconLogin,
    IconLogout,
    IconBarcode,
    IconPhoto,
    IconRoute,
    IconSettings,
    IconUsers,
} from "@tabler/icons-react";
import { useTranslation } from "react-i18next";
import { SUPPORTED_LANGUAGES } from "../i18n";
import { useSessionStore } from "../state/session";

/**
 * Root layout: Mantine AppShell with a header + collapsible left navbar.
 * Every child route renders into <Outlet />. F2 sets up the shell and
 * F3 adds the language switcher + i18n-keyed strings. Real theming,
 * dark-mode toggle, and user menu land in later items.
 */
export function RootLayout() {
    const [opened, { toggle }] = useDisclosure();
    const { t } = useTranslation();
    return (
        <AppShell
            header={{ height: 64 }}
            navbar={{
                width: 220,
                breakpoint: "sm",
                collapsed: { mobile: !opened },
            }}
            padding="md"
        >
            <AppShell.Header>
                <Group h="100%" px="md" justify="space-between">
                    <Group gap="sm">
                        <Burger
                            opened={opened}
                            onClick={toggle}
                            hiddenFrom="sm"
                            size="sm"
                            aria-label={t("app.toggleNavigation")}
                        />
                        <BrandMark />
                    </Group>
                    <Group gap="md">
                        <Text size="sm" c="dimmed" visibleFrom="sm">
                            {t("app.subtitle")}
                        </Text>
                        <SessionIndicator />
                        <LanguageSwitcher />
                    </Group>
                </Group>
            </AppShell.Header>

            <AppShell.Navbar p="sm">
                <AppShell.Section grow component={ScrollArea}>
                    <SideNav />
                </AppShell.Section>
                <AppShell.Section>
                    <BrandFooter />
                </AppShell.Section>
            </AppShell.Navbar>

            <AppShell.Main>
                <Container size="lg">
                    <Outlet />
                </Container>
            </AppShell.Main>

            {import.meta.env.DEV && (
                <TanStackRouterDevtools position="bottom-right" />
            )}
        </AppShell>
    );
}

function SideNav() {
    // Subscribe to the location so the nav re-renders on every
    // navigation. Reading router.state imperatively (via useRouter) is
    // NOT reactive, which left the first-selected item locked as active
    // while later selections also lit up.
    const active = useRouterState({ select: (s) => s.location.pathname });
    const { t } = useTranslation();
    const user = useSessionStore((s) => s.user);
    const isAdmin = user?.roles.includes("Admin") ?? false;
    const canAuthor =
        (user?.roles.includes("Author") || user?.roles.includes("Admin")) ?? false;
    // The Settings parent groups the low-frequency admin + account
    // pages so the top-level nav stays focused on reports. It renders
    // whenever *any* child would render (an admin sees all 6 admin
    // items + change-password; a non-admin sees only change-password).
    // Auto-expanded when the current URL matches a child so a deep
    // link lands with the correct branch open.
    const settingsActive =
        active.startsWith("/admin/") ||
        active.startsWith("/account/") ||
        active.startsWith("/settings/");
    const showSettings = isAdmin || Boolean(user);
    return (
        <>
            <NavLink
                component={Link}
                to="/"
                label={t("nav.home")}
                leftSection={<IconHome size={18} />}
                active={active === "/"}
            />
            <NavLink
                component={Link}
                to="/report/fpy-trend"
                label={t("nav.fpyTrend")}
                leftSection={<IconChartBar size={18} />}
                active={active.startsWith("/report/fpy-trend")}
            />
            <NavLink
                component={Link}
                to="/report/pareto"
                label={t("nav.pareto")}
                leftSection={<IconChartBar size={18} />}
                active={active.startsWith("/report/pareto")}
            />
            <NavLink
                component={Link}
                to="/report/dpmo"
                label={t("nav.dpmo")}
                leftSection={<IconChartBar size={18} />}
                // Exact-match, NOT startsWith("/report/dpmo"): that prefix also
                // matches "/report/dpmo-trend", which would light up both nav
                // items at once. Same shape as the /report/fpy vs
                // /report/fpy-trend pair below.
                active={
                    active === "/report/dpmo" || active.startsWith("/report/dpmo/")
                }
            />
            <NavLink
                component={Link}
                to="/report/dpmo-trend"
                label={t("nav.dpmoTrend")}
                leftSection={<IconChartBar size={18} />}
                active={active.startsWith("/report/dpmo-trend")}
            />
            <NavLink
                component={Link}
                to="/report/fpy"
                label={t("nav.fpy")}
                leftSection={<IconChartBar size={18} />}
                active={
                    active === "/report/fpy" || active.startsWith("/report/fpy/")
                }
            />
            <NavLink
                component={Link}
                to="/report/skip-summary"
                label={t("nav.skipSummary")}
                leftSection={<IconChartBar size={18} />}
                active={active.startsWith("/report/skip-summary")}
            />
            <NavLink
                component={Link}
                to="/report/canvas-demo"
                label={t("nav.canvasDemo")}
                leftSection={<IconChartBar size={18} />}
                active={active.startsWith("/report/canvas-demo")}
            />
            <NavLink
                component={Link}
                to="/traceability/board"
                label={t("nav.boardTrace")}
                leftSection={<IconBarcode size={18} />}
                active={active.startsWith("/traceability/board")}
            />
            {canAuthor && (
                <NavLink
                    component={Link}
                    to="/reports"
                    label={t("nav.myReports")}
                    leftSection={<IconChartBar size={18} />}
                    active={active === "/reports" || active.startsWith("/reports/")}
                />
            )}
            {canAuthor && (
                <NavLink
                    component={Link}
                    to="/old-school/reports"
                    label={t("nav.oldSchool")}
                    leftSection={<IconChartBar size={18} />}
                    active={active.startsWith("/old-school")}
                />
            )}
            {isAdmin && (
                <NavLink
                    component={Link}
                    to="/admin/reports"
                    label={t("nav.adminReports")}
                    leftSection={<IconChartBar size={18} />}
                    active={active.startsWith("/admin/reports")}
                />
            )}
            {showSettings && (
                <Box data-testid="nav-settings-branch">
                    <NavLink
                        label={t("nav.settings")}
                        leftSection={<IconSettings size={18} />}
                        childrenOffset={28}
                        defaultOpened={settingsActive}
                        data-testid="nav-settings"
                    >
                    {isAdmin && (
                        <NavLink
                            component={Link}
                            to="/admin/users"
                            label={t("nav.adminUsers")}
                            leftSection={<IconUsers size={18} />}
                            active={active.startsWith("/admin/users")}
                        />
                    )}
                    {isAdmin && (
                        <NavLink
                            component={Link}
                            to="/admin/audit"
                            label={t("nav.adminAudit")}
                            leftSection={<IconClipboardList size={18} />}
                            active={active.startsWith("/admin/audit")}
                        />
                    )}
                    {isAdmin && (
                        <NavLink
                            component={Link}
                            to="/admin/board-svgs"
                            label={t("nav.adminBoardSvgs")}
                            leftSection={<IconPhoto size={18} />}
                            active={active.startsWith("/admin/board-svgs")}
                        />
                    )}
                    {isAdmin && (
                        <NavLink
                            component={Link}
                            to="/admin/production-lines"
                            label={t("nav.adminProductionLines")}
                            leftSection={<IconRoute size={18} />}
                            active={active.startsWith(
                                "/admin/production-lines",
                            )}
                        />
                    )}
                    {isAdmin && (
                        <NavLink
                            component={Link}
                            to="/admin/shifts"
                            label={t("nav.adminShifts")}
                            leftSection={<IconClock size={18} />}
                            active={active.startsWith("/admin/shifts")}
                        />
                    )}
                    {isAdmin && (
                        <NavLink
                            component={Link}
                            to="/admin/parameters"
                            label={t("nav.adminParameters")}
                            leftSection={<IconAdjustments size={18} />}
                            active={active.startsWith("/admin/parameters")}
                        />
                    )}
                    {isAdmin && (
                        <NavLink
                            component={Link}
                            to="/admin/skip-classification"
                            label={t("nav.adminSkipClassification")}
                            leftSection={<IconAdjustments size={18} />}
                            active={active.startsWith("/admin/skip-classification")}
                        />
                    )}
                    {user && (
                        <NavLink
                            component={Link}
                            to="/settings/timezone"
                            label={t("nav.settingsTimezone")}
                            leftSection={<IconClock size={18} />}
                            active={active.startsWith("/settings/timezone")}
                        />
                    )}
                    {isAdmin && (
                        <NavLink
                            component={Link}
                            to="/settings/databases"
                            label={t("nav.settingsDatabases")}
                            leftSection={<IconDatabase size={18} />}
                            active={active.startsWith("/settings/databases")}
                        />
                    )}
                    {user && (
                        <NavLink
                            component={Link}
                            to="/account/password"
                            label={t("nav.changePassword")}
                            leftSection={<IconKey size={18} />}
                            active={active.startsWith("/account/password")}
                        />
                    )}
                </NavLink>
                </Box>
            )}
            <NavLink
                component={Link}
                to="/login"
                label={user ? t("nav.signOut") : t("nav.signIn")}
                leftSection={
                    user ? <IconLogout size={18} /> : <IconLogin size={18} />
                }
                active={active === "/login"}
            />
        </>
    );
}

/**
 * Header brand cluster: the primary Nieweb wordmark, wrapped in a link
 * to the home page. The BigSoy Studios sub-brand now lives in the
 * navbar footer (see {@link BrandFooter}). The SVG is shipped from
 * `public/logo/` so it is cache-friendly and can be swapped without a
 * rebuild. The `aria-label` satisfies a11y since the visual mark
 * carries all the meaning.
 */
function BrandMark() {
    const { t } = useTranslation();
    return (
        <Link
            to="/"
            aria-label={t("app.title")}
            style={{
                display: "inline-flex",
                alignItems: "center",
                textDecoration: "none",
            }}
        >
            <img
                src="/app/logo/logo.svg"
                alt=""
                height={44}
                style={{ display: "block", width: "auto" }}
            />
        </Link>
    );
}

/**
 * Navbar footer brand: the BigSoy Studios (BSS Green Premium) sub-brand,
 * pinned to the bottom of the left navbar beneath the scrolling nav
 * links. Sized to the navbar width with a thin separator above it.
 */
function BrandFooter() {
    return (
        <Box
            style={{
                borderTop: "1px solid var(--mantine-color-default-border)",
                paddingTop: "var(--mantine-spacing-xs)",
                display: "flex",
                justifyContent: "center",
            }}
        >
            <img
                src="/app/logo/bss_green_.svg"
                alt="BigSoy Studios"
                style={{
                    display: "block",
                    height: 48,
                    width: "auto",
                    maxWidth: "100%",
                    opacity: 0.85,
                }}
            />
        </Box>
    );
}

function SessionIndicator() {
    const user = useSessionStore((s) => s.user);
    const clearSession = useSessionStore((s) => s.clear);
    const navigate = useNavigate();
    const { t } = useTranslation();
    if (!user) {
        return null;
    }
    return (
        <Group gap="xs">
            <Text size="sm" fw={500} visibleFrom="sm">
                {user.displayName}
            </Text>
            <UnstyledButton
                onClick={() => {
                    clearSession();
                    void navigate({ to: "/login" });
                }}
                aria-label={t("login.signOut")}
                title={t("login.signOut")}
            >
                <Group gap={4} c="dimmed">
                    <IconLogout size={16} />
                    <Text size="sm" visibleFrom="sm">
                        {t("login.signOut")}
                    </Text>
                </Group>
            </UnstyledButton>
        </Group>
    );
}

const LANGUAGE_LABELS: Record<(typeof SUPPORTED_LANGUAGES)[number], string> = {
    en: "English",
    fr: "Français",
};

function LanguageSwitcher() {
    const { t, i18n } = useTranslation();
    const current =
        (SUPPORTED_LANGUAGES as readonly string[]).find(
            (l) => l === i18n.resolvedLanguage,
        ) ?? "en";
    return (
        <Select
            size="xs"
            aria-label={t("app.language")}
            data={SUPPORTED_LANGUAGES.map((l) => ({
                value: l,
                label: LANGUAGE_LABELS[l],
            }))}
            value={current}
            onChange={(value) => {
                if (value) {
                    void i18n.changeLanguage(value);
                }
            }}
            allowDeselect={false}
            checkIconPosition="right"
            w={120}
        />
    );
}
