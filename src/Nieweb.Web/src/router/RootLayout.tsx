import { Link, Outlet, useRouter } from "@tanstack/react-router";
import { TanStackRouterDevtools } from "@tanstack/react-router-devtools";
import {
    AppShell,
    Burger,
    Container,
    Group,
    NavLink,
    Text,
    Title,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import { IconChartBar, IconHome, IconLogin } from "@tabler/icons-react";

/**
 * Root layout: Mantine AppShell with a header + collapsible left navbar.
 * Every child route renders into <Outlet />. Real theming, dark-mode
 * toggle, and user menu land in later frontend items; F2 sets up the
 * shell only.
 */
export function RootLayout() {
    const [opened, { toggle }] = useDisclosure();
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
                            aria-label="Toggle navigation"
                        />
                        <Title order={3} style={{ margin: 0 }}>
                            Nieweb
                        </Title>
                    </Group>
                    <Text size="sm" c="dimmed">
                        Phase 1 MVP
                    </Text>
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
    return (
        <>
            <NavLink
                component={Link}
                to="/"
                label="Home"
                leftSection={<IconHome size={18} />}
                active={active === "/"}
            />
            <NavLink
                component={Link}
                to="/report/panel-yield"
                label="Panel Yield by Line"
                leftSection={<IconChartBar size={18} />}
                active={active.startsWith("/report/panel-yield")}
            />
            <NavLink
                component={Link}
                to="/login"
                label="Sign in"
                leftSection={<IconLogin size={18} />}
                active={active === "/login"}
            />
        </>
    );
}
