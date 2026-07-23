import { useEffect, useMemo, useState } from "react";
import {
    Alert,
    Badge,
    Button,
    Card,
    Checkbox,
    Group,
    Loader,
    Modal,
    NumberInput,
    PasswordInput,
    Select,
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
    IconCircleCheck,
    IconDatabase,
    IconEdit,
    IconPlugConnected,
    IconPlus,
    IconRefresh,
    IconRestore,
    IconTrash,
} from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    deleteAoiSource,
    getRestartStatus,
    listAoiSources,
    restartApi,
    testAoiSource,
    upsertAoiSource,
    waitForApi,
    type AoiSourceConfigDto,
    type AoiSourceTestResult,
    type AoiSourceUpsertRequest,
} from "../api/adminDataSources";
import { ApiError } from "../api/client";
import { useDateTimeFormatter } from "../i18n/formatters";
import { useSessionStore } from "../state/session";

/**
 * Admin-only Databases settings route (Phase C — docs/phase-3.md).
 * <ul>
 *   <li>Table of configured AOI sources (from
 *       <c>GET /api/admin/data-sources</c>).</li>
 *   <li>Add / edit modal with a "Test connection" button that hits
 *       the server-side probe.</li>
 *   <li>Delete confirmation.</li>
 *   <li>Restart banner: polls <c>/api/admin/data-sources/restart-status</c>
 *       every 5s; when armed, the admin can click "Restart API now",
 *       which triggers the shutdown then polls <c>/health/live</c>
 *       until the process is back up.</li>
 * </ul>
 * Route-level gating is handled by the router (requireAuthentication);
 * the Admin role check lives inside this component so we can render
 * a localised forbidden panel for signed-in-but-not-admin users.
 */

const SOURCES_QUERY_KEY = ["admin", "dataSources", "list"] as const;
const RESTART_STATUS_QUERY_KEY = ["admin", "dataSources", "restart"] as const;

type UpsertMode =
    | { kind: "create" }
    | { kind: "edit"; row: AoiSourceConfigDto };

type UpsertErrorInfo = {
    messageKey:
        | "settings.databases.upsert.conflict"
        | "settings.databases.upsert.validationFailed"
        | "settings.databases.upsert.unexpectedError";
    detail?: string;
};

function extractValidationDetail(body: string): string | undefined {
    try {
        const parsed = JSON.parse(body) as {
            errors?: Record<string, string[]>;
        };
        if (!parsed.errors) return body.length > 0 ? body : undefined;
        return Object.values(parsed.errors).flat().join("; ");
    } catch {
        return body.length > 0 ? body : undefined;
    }
}

function parseUpsertError(error: unknown): UpsertErrorInfo {
    if (!(error instanceof ApiError)) {
        return { messageKey: "settings.databases.upsert.unexpectedError" };
    }
    if (error.status === 409) {
        return { messageKey: "settings.databases.upsert.conflict" };
    }
    if (error.status === 400) {
        return {
            messageKey: "settings.databases.upsert.validationFailed",
            detail: extractValidationDetail(error.body),
        };
    }
    return { messageKey: "settings.databases.upsert.unexpectedError" };
}

export function SettingsDatabasesRoute() {
    const { t } = useTranslation();
    const roles = useSessionStore((s) => s.user?.roles ?? []);
    const isAdmin = roles.includes("Admin");
    const queryClient = useQueryClient();

    const [upsertMode, setUpsertMode] = useState<UpsertMode | null>(null);
    const [deleting, setDeleting] = useState<AoiSourceConfigDto | null>(null);
    const [restartPhase, setRestartPhase] = useState<
        "idle" | "restarting" | "restarted" | "failed"
    >("idle");

    const sourcesQuery = useQuery({
        queryKey: SOURCES_QUERY_KEY,
        queryFn: listAoiSources,
        enabled: isAdmin,
        refetchOnWindowFocus: false,
    });

    const restartStatusQuery = useQuery({
        queryKey: RESTART_STATUS_QUERY_KEY,
        queryFn: getRestartStatus,
        enabled: isAdmin,
        refetchInterval: 5_000,
        refetchOnWindowFocus: false,
    });

    const restartMutation = useMutation({
        mutationFn: restartApi,
        onSuccess: async () => {
            setRestartPhase("restarting");
            const ok = await waitForApi({ timeoutMs: 90_000 });
            setRestartPhase(ok ? "restarted" : "failed");
            if (ok) {
                await queryClient.invalidateQueries({
                    queryKey: SOURCES_QUERY_KEY,
                });
                await queryClient.invalidateQueries({
                    queryKey: RESTART_STATUS_QUERY_KEY,
                });
            }
        },
        onError: () => setRestartPhase("failed"),
    });

    const dateFormatter = useDateTimeFormatter({
        dateStyle: "short",
        timeStyle: "medium",
    });

    if (!isAdmin) {
        return (
            <Stack gap="md">
                <Title order={2}>{t("settings.databases.title")}</Title>
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("settings.databases.forbidden")}
                </Alert>
            </Stack>
        );
    }

    const rows = sourcesQuery.data ?? [];
    const status = restartStatusQuery.data;
    const restartPending = status?.pending === true;

    return (
        <Stack gap="lg">
            <Group justify="space-between" align="flex-end" wrap="wrap">
                <Stack gap={4}>
                    <Title order={2}>{t("settings.databases.title")}</Title>
                    <Text c="dimmed" size="sm">
                        {t("settings.databases.subtitle")}
                    </Text>
                </Stack>
                <Group gap="xs">
                    <Button
                        variant="default"
                        leftSection={<IconRefresh size={16} />}
                        onClick={() => void sourcesQuery.refetch()}
                        loading={
                            sourcesQuery.isFetching && !sourcesQuery.isLoading
                        }
                    >
                        {t("settings.databases.reload")}
                    </Button>
                    <Button
                        leftSection={<IconPlus size={16} />}
                        onClick={() => setUpsertMode({ kind: "create" })}
                    >
                        {t("settings.databases.addButton")}
                    </Button>
                </Group>
            </Group>

            <RestartBanner
                pending={restartPending}
                reason={status?.reason ?? null}
                phase={restartPhase}
                onRestart={() => {
                    setRestartPhase("idle");
                    restartMutation.mutate();
                }}
                onDismissResult={() => setRestartPhase("idle")}
                restartLoading={restartMutation.isPending}
            />

            {sourcesQuery.isError && (
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("settings.databases.loadError")}
                </Alert>
            )}

            <SourcesTable
                rows={rows}
                isLoading={sourcesQuery.isLoading}
                dateFormatter={dateFormatter}
                onEdit={(row) => setUpsertMode({ kind: "edit", row })}
                onDelete={setDeleting}
            />

            <UpsertModal
                mode={upsertMode}
                existingKeys={rows.map((r) => r.key)}
                onClose={() => setUpsertMode(null)}
                onSuccess={() => {
                    setUpsertMode(null);
                    void queryClient.invalidateQueries({
                        queryKey: SOURCES_QUERY_KEY,
                    });
                    void queryClient.invalidateQueries({
                        queryKey: RESTART_STATUS_QUERY_KEY,
                    });
                }}
            />
            <DeleteModal
                row={deleting}
                onClose={() => setDeleting(null)}
                onDeleted={() => {
                    setDeleting(null);
                    void queryClient.invalidateQueries({
                        queryKey: SOURCES_QUERY_KEY,
                    });
                    void queryClient.invalidateQueries({
                        queryKey: RESTART_STATUS_QUERY_KEY,
                    });
                }}
            />
        </Stack>
    );
}

// ------------------------------------------------------------ Restart banner

function RestartBanner(props: {
    pending: boolean;
    reason: string | null;
    phase: "idle" | "restarting" | "restarted" | "failed";
    restartLoading: boolean;
    onRestart: () => void;
    onDismissResult: () => void;
}) {
    const { t } = useTranslation();
    const {
        pending,
        reason,
        phase,
        restartLoading,
        onRestart,
        onDismissResult,
    } = props;

    if (phase === "restarting") {
        return (
            <Alert
                color="blue"
                icon={<Loader size="sm" />}
                title={t(
                    "settings.databases.restartBanner.restartingTitle",
                )}
                data-testid="databases-restarting"
            >
                {t("settings.databases.restartBanner.restartingBody")}
            </Alert>
        );
    }
    if (phase === "restarted") {
        return (
            <Alert
                color="green"
                icon={<IconCircleCheck size={18} />}
                title={t("settings.databases.restartBanner.restartedTitle")}
                withCloseButton
                onClose={onDismissResult}
                data-testid="databases-restarted"
            >
                {t("settings.databases.restartBanner.restartedBody")}
            </Alert>
        );
    }
    if (phase === "failed") {
        return (
            <Alert
                color="red"
                icon={<IconAlertCircle size={18} />}
                title={t(
                    "settings.databases.restartBanner.restartFailedTitle",
                )}
                withCloseButton
                onClose={onDismissResult}
                data-testid="databases-restart-failed"
            >
                {t("settings.databases.restartBanner.restartFailedBody")}
            </Alert>
        );
    }
    if (!pending) {
        return null;
    }
    return (
        <Alert
            color="yellow"
            icon={<IconAlertCircle size={18} />}
            title={t("settings.databases.restartBanner.pending")}
            data-testid="databases-restart-pending"
        >
            <Stack gap="xs">
                {reason && (
                    <Text size="sm">
                        {t(
                            "settings.databases.restartBanner.pendingReason",
                            { reason },
                        )}
                    </Text>
                )}
                <Group>
                    <Button
                        size="xs"
                        color="yellow"
                        leftSection={<IconRestore size={14} />}
                        onClick={onRestart}
                        loading={restartLoading}
                    >
                        {t(
                            "settings.databases.restartBanner.restartButton",
                        )}
                    </Button>
                </Group>
            </Stack>
        </Alert>
    );
}

// ------------------------------------------------------------- Sources table

function SourcesTable(props: {
    rows: AoiSourceConfigDto[];
    isLoading: boolean;
    dateFormatter: Intl.DateTimeFormat;
    onEdit: (row: AoiSourceConfigDto) => void;
    onDelete: (row: AoiSourceConfigDto) => void;
}) {
    const { t } = useTranslation();
    const { rows, isLoading, dateFormatter, onEdit, onDelete } = props;

    if (isLoading) {
        return (
            <Card withBorder padding="lg" radius="md">
                <Text c="dimmed">{t("common.loading")}</Text>
            </Card>
        );
    }
    if (rows.length === 0) {
        return (
            <Card withBorder padding="lg" radius="md">
                <Text c="dimmed">{t("settings.databases.emptyState")}</Text>
            </Card>
        );
    }
    return (
        <Card withBorder padding="lg" radius="md">
            <Table striped highlightOnHover withColumnBorders>
                <Table.Thead>
                    <Table.Tr>
                        <Table.Th>{t("settings.databases.columns.key")}</Table.Th>
                        <Table.Th>
                            {t("settings.databases.columns.displayName")}
                        </Table.Th>
                        <Table.Th>
                            {t("settings.databases.columns.kind")}
                        </Table.Th>
                        <Table.Th>
                            {t("settings.databases.columns.server")}
                        </Table.Th>
                        <Table.Th>
                            {t("settings.databases.columns.database")}
                        </Table.Th>
                        <Table.Th>
                            {t("settings.databases.columns.enabled")}
                        </Table.Th>
                        <Table.Th>
                            {t("settings.databases.columns.lastTested")}
                        </Table.Th>
                        <Table.Th>
                            {t("settings.databases.columns.actions")}
                        </Table.Th>
                    </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                    {rows.map((row) => (
                        <Table.Tr key={row.key} data-testid={`db-row-${row.key}`}>
                            <Table.Td style={{ fontFamily: "monospace" }}>
                                {row.key}
                            </Table.Td>
                            <Table.Td>{row.displayName}</Table.Td>
                            <Table.Td>
                                {t(
                                    // eslint-disable-next-line @typescript-eslint/no-explicit-any
                                    `settings.databases.kinds.${row.kind}` as any,
                                    { defaultValue: row.kind },
                                )}
                            </Table.Td>
                            <Table.Td>{row.server ?? "—"}</Table.Td>
                            <Table.Td>{row.database ?? "—"}</Table.Td>
                            <Table.Td>
                                <Badge
                                    color={row.isEnabled ? "green" : "gray"}
                                    variant="light"
                                >
                                    {row.isEnabled
                                        ? t("settings.databases.enabled")
                                        : t("settings.databases.disabled")}
                                </Badge>
                            </Table.Td>
                            <Table.Td>
                                {row.lastTestedUtc ? (
                                    <Group gap={4} wrap="nowrap">
                                        <Badge
                                            color={
                                                row.lastTestSucceeded
                                                    ? "green"
                                                    : "red"
                                            }
                                            variant="light"
                                            size="sm"
                                        >
                                            {row.lastTestSucceeded
                                                ? t(
                                                    "settings.databases.testPass",
                                                )
                                                : t(
                                                    "settings.databases.testFail",
                                                )}
                                        </Badge>
                                        <Text size="xs" c="dimmed">
                                            {dateFormatter.format(
                                                new Date(row.lastTestedUtc),
                                            )}
                                        </Text>
                                    </Group>
                                ) : (
                                    <Text size="xs" c="dimmed">
                                        {t("settings.databases.never")}
                                    </Text>
                                )}
                            </Table.Td>
                            <Table.Td>
                                <Group gap={4}>
                                    <Button
                                        size="xs"
                                        variant="default"
                                        leftSection={<IconEdit size={14} />}
                                        onClick={() => onEdit(row)}
                                    >
                                        {t(
                                            "settings.databases.actions.edit",
                                        )}
                                    </Button>
                                    <Button
                                        size="xs"
                                        variant="default"
                                        color="red"
                                        leftSection={<IconTrash size={14} />}
                                        onClick={() => onDelete(row)}
                                    >
                                        {t(
                                            "settings.databases.actions.delete",
                                        )}
                                    </Button>
                                </Group>
                            </Table.Td>
                        </Table.Tr>
                    ))}
                </Table.Tbody>
            </Table>
        </Card>
    );
}

// ---------------------------------------------------------- Upsert modal ----

type UpsertFormValues = {
    key: string;
    displayName: string;
    kind: string;
    server: string;
    database: string;
    user: string;
    password: string;
    connectTimeoutSeconds: number;
    queryTimeoutSeconds: number;
    trustServerCertificate: boolean;
    encrypt: boolean;
    isEnabled: boolean;
};

const EMPTY_FORM: UpsertFormValues = {
    key: "",
    displayName: "",
    kind: "SqlServer",
    server: "",
    database: "",
    user: "",
    password: "",
    connectTimeoutSeconds: 15,
    queryTimeoutSeconds: 30,
    trustServerCertificate: true,
    encrypt: false,
    isEnabled: true,
};

function toFormValues(row: AoiSourceConfigDto): UpsertFormValues {
    return {
        key: row.key,
        displayName: row.displayName,
        kind: row.kind,
        server: row.server ?? "",
        database: row.database ?? "",
        user: row.user ?? "",
        password: "",
        connectTimeoutSeconds: row.connectTimeoutSeconds,
        queryTimeoutSeconds: row.queryTimeoutSeconds,
        trustServerCertificate: row.trustServerCertificate,
        encrypt: row.encrypt,
        isEnabled: row.isEnabled,
    };
}

function toRequest(values: UpsertFormValues): AoiSourceUpsertRequest {
    const sql = values.kind === "SqlServer";
    return {
        displayName: values.displayName.trim(),
        kind: values.kind,
        server: sql ? values.server.trim() || null : null,
        database: sql ? values.database.trim() || null : null,
        user: sql ? values.user.trim() || null : null,
        password: values.password.length === 0 ? null : values.password,
        connectTimeoutSeconds: values.connectTimeoutSeconds,
        queryTimeoutSeconds: values.queryTimeoutSeconds,
        trustServerCertificate: values.trustServerCertificate,
        encrypt: values.encrypt,
        isEnabled: values.isEnabled,
    };
}

function UpsertModal(props: {
    mode: UpsertMode | null;
    existingKeys: string[];
    onClose: () => void;
    onSuccess: () => void;
}) {
    const { t } = useTranslation();
    const { mode, existingKeys, onClose, onSuccess } = props;
    const [serverError, setServerError] = useState<UpsertErrorInfo | null>(
        null,
    );
    const [testResult, setTestResult] = useState<AoiSourceTestResult | null>(
        null,
    );

    const isEdit = mode?.kind === "edit";

    const form = useForm<UpsertFormValues>({
        mode: "controlled",
        initialValues: EMPTY_FORM,
        validate: {
            key: (_v, values) => {
                if (values.key.trim().length === 0) {
                    return t("settings.databases.upsert.keyRequired");
                }
                return null;
            },
            displayName: (v) =>
                v.trim().length === 0
                    ? t("settings.databases.upsert.displayNameRequired")
                    : null,
            kind: (v) =>
                v.trim().length === 0
                    ? t("settings.databases.upsert.kindRequired")
                    : null,
            server: (v, values) =>
                values.kind === "SqlServer" && v.trim().length === 0
                    ? t("settings.databases.upsert.serverRequiredForSql")
                    : null,
            database: (v, values) =>
                values.kind === "SqlServer" && v.trim().length === 0
                    ? t("settings.databases.upsert.databaseRequiredForSql")
                    : null,
            user: (v, values) =>
                values.kind === "SqlServer" && v.trim().length === 0
                    ? t("settings.databases.upsert.userRequiredForSql")
                    : null,
        },
    });

    // Reset form + local state whenever the modal opens or the target row
    // changes.
    useEffect(() => {
        if (!mode) {
            return;
        }
        setServerError(null);
        setTestResult(null);
        if (mode.kind === "edit") {
            form.setValues(toFormValues(mode.row));
            form.resetDirty(toFormValues(mode.row));
        } else {
            form.setValues(EMPTY_FORM);
            form.resetDirty(EMPTY_FORM);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [mode]);

    const upsertMutation = useMutation({
        mutationFn: async (values: UpsertFormValues) => {
            const body = toRequest(values);
            return upsertAoiSource(values.key.trim(), body, {
                // Race-free create: server returns 409 if a row with
                // this key was inserted between our pre-flight check
                // and this PUT (RFC 7232 If-None-Match: *).
                ifNoneMatch: mode?.kind === "create",
            });
        },
        onSuccess: () => {
            setServerError(null);
            onSuccess();
        },
        onError: (err) => setServerError(parseUpsertError(err)),
    });

    const testMutation = useMutation({
        mutationFn: async (values: UpsertFormValues) => {
            const body = toRequest(values);
            return testAoiSource({ key: values.key.trim(), ...body });
        },
        onSuccess: (r) => setTestResult(r),
        onError: (err) =>
            setTestResult({
                ok: false,
                durationMs: 0,
                errorMessage:
                    err instanceof Error ? err.message : "Unexpected error",
            }),
    });

    const kindData = useMemo(
        () => [
            { value: "SqlServer", label: t("settings.databases.kinds.SqlServer") },
            { value: "Fake", label: t("settings.databases.kinds.Fake") },
        ],
        [t],
    );

    if (!mode) return null;

    const isSql = form.values.kind === "SqlServer";

    return (
        <Modal
            opened={true}
            onClose={onClose}
            title={
                isEdit
                    ? t("settings.databases.upsert.editTitle", {
                        key: mode.row.key,
                    })
                    : t("settings.databases.upsert.createTitle")
            }
            centered
            size="lg"
        >
            <form
                onSubmit={form.onSubmit((values) => {
                    // Client-side pre-flight for create: reject a key
                    // that already exists so operators don't silently
                    // overwrite an existing row via the PUT upsert. The
                    // alert reuses `serverError` so it renders through
                    // the same role="alert" element that HTTP 409 uses.
                    if (
                        mode?.kind === "create" &&
                        existingKeys.includes(values.key.trim())
                    ) {
                        setServerError({
                            messageKey: "settings.databases.upsert.conflict",
                        });
                        return;
                    }
                    upsertMutation.mutate(values);
                })}
                noValidate
            >
                <Stack gap="md">
                    {serverError && (
                        <Alert
                            color="red"
                            icon={<IconAlertCircle size={18} />}
                            role="alert"
                        >
                            <Text>{t(serverError.messageKey)}</Text>
                            {serverError.detail && (
                                <Text size="sm">{serverError.detail}</Text>
                            )}
                        </Alert>
                    )}

                    <TextInput
                        label={t("settings.databases.upsert.keyLabel")}
                        placeholder={t(
                            "settings.databases.upsert.keyPlaceholder",
                        )}
                        description={
                            isEdit
                                ? t("settings.databases.upsert.keyImmutable")
                                : t("settings.databases.upsert.keyHint")
                        }
                        required
                        readOnly={isEdit}
                        {...form.getInputProps("key")}
                    />
                    <TextInput
                        label={t("settings.databases.upsert.displayNameLabel")}
                        placeholder={t(
                            "settings.databases.upsert.displayNamePlaceholder",
                        )}
                        required
                        {...form.getInputProps("displayName")}
                    />
                    <Select
                        label={t("settings.databases.upsert.kindLabel")}
                        placeholder={t(
                            "settings.databases.upsert.kindPlaceholder",
                        )}
                        data={kindData}
                        required
                        allowDeselect={false}
                        {...form.getInputProps("kind")}
                    />

                    {isSql && (
                        <>
                            <TextInput
                                label={t(
                                    "settings.databases.upsert.serverLabel",
                                )}
                                placeholder={t(
                                    "settings.databases.upsert.serverPlaceholder",
                                )}
                                required
                                {...form.getInputProps("server")}
                            />
                            <TextInput
                                label={t(
                                    "settings.databases.upsert.databaseLabel",
                                )}
                                placeholder={t(
                                    "settings.databases.upsert.databasePlaceholder",
                                )}
                                required
                                {...form.getInputProps("database")}
                            />
                            <TextInput
                                label={t(
                                    "settings.databases.upsert.userLabel",
                                )}
                                placeholder={t(
                                    "settings.databases.upsert.userPlaceholder",
                                )}
                                required
                                autoComplete="off"
                                {...form.getInputProps("user")}
                            />
                            <PasswordInput
                                label={t(
                                    "settings.databases.upsert.passwordLabel",
                                )}
                                placeholder={t(
                                    "settings.databases.upsert.passwordPlaceholder",
                                )}
                                description={
                                    isEdit
                                        ? t(
                                            "settings.databases.upsert.passwordHintEdit",
                                        )
                                        : t(
                                            "settings.databases.upsert.passwordHintCreate",
                                        )
                                }
                                autoComplete="new-password"
                                {...form.getInputProps("password")}
                            />
                            <Group grow>
                                <NumberInput
                                    label={t(
                                        "settings.databases.upsert.connectTimeoutLabel",
                                    )}
                                    min={1}
                                    max={600}
                                    {...form.getInputProps(
                                        "connectTimeoutSeconds",
                                    )}
                                />
                                <NumberInput
                                    label={t(
                                        "settings.databases.upsert.queryTimeoutLabel",
                                    )}
                                    min={1}
                                    max={600}
                                    {...form.getInputProps(
                                        "queryTimeoutSeconds",
                                    )}
                                />
                            </Group>
                            <Group>
                                <Checkbox
                                    label={t(
                                        "settings.databases.upsert.trustServerCertificateLabel",
                                    )}
                                    {...form.getInputProps(
                                        "trustServerCertificate",
                                        { type: "checkbox" },
                                    )}
                                />
                                <Checkbox
                                    label={t(
                                        "settings.databases.upsert.encryptLabel",
                                    )}
                                    {...form.getInputProps("encrypt", {
                                        type: "checkbox",
                                    })}
                                />
                            </Group>
                        </>
                    )}

                    <Checkbox
                        label={t(
                            "settings.databases.upsert.isEnabledLabel",
                        )}
                        description={t(
                            "settings.databases.upsert.isEnabledHint",
                        )}
                        {...form.getInputProps("isEnabled", {
                            type: "checkbox",
                        })}
                    />

                    {testResult && (
                        <Alert
                            color={testResult.ok ? "green" : "red"}
                            icon={
                                testResult.ok ? (
                                    <IconCircleCheck size={18} />
                                ) : (
                                    <IconAlertCircle size={18} />
                                )
                            }
                            data-testid="databases-test-result"
                        >
                            {testResult.ok
                                ? t(
                                    "settings.databases.upsert.testSuccess",
                                    { ms: testResult.durationMs },
                                )
                                : t(
                                    "settings.databases.upsert.testFailure",
                                    {
                                        error:
                                            testResult.errorMessage ?? "?",
                                    },
                                )}
                        </Alert>
                    )}

                    <Group justify="space-between">
                        <Button
                            variant="default"
                            leftSection={<IconPlugConnected size={14} />}
                            onClick={() => {
                                if (form.validate().hasErrors) {
                                    return;
                                }
                                testMutation.mutate(form.getValues());
                            }}
                            loading={testMutation.isPending}
                        >
                            {testMutation.isPending
                                ? t("settings.databases.upsert.testing")
                                : t("settings.databases.upsert.testButton")}
                        </Button>
                        <Group>
                            <Button variant="default" onClick={onClose}>
                                {t("settings.databases.upsert.cancel")}
                            </Button>
                            <Button
                                type="submit"
                                loading={upsertMutation.isPending}
                            >
                                {isEdit
                                    ? t(
                                        "settings.databases.upsert.submitEdit",
                                    )
                                    : t(
                                        "settings.databases.upsert.submitCreate",
                                    )}
                            </Button>
                        </Group>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}

// ------------------------------------------------------------ Delete modal --

function DeleteModal(props: {
    row: AoiSourceConfigDto | null;
    onClose: () => void;
    onDeleted: () => void;
}) {
    const { t } = useTranslation();
    const { row, onClose, onDeleted } = props;
    const [error, setError] = useState<string | null>(null);

    const mutation = useMutation({
        mutationFn: (key: string) => deleteAoiSource(key),
        onSuccess: () => {
            setError(null);
            onDeleted();
        },
        onError: () =>
            setError(t("settings.databases.deleteModal.unexpectedError")),
    });

    return (
        <Modal
            opened={row !== null}
            onClose={onClose}
            title={t("settings.databases.deleteModal.title")}
            centered
        >
            {row && (
                <Stack gap="md">
                    {error && (
                        <Alert
                            color="red"
                            icon={<IconAlertCircle size={18} />}
                            role="alert"
                        >
                            {error}
                        </Alert>
                    )}
                    <Text>
                        {t("settings.databases.deleteModal.body", {
                            key: row.key,
                        })}
                    </Text>
                    <Group justify="flex-end">
                        <Button variant="default" onClick={onClose}>
                            {t("settings.databases.deleteModal.cancel")}
                        </Button>
                        <Button
                            color="red"
                            leftSection={<IconTrash size={14} />}
                            loading={mutation.isPending}
                            onClick={() => mutation.mutate(row.key)}
                        >
                            {t("settings.databases.deleteModal.submit")}
                        </Button>
                    </Group>
                </Stack>
            )}
        </Modal>
    );
}

// Export the icon so the RootLayout can reuse it for the NavLink.
export const DatabasesRouteIcon = IconDatabase;
