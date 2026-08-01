import { useEffect, useState } from "react";
import {
    Alert,
    Badge,
    Button,
    Card,
    Checkbox,
    Group,
    Modal,
    PasswordInput,
    Stack,
    Table,
    Text,
    TextInput,
    Title,
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
    IconAlertCircle,
    IconEdit,
    IconKey,
    IconRefresh,
    IconUserPlus,
} from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    createAdminUser,
    listAdminUsers,
    resetAdminUserPassword,
    updateAdminUser,
    type AdminUserDto,
    type CreateUserRequest,
    type UpdateUserRequest,
} from "../api/adminUsers";
import { ApiError } from "../api/client";
import { useDateTimeFormatter } from "../i18n/formatters";
import { useSessionStore } from "../state/session";
import { relativeFromNow } from "../components/freshness";
import { MultiSelectField } from "../components/MultiSelectField";

/**
 * Admin-only users route. Lists local accounts and lets an admin
 * create new users, edit display name / roles / disabled state, and
 * reset passwords. Route-level gating is handled by the router's
 * beforeLoad hook (see router/router.ts); this component defends in
 * depth by also refusing to render if the current session is missing
 * the Admin role.
 */

const ROLE_OPTIONS = ["Reader", "Author", "Admin"] as const;

const ADMIN_USERS_QUERY_KEY = ["admin", "users"] as const;

type ServerErrorInfo = {
    /** i18n key for the top-level message. */
    messageKey:
        | "admin.users.create.conflict"
        | "admin.users.create.validationFailed"
        | "admin.users.create.unexpectedError"
        | "admin.users.edit.conflictLastAdmin"
        | "admin.users.edit.conflictSelfDisable"
        | "admin.users.edit.validationFailed"
        | "admin.users.edit.unexpectedError"
        | "admin.users.reset.validationFailed"
        | "admin.users.reset.unexpectedError";
    /** Extra lines pulled out of a ValidationProblem body. */
    detail?: string;
};

function parseServerError(
    error: unknown,
    kind: "create" | "edit" | "reset",
): ServerErrorInfo {
    if (!(error instanceof ApiError)) {
        return kind === "create"
            ? { messageKey: "admin.users.create.unexpectedError" }
            : kind === "edit"
                ? { messageKey: "admin.users.edit.unexpectedError" }
                : { messageKey: "admin.users.reset.unexpectedError" };
    }
    if (kind === "create" && error.status === 409) {
        return { messageKey: "admin.users.create.conflict" };
    }
    if (kind === "edit" && error.status === 409) {
        // Best-effort discrimination between the two 409 flavours based
        // on the plain-text body the endpoint returns.
        if (error.body.includes("own account")) {
            return { messageKey: "admin.users.edit.conflictSelfDisable" };
        }
        return { messageKey: "admin.users.edit.conflictLastAdmin" };
    }
    if (error.status === 400) {
        const detail = extractValidationDetail(error.body);
        return {
            messageKey:
                kind === "create"
                    ? "admin.users.create.validationFailed"
                    : kind === "edit"
                        ? "admin.users.edit.validationFailed"
                        : "admin.users.reset.validationFailed",
            detail,
        };
    }
    return kind === "create"
        ? { messageKey: "admin.users.create.unexpectedError" }
        : kind === "edit"
            ? { messageKey: "admin.users.edit.unexpectedError" }
            : { messageKey: "admin.users.reset.unexpectedError" };
}

function extractValidationDetail(body: string): string | undefined {
    try {
        const parsed = JSON.parse(body) as {
            errors?: Record<string, string[]>;
        };
        if (!parsed.errors) return undefined;
        return Object.values(parsed.errors).flat().join("; ");
    } catch {
        return body.length > 0 ? body : undefined;
    }
}

export function AdminUsersRoute() {
    const { t } = useTranslation();
    const roles = useSessionStore((s) => s.user?.roles ?? []);
    const isAdmin = roles.includes("Admin");
    const queryClient = useQueryClient();
    const [createOpen, setCreateOpen] = useState(false);
    const [editing, setEditing] = useState<AdminUserDto | null>(null);
    const [resetting, setResetting] = useState<AdminUserDto | null>(null);

    const query = useQuery({
        queryKey: ADMIN_USERS_QUERY_KEY,
        queryFn: listAdminUsers,
        enabled: isAdmin,
        refetchOnWindowFocus: false,
    });

    const dateFormatter = useDateTimeFormatter({
        dateStyle: "medium",
        timeStyle: "short",
    });

    if (!isAdmin) {
        return (
            <Stack gap="md">
                <Title order={2}>{t("admin.users.title")}</Title>
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("admin.users.forbidden")}
                </Alert>
            </Stack>
        );
    }

    const rows = query.data ?? [];

    return (
        <Stack gap="lg">
            <Group justify="space-between" align="flex-end" wrap="wrap">
                <Stack gap={4}>
                    <Title order={2}>{t("admin.users.title")}</Title>
                    <Text c="dimmed" size="sm">
                        {t("admin.users.subtitle")}
                    </Text>
                </Stack>
                <Group gap="xs">
                    <Button
                        variant="default"
                        leftSection={<IconRefresh size={16} />}
                        onClick={() => query.refetch()}
                        loading={query.isFetching && !query.isLoading}
                    >
                        {t("admin.users.reload")}
                    </Button>
                    <Button
                        leftSection={<IconUserPlus size={16} />}
                        onClick={() => setCreateOpen(true)}
                    >
                        {t("admin.users.createButton")}
                    </Button>
                </Group>
            </Group>

            {query.isError && (
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("admin.users.loadError")}
                </Alert>
            )}

            <Card withBorder radius="md" padding="lg">
                {query.isLoading ? (
                    <Text c="dimmed">{t("common.loading")}</Text>
                ) : rows.length === 0 ? (
                    <Text c="dimmed">{t("admin.users.emptyState")}</Text>
                ) : (
                    <Table striped highlightOnHover withColumnBorders>
                        <Table.Thead>
                            <Table.Tr>
                                <Table.Th>{t("admin.users.columns.email")}</Table.Th>
                                <Table.Th>{t("admin.users.columns.displayName")}</Table.Th>
                                <Table.Th>{t("admin.users.columns.roles")}</Table.Th>
                                <Table.Th>{t("admin.users.columns.status")}</Table.Th>
                                <Table.Th>{t("admin.users.columns.lastLogin")}</Table.Th>
                                <Table.Th>{t("admin.users.columns.actions")}</Table.Th>
                            </Table.Tr>
                        </Table.Thead>
                        <Table.Tbody>
                            {rows.map((row) => (
                                <Table.Tr key={row.id}>
                                    <Table.Td>{row.email}</Table.Td>
                                    <Table.Td>{row.displayName}</Table.Td>
                                    <Table.Td>
                                        <Group gap={4}>
                                            {row.roles.map((r) => (
                                                <Badge key={r} variant="light">
                                                    {r}
                                                </Badge>
                                            ))}
                                        </Group>
                                    </Table.Td>
                                    <Table.Td>
                                        <Badge
                                            color={row.isDisabled ? "red" : "green"}
                                            variant="light"
                                        >
                                            {row.isDisabled
                                                ? t("admin.users.status.disabled")
                                                : t("admin.users.status.active")}
                                        </Badge>
                                    </Table.Td>
                                    <Table.Td>
                                        {row.lastLoginUtc
                                            ? formatLastLogin(
                                                new Date(row.lastLoginUtc),
                                                dateFormatter,
                                                (key, params) =>
                                                    // Types checked at the call site
                                                    // (the RelativeTimeKey union),
                                                    // t() is happy with those keys.
                                                    t(key, params),
                                            )
                                            : t("admin.users.never")}
                                    </Table.Td>
                                    <Table.Td>
                                        <Group gap={4}>
                                            <Button
                                                size="xs"
                                                variant="default"
                                                leftSection={<IconEdit size={14} />}
                                                onClick={() => setEditing(row)}
                                            >
                                                {t("admin.users.actions.edit")}
                                            </Button>
                                            <Button
                                                size="xs"
                                                variant="default"
                                                leftSection={<IconKey size={14} />}
                                                onClick={() => setResetting(row)}
                                            >
                                                {t("admin.users.actions.resetPassword")}
                                            </Button>
                                        </Group>
                                    </Table.Td>
                                </Table.Tr>
                            ))}
                        </Table.Tbody>
                    </Table>
                )}
            </Card>

            <CreateUserModal
                opened={createOpen}
                onClose={() => setCreateOpen(false)}
                onSuccess={() => {
                    setCreateOpen(false);
                    void queryClient.invalidateQueries({ queryKey: ADMIN_USERS_QUERY_KEY });
                }}
            />
            <EditUserModal
                user={editing}
                onClose={() => setEditing(null)}
                onSuccess={() => {
                    setEditing(null);
                    void queryClient.invalidateQueries({ queryKey: ADMIN_USERS_QUERY_KEY });
                }}
            />
            <ResetPasswordModal
                user={resetting}
                onClose={() => setResetting(null)}
                onSuccess={() => setResetting(null)}
            />
        </Stack>
    );
}

function formatLastLogin(
    when: Date,
    formatter: Intl.DateTimeFormat,
    translate: (
        key:
            | "freshness.relative.justNow"
            | "freshness.relative.secondsAgo"
            | "freshness.relative.minutesAgo"
            | "freshness.relative.hoursAgo"
            | "freshness.relative.daysAgo"
            | "freshness.relative.inFuture",
        params?: Record<string, number>,
    ) => string,
) {
    const rel = relativeFromNow(when);
    const relLabel = translate(rel.key, rel.params);
    return `${formatter.format(when)} (${relLabel})`;
}

// ---------------------------------------------------------------- Create ----

function CreateUserModal(props: {
    opened: boolean;
    onClose: () => void;
    onSuccess: (created: AdminUserDto) => void;
}) {
    const { t } = useTranslation();
    const [error, setError] = useState<ServerErrorInfo | null>(null);
    const form = useForm<CreateUserRequest>({
        mode: "controlled",
        initialValues: {
            email: "",
            displayName: "",
            password: "",
            roles: [],
        },
        validate: {
            email: (v) => {
                if (!v.trim()) return t("admin.users.create.emailRequired");
                if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v.trim())) {
                    return t("admin.users.create.emailInvalid");
                }
                return null;
            },
            displayName: (v) =>
                v.trim().length === 0
                    ? t("admin.users.create.displayNameRequired")
                    : null,
            password: (v) =>
                v.length === 0
                    ? t("admin.users.create.passwordRequired")
                    : null,
        },
    });

    const mutation = useMutation({
        mutationFn: createAdminUser,
        onSuccess: (created) => {
            setError(null);
            form.reset();
            props.onSuccess(created);
        },
        onError: (err) => setError(parseServerError(err, "create")),
    });

    // Reset form + errors every time the modal opens.
    const handleClose = () => {
        form.reset();
        setError(null);
        props.onClose();
    };

    return (
        <Modal
            opened={props.opened}
            onClose={handleClose}
            title={t("admin.users.create.title")}
            centered
        >
            <form
                onSubmit={form.onSubmit((values) =>
                    mutation.mutate({
                        email: values.email.trim(),
                        displayName: values.displayName.trim(),
                        password: values.password,
                        roles: values.roles,
                    }),
                )}
                noValidate
            >
                <Stack gap="md">
                    {error && (
                        <Alert
                            color="red"
                            icon={<IconAlertCircle size={18} />}
                            role="alert"
                        >
                            <Text>{t(error.messageKey)}</Text>
                            {error.detail && <Text size="sm">{error.detail}</Text>}
                        </Alert>
                    )}
                    <TextInput
                        label={t("admin.users.create.emailLabel")}
                        placeholder={t("admin.users.create.emailPlaceholder")}
                        type="email"
                        required
                        {...form.getInputProps("email")}
                    />
                    <TextInput
                        label={t("admin.users.create.displayNameLabel")}
                        placeholder={t("admin.users.create.displayNamePlaceholder")}
                        required
                        {...form.getInputProps("displayName")}
                    />
                    <PasswordInput
                        label={t("admin.users.create.passwordLabel")}
                        placeholder={t("admin.users.create.passwordPlaceholder")}
                        autoComplete="new-password"
                        required
                        {...form.getInputProps("password")}
                    />
                    <MultiSelectField
                        label={t("admin.users.create.rolesLabel")}
                        placeholder={t("admin.users.create.rolesPlaceholder")}
                        data={[...ROLE_OPTIONS]}
                        {...form.getInputProps("roles")}
                    />
                    <Group justify="flex-end">
                        <Button variant="default" onClick={handleClose}>
                            {t("admin.users.create.cancel")}
                        </Button>
                        <Button type="submit" loading={mutation.isPending}>
                            {t("admin.users.create.submit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}

// ------------------------------------------------------------------ Edit ----

function EditUserModal(props: {
    user: AdminUserDto | null;
    onClose: () => void;
    onSuccess: (updated: AdminUserDto) => void;
}) {
    const { t } = useTranslation();
    const [error, setError] = useState<ServerErrorInfo | null>(null);
    const form = useForm<UpdateUserRequest>({
        mode: "controlled",
        initialValues: {
            displayName: "",
            isDisabled: false,
            roles: [],
        },
    });

    // Sync form when a new user is selected for editing.
    const currentId = props.user?.id;
    useEffect(() => {
        if (props.user) {
            form.setValues({
                displayName: props.user.displayName,
                isDisabled: props.user.isDisabled,
                roles: [...props.user.roles],
            });
            setError(null);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [currentId]);

    const mutation = useMutation({
        mutationFn: (values: UpdateUserRequest) =>
            updateAdminUser(props.user!.id, values),
        onSuccess: (updated) => {
            setError(null);
            props.onSuccess(updated);
        },
        onError: (err) => setError(parseServerError(err, "edit")),
    });

    return (
        <Modal
            opened={props.user !== null}
            onClose={props.onClose}
            title={
                props.user
                    ? `${t("admin.users.edit.title")} — ${props.user.email}`
                    : t("admin.users.edit.title")
            }
            centered
        >
            {props.user && (
                <form
                    onSubmit={form.onSubmit((values) =>
                        mutation.mutate({
                            displayName: values.displayName.trim(),
                            isDisabled: values.isDisabled,
                            roles: values.roles,
                        }),
                    )}
                    noValidate
                >
                    <Stack gap="md">
                        {error && (
                            <Alert
                                color="red"
                                icon={<IconAlertCircle size={18} />}
                                role="alert"
                            >
                                <Text>{t(error.messageKey)}</Text>
                                {error.detail && <Text size="sm">{error.detail}</Text>}
                            </Alert>
                        )}
                        <TextInput
                            label={t("admin.users.edit.displayNameLabel")}
                            required
                            {...form.getInputProps("displayName")}
                        />
                        <MultiSelectField
                            label={t("admin.users.edit.rolesLabel")}
                            data={[...ROLE_OPTIONS]}
                            {...form.getInputProps("roles")}
                        />
                        <Checkbox
                            label={t("admin.users.edit.isDisabledLabel")}
                            description={t("admin.users.edit.isDisabledHint")}
                            {...form.getInputProps("isDisabled", { type: "checkbox" })}
                        />
                        <Group justify="flex-end">
                            <Button variant="default" onClick={props.onClose}>
                                {t("admin.users.edit.cancel")}
                            </Button>
                            <Button type="submit" loading={mutation.isPending}>
                                {t("admin.users.edit.submit")}
                            </Button>
                        </Group>
                    </Stack>
                </form>
            )}
        </Modal>
    );
}

// ------------------------------------------------------------- Reset password
function ResetPasswordModal(props: {
    user: AdminUserDto | null;
    onClose: () => void;
    onSuccess: () => void;
}) {
    const { t } = useTranslation();
    const [error, setError] = useState<ServerErrorInfo | null>(null);
    const form = useForm<{ newPassword: string }>({
        mode: "controlled",
        initialValues: { newPassword: "" },
        validate: {
            newPassword: (v) =>
                v.length === 0
                    ? t("admin.users.create.passwordRequired")
                    : null,
        },
    });
    const currentId = props.user?.id;
    useEffect(() => {
        form.reset();
        setError(null);
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [currentId]);

    const mutation = useMutation({
        mutationFn: (values: { newPassword: string }) =>
            resetAdminUserPassword(props.user!.id, values),
        onSuccess: () => {
            setError(null);
            form.reset();
            props.onSuccess();
        },
        onError: (err) => setError(parseServerError(err, "reset")),
    });

    return (
        <Modal
            opened={props.user !== null}
            onClose={props.onClose}
            title={t("admin.users.reset.title")}
            centered
        >
            {props.user && (
                <form
                    onSubmit={form.onSubmit((values) => mutation.mutate(values))}
                    noValidate
                >
                    <Stack gap="md">
                        <Text size="sm" c="dimmed">
                            {t("admin.users.reset.body", { email: props.user.email })}
                        </Text>
                        {error && (
                            <Alert
                                color="red"
                                icon={<IconAlertCircle size={18} />}
                                role="alert"
                            >
                                <Text>{t(error.messageKey)}</Text>
                                {error.detail && <Text size="sm">{error.detail}</Text>}
                            </Alert>
                        )}
                        <PasswordInput
                            label={t("admin.users.reset.newPasswordLabel")}
                            autoComplete="new-password"
                            required
                            {...form.getInputProps("newPassword")}
                        />
                        <Group justify="flex-end">
                            <Button variant="default" onClick={props.onClose}>
                                {t("admin.users.reset.cancel")}
                            </Button>
                            <Button type="submit" loading={mutation.isPending}>
                                {t("admin.users.reset.submit")}
                            </Button>
                        </Group>
                    </Stack>
                </form>
            )}
        </Modal>
    );
}
