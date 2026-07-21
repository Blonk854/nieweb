import { Card, Stack, Text, Title } from "@mantine/core";
import { useSessionStore } from "../state/session";

/**
 * Sign-in placeholder. The real form (username/password against
 * /api/auth/token, plus a "Sign in with corporate account" OIDC
 * button) lands with the auth backlog item.
 */
export function LoginRoute() {
    const user = useSessionStore((s) => s.user);
    return (
        <Stack gap="lg">
            <Title order={2}>Sign in</Title>
            <Card withBorder padding="lg" radius="md">
                {user ? (
                    <Text>
                        Signed in as <strong>{user.displayName}</strong>{" "}
                        ({user.email}).
                    </Text>
                ) : (
                    <Text c="dimmed">
                        Sign-in form goes here. For now, a token can be
                        obtained via <Text component="code">POST /api/auth/token</Text>{" "}
                        and pushed into the Zustand session store from the
                        browser console (temporary dev affordance).
                    </Text>
                )}
            </Card>
        </Stack>
    );
}
