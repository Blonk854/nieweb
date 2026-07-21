import { useState } from "react";
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
    Title,
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { useMutation } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { IconAlertCircle, IconCircleCheck } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import { changePassword, whoami } from "../api/auth";
import { ApiError } from "../api/client";
import { useSessionStore } from "../state/session";

type FormValues = {
    currentPassword: string;
    newPassword: string;
    confirmPassword: string;
};

type ServerErrorInfo = {
    messageKey:
        | "account.changePassword.wrongCurrentPassword"
        | "account.changePassword.validationFailed"
        | "account.changePassword.unexpectedError";
    /** Extra lines pulled out of a ValidationProblem body. */
    detail?: string;
};

function parseServerError(error: unknown): ServerErrorInfo {
    if (!(error instanceof ApiError)) {
        return { messageKey: "account.changePassword.unexpectedError" };
    }
    if (error.status === 401) {
        // The API returns 401 only when the session is invalid mid-
        // request (the account got disabled). The apiFetch wrapper
        // already cleared the session, so the router guard will
        // bounce the user to /login on the next tick.
        return { messageKey: "account.changePassword.unexpectedError" };
    }
    if (error.status === 400 && error.body) {
        try {
            const parsed = JSON.parse(error.body) as {
                errors?: Record<string, string[] | undefined>;
            };
            if (parsed.errors) {
                // Identity uses code "PasswordMismatch" when the current
                // password is wrong; surface that as its own message so
                // the user knows to fix the *current* field.
                if (Object.keys(parsed.errors).some((c) => c === "PasswordMismatch")) {
                    return {
                        messageKey: "account.changePassword.wrongCurrentPassword",
                    };
                }
                const lines = Object.values(parsed.errors)
                    .flatMap((v) => v ?? [])
                    .filter((s): s is string => typeof s === "string");
                if (lines.length > 0) {
                    return {
                        messageKey: "account.changePassword.validationFailed",
                        detail: lines.join(" "),
                    };
                }
            }
        } catch {
            // Fall through to the generic message.
        }
    }
    return { messageKey: "account.changePassword.unexpectedError" };
}

/**
 * Change-password route. Reachable from the navbar for any signed-in
 * user, and forced on any user whose account carries
 * `MustRotatePassword` (bootstrap admin, admin-created accounts,
 * admin-reset accounts). The router guard bounces those users here
 * regardless of where they were trying to go.
 *
 * On success we re-run `whoami()` so the server has the last word on
 * whether the rotation flag is now cleared, then clear the local
 * flag optimistically for a snappy UX.
 */
export function ChangePasswordRoute() {
    const { t } = useTranslation();
    const user = useSessionStore((s) => s.user);
    const setMustRotate = useSessionStore((s) => s.setMustRotatePassword);
    const [serverError, setServerError] = useState<ServerErrorInfo | null>(null);
    const [succeeded, setSucceeded] = useState(false);

    const form = useForm<FormValues>({
        mode: "controlled",
        initialValues: {
            currentPassword: "",
            newPassword: "",
            confirmPassword: "",
        },
        validate: {
            currentPassword: (value) =>
                value.length === 0
                    ? t("account.changePassword.currentPasswordRequired")
                    : null,
            newPassword: (value, values) => {
                if (value.length === 0) {
                    return t("account.changePassword.newPasswordRequired");
                }
                if (value === values.currentPassword) {
                    return t("account.changePassword.sameAsCurrent");
                }
                return null;
            },
            confirmPassword: (value, values) => {
                if (value.length === 0) {
                    return t("account.changePassword.confirmPasswordRequired");
                }
                if (value !== values.newPassword) {
                    return t("account.changePassword.confirmMismatch");
                }
                return null;
            },
        },
    });

    const mutation = useMutation({
        mutationFn: async (values: FormValues) => {
            await changePassword({
                currentPassword: values.currentPassword,
                newPassword: values.newPassword,
            });
            // Re-hydrate the session so the guard stops bouncing us.
            // whoami() authoritatively reports the flag; if it fails we
            // still fall back to clearing the flag locally so the user
            // isn't stranded on this page.
            try {
                const me = await whoami();
                setMustRotate(me.mustRotatePassword);
            } catch {
                setMustRotate(false);
            }
        },
        onSuccess: () => {
            setServerError(null);
            setSucceeded(true);
            form.reset();
        },
        onError: (error) => {
            setServerError(parseServerError(error));
        },
    });

    if (!user) {
        // Should never render — the router guard sends anonymous users
        // to /login before this route mounts — but keep a defensive
        // fallback in case someone hits the URL through the browser
        // history before the store rehydrates.
        return null;
    }

    if (succeeded) {
        return (
            <Stack gap="lg" maw={480}>
                <Title order={2}>{t("account.changePassword.success")}</Title>
                <Card withBorder padding="lg" radius="md">
                    <Stack gap="md">
                        <Alert
                            color="green"
                            icon={<IconCircleCheck size={18} />}
                            role="status"
                        >
                            {t("account.changePassword.successBody")}
                        </Alert>
                        <Group>
                            <Anchor
                                component={Link}
                                to="/"
                                onClick={() => setSucceeded(false)}
                            >
                                {t("account.changePassword.continueHome")}
                            </Anchor>
                        </Group>
                    </Stack>
                </Card>
            </Stack>
        );
    }

    return (
        <Stack gap="lg" maw={480}>
            <Title order={2}>{t("account.changePassword.title")}</Title>
            <Text c="dimmed">{t("account.changePassword.subtitle")}</Text>
            <Card withBorder padding="lg" radius="md">
                <form
                    onSubmit={form.onSubmit((values) => mutation.mutate(values))}
                    noValidate
                >
                    <Stack gap="md">
                        {user.mustRotatePassword && (
                            <Alert
                                color="yellow"
                                icon={<IconAlertCircle size={18} />}
                                role="status"
                            >
                                {t("account.changePassword.mustRotateBanner")}
                            </Alert>
                        )}
                        {serverError && (
                            <Alert
                                color="red"
                                icon={<IconAlertCircle size={18} />}
                                role="alert"
                            >
                                <Stack gap={4}>
                                    <Text>{t(serverError.messageKey)}</Text>
                                    {serverError.detail && (
                                        <Text size="sm" c="dimmed">
                                            {serverError.detail}
                                        </Text>
                                    )}
                                </Stack>
                            </Alert>
                        )}
                        <PasswordInput
                            label={t(
                                "account.changePassword.currentPasswordLabel",
                            )}
                            placeholder={t(
                                "account.changePassword.currentPasswordPlaceholder",
                            )}
                            autoComplete="current-password"
                            required
                            {...form.getInputProps("currentPassword")}
                        />
                        <PasswordInput
                            label={t("account.changePassword.newPasswordLabel")}
                            placeholder={t(
                                "account.changePassword.newPasswordPlaceholder",
                            )}
                            autoComplete="new-password"
                            required
                            {...form.getInputProps("newPassword")}
                        />
                        <PasswordInput
                            label={t(
                                "account.changePassword.confirmPasswordLabel",
                            )}
                            placeholder={t(
                                "account.changePassword.confirmPasswordPlaceholder",
                            )}
                            autoComplete="new-password"
                            required
                            {...form.getInputProps("confirmPassword")}
                        />
                        <Divider my="xs" />
                        <Group justify="flex-end">
                            {/* Show a cancel/back-home link only when
                                the user is not forced to rotate — a
                                forced-rotation user has no way out. */}
                            {!user.mustRotatePassword && (
                                <Anchor component={Link} to="/">
                                    {t("account.changePassword.cancel")}
                                </Anchor>
                            )}
                            <Button type="submit" loading={mutation.isPending}>
                                {mutation.isPending
                                    ? t("account.changePassword.submitting")
                                    : t("account.changePassword.submit")}
                            </Button>
                        </Group>
                    </Stack>
                </form>
            </Card>
        </Stack>
    );
}
