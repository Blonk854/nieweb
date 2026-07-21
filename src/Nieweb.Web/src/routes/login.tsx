import { Card, Stack, Text, Title } from "@mantine/core";
import { Trans, useTranslation } from "react-i18next";
import { useSessionStore } from "../state/session";

/**
 * Sign-in placeholder. The real form (username/password against
 * /api/auth/token, plus a "Sign in with corporate account" OIDC
 * button) lands with the auth backlog item.
 */
export function LoginRoute() {
    const { t } = useTranslation();
    const user = useSessionStore((s) => s.user);
    return (
        <Stack gap="lg">
            <Title order={2}>{t("login.title")}</Title>
            <Card withBorder padding="lg" radius="md">
                {user ? (
                    <Text>
                        <Trans
                            i18nKey="login.signedInAs"
                            values={{
                                displayName: user.displayName,
                                email: user.email,
                            }}
                            components={{ 1: <strong /> }}
                        />
                    </Text>
                ) : (
                    <Text c="dimmed">
                        <Trans
                            i18nKey="login.placeholderBody"
                            components={{ 1: <Text component="code" /> }}
                        />
                    </Text>
                )}
            </Card>
        </Stack>
    );
}
