import { Link, Outlet, useRouter } from "@tanstack/react-router";
import { TanStackRouterDevtools } from "@tanstack/react-router-devtools";
import {
    AppShell,
    Burger,
    Container,
    Group,
    NavLink,
    Select,
    Text,
    Title,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import { IconChartBar, IconHome, IconLogin } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";
import { SUPPORTED_LANGUAGES } from "../i18n";

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
            header={{ height: 56 }}
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
                        <Title order={3} style={{ margin: 0 }}>
                            {t("app.title")}
                        </Title>
                    </Group>
                    <Group gap="md">
                        <Text size="sm" c="dimmed">
                            {t("app.subtitle")}
                        </Text>
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
                to="/login"
                label={t("nav.signIn")}
                leftSection={<IconLogin size={18} />}
                active={active === "/login"}
            />
        </>
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
