import { useEffect, useState } from "react";
import {
    Alert,
    Anchor,
    Button,
    Card,
    Divider,
    Group,
    PasswordInput,
    Stack,
    Text,
    TextInput,
    Title,
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { useMutation } from "@tanstack/react-query";
import { Link, useNavigate, useSearch } from "@tanstack/react-router";
import { IconAlertCircle, IconLogout } from "@tabler/icons-react";
import { Trans, useTranslation } from "react-i18next";

import { login, whoami } from "../api/auth";
import { ApiError } from "../api/client";
import { useSessionStore } from "../state/session";
import type { LoginSearch } from "./login.search";

type FormValues = {
    email: string;
    password: string;
};

/**
 * Sign-in route. Renders a Mantine form that posts credentials to
 * POST /auth/login, then calls GET /auth/whoami to hydrate the
 * session store, and finally navigates back to either the URL the
 * auth guard bounced the user from (via `?redirect=<path>`) or the
 * home page.
 *
 * If the user is already signed in the route shows their identity and
 * a sign-out button instead of the form — unless a redirect param is
 * present, in which case they are sent straight to their destination.
 */
export function LoginRoute() {
    const { t } = useTranslation();
    const user = useSessionStore((s) => s.user);
    const setSession = useSessionStore((s) => s.setSession);
    const clearSession = useSessionStore((s) => s.clear);
    const navigate = useNavigate();
    // `strict: false` mirrors the panel-yield route: the shape is
    // enforced at the router level by `validateLoginSearch`; the cast
    // keeps the component decoupled from the router registration.
    const rawSearch = useSearch({ strict: false });
    const { redirect: redirectTarget } = rawSearch as LoginSearch;
    const [errorKey, setErrorKey] = useState<
        "login.form.invalidCredentials" | "login.form.unexpectedError" | null
    >(null);

    // Signed in with a pending redirect? Send them there once the
    // route mounts. Guarded by the effect so the initial render is
    // still consistent (React can render this component up to twice
    // in strict mode; the redirect fires idempotently either way).
    //
    // If the user is flagged for forced password rotation, /login is
    // the wrong destination — send them straight to the change-
    // password screen instead.
    useEffect(() => {
        if (!user) {
            return;
        }
        if (user.mustRotatePassword) {
            void navigate({ to: "/account/password" });
            return;
        }
        if (redirectTarget) {
            void navigate({ to: redirectTarget });
        }
    }, [user, redirectTarget, navigate]);

    const form = useForm<FormValues>({
        mode: "controlled",
        initialValues: { email: "", password: "" },
        validate: {
            email: (value) => {
                if (!value.trim()) {
                    return t("login.form.emailRequired");
                }
                // Deliberately-permissive check; the server does the
                // authoritative validation via [EmailAddress].
                if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim())) {
                    return t("login.form.emailInvalid");
                }
                return null;
            },
            password: (value) =>
                value.length === 0 ? t("login.form.passwordRequired") : null,
        },
    });

    const mutation = useMutation({
        mutationFn: async (values: FormValues) => {
            const tokenResponse = await login({
                email: values.email.trim(),
                password: values.password,
            });
            // apiFetch reads the current token from the store, so seed
            // it *before* calling whoami. If whoami fails we clear the
            // half-populated state below.
            setSession(
                {
                    email: values.email.trim(),
                    displayName: values.email.trim(),
                    roles: [],
                    mustRotatePassword: tokenResponse.mustRotatePassword,
                },
                tokenResponse.accessToken,
            );
            const me = await whoami();
            setSession(
                {
                    email: me.email ?? values.email.trim(),
                    displayName: me.name ?? me.email ?? values.email.trim(),
                    roles: me.roles,
                    mustRotatePassword: me.mustRotatePassword,
                },
                tokenResponse.accessToken,
            );
            return me;
        },
        onSuccess: (me) => {
            setErrorKey(null);
            // Forced-rotation accounts skip the redirect target and
            // go straight to the change-password screen. The router
            // guard will keep bouncing them back here until the flag
            // is cleared.
            if (me.mustRotatePassword) {
                void navigate({ to: "/account/password" });
                return;
            }
            void navigate({ to: redirectTarget ?? "/" });
        },
        onError: (error) => {
            clearSession();
            if (error instanceof ApiError && error.status === 401) {
                setErrorKey("login.form.invalidCredentials");
                return;
            }
            setErrorKey("login.form.unexpectedError");
        },
    });

    if (user) {
        return (
            <Stack gap="lg">
                <Title order={2}>{t("login.title")}</Title>
                <Card withBorder padding="lg" radius="md">
                    <Stack gap="md">
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
                        <Group>
                            <Button
                                variant="default"
                                leftSection={<IconLogout size={16} />}
                                onClick={() => {
                                    clearSession();
                                    void navigate({ to: "/login" });
                                }}
                            >
                                {t("login.signOut")}
                            </Button>
                            <Anchor component={Link} to="/">
                                {t("nav.home")}
                            </Anchor>
                        </Group>
                    </Stack>
                </Card>
            </Stack>
        );
    }

    return (
        <Stack gap="lg" maw={480}>
            <Title order={2}>{t("login.signInHeading")}</Title>
            <Card withBorder padding="lg" radius="md">
                <form
                    onSubmit={form.onSubmit((values) => mutation.mutate(values))}
                    noValidate
                >
                    <Stack gap="md">
                        {errorKey && (
                            <Alert
                                color="red"
                                icon={<IconAlertCircle size={18} />}
                                role="alert"
                            >
                                {t(errorKey)}
                            </Alert>
                        )}
                        <TextInput
                            label={t("login.form.emailLabel")}
                            placeholder={t("login.form.emailPlaceholder")}
                            type="email"
                            autoComplete="username"
                            required
                            {...form.getInputProps("email")}
                        />
                        <PasswordInput
                            label={t("login.form.passwordLabel")}
                            placeholder={t("login.form.passwordPlaceholder")}
                            autoComplete="current-password"
                            required
                            {...form.getInputProps("password")}
                        />
                        <Divider my="xs" />
                        <Group justify="flex-end">
                            <Button type="submit" loading={mutation.isPending}>
                                {mutation.isPending
                                    ? t("login.form.signingIn")
                                    : t("login.form.submit")}
                            </Button>
                        </Group>
                    </Stack>
                </form>
            </Card>
        </Stack>
    );
}
