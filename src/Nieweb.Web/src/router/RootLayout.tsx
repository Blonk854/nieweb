import { Link, Outlet, useNavigate, useRouter } from "@tanstack/react-router";
import { TanStackRouterDevtools } from "@tanstack/react-router-devtools";
import {
    AppShell,
    Box,
    Burger,
    Container,
    Group,
    NavLink,
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
                <SideNav />
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
    const router = useRouter();
    const active = router.state.location.pathname;
    const { t } = useTranslation();
    const user = useSessionStore((s) => s.user);
    const isAdmin = user?.roles.includes("Admin") ?? false;
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
                to="/report/panel-yield"
                label={t("nav.panelYield")}
                leftSection={<IconChartBar size={18} />}
                active={active.startsWith("/report/panel-yield")}
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
 * Header brand cluster: the primary Nieweb wordmark stacked over the
 * BSS Green Premium sub-brand, wrapped in a link to the home page.
 * The two SVGs are shipped from `public/logo/` so they are cache-
 * friendly and can be swapped without a rebuild. The `aria-label`
 * satisfies a11y since the visual mark carries all the meaning.
 */
function BrandMark() {
    const { t } = useTranslation();
    return (
        <Link
            to="/"
            aria-label={t("app.title")}
            style={{
                display: "inline-flex",
                flexDirection: "column",
                alignItems: "flex-start",
                gap: 2,
                textDecoration: "none",
            }}
        >
            <img
                src="/app/logo/logo.svg"
                alt=""
                height={28}
                style={{ display: "block", width: "auto" }}
            />
            <img
                src="/app/logo/bss_green_premium_no_pod.svg"
                alt=""
                height={12}
                style={{ display: "block", width: "auto", opacity: 0.85 }}
            />
        </Link>
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
