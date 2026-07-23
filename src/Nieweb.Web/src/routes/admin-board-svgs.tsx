import { useEffect, useState } from "react";
import {
    Alert,
    Badge,
    Button,
    Card,
    Checkbox,
    Group,
    List,
    Modal,
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
    IconPlus,
    IconRefresh,
    IconTrash,
    IconCloudDownload,
} from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    createBoardSvgSource,
    deleteBoardSvgSource,
    getBoardSvgStatus,
    listBoardSvgSources,
    syncBoardSvgsNow,
    updateBoardSvgSource,
    type BoardSvgSourceDto,
    type BoardSvgStatusDto,
    type BoardSvgSyncResultDto,
    type CreateBoardSvgSourceRequest,
    type UpdateBoardSvgSourceRequest,
} from "../api/adminBoardSvgs";
import { ApiError } from "../api/client";
import { useDateTimeFormatter } from "../i18n/formatters";
import { useSessionStore } from "../state/session";

/**
 * Admin-only board-SVG management route (docs/phase-2.md §7.5 TC4
 * Phase D). Combines three panels:
 * <ul>
 *   <li>Status card summarising the local cache directory, sync
 *       cadence, cached SVG list, and any known products that are
 *       still missing a cached SVG.</li>
 *   <li>Sources table with row-level edit / delete affordances plus
 *       an "Add source" modal.</li>
 *   <li>"Sync now" button that triggers an on-demand sweep and
 *       renders the per-source / per-product outcome in a result
 *       modal.</li>
 * </ul>
 *
 * Route-level gating is handled by the router (requireAuthentication);
 * the Admin role check lives inside this component so we can render
 * a localised forbidden panel for signed-in-but-not-admin users.
 */

const SOURCES_QUERY_KEY = ["admin", "boardSvgs", "sources"] as const;
const STATUS_QUERY_KEY = ["admin", "boardSvgs", "status"] as const;

type ServerErrorInfo = {
    messageKey:
        | "admin.boardSvgs.sources.create.conflict"
        | "admin.boardSvgs.sources.create.validationFailed"
        | "admin.boardSvgs.sources.create.unexpectedError"
        | "admin.boardSvgs.sources.edit.conflict"
        | "admin.boardSvgs.sources.edit.validationFailed"
        | "admin.boardSvgs.sources.edit.unexpectedError";
    detail?: string;
};

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

function parseServerError(
    error: unknown,
    kind: "create" | "edit",
): ServerErrorInfo {
    if (!(error instanceof ApiError)) {
        return kind === "create"
            ? { messageKey: "admin.boardSvgs.sources.create.unexpectedError" }
            : { messageKey: "admin.boardSvgs.sources.edit.unexpectedError" };
    }
    if (error.status === 409) {
        return kind === "create"
            ? { messageKey: "admin.boardSvgs.sources.create.conflict" }
            : { messageKey: "admin.boardSvgs.sources.edit.conflict" };
    }
    if (error.status === 400) {
        return {
            messageKey:
                kind === "create"
                    ? "admin.boardSvgs.sources.create.validationFailed"
                    : "admin.boardSvgs.sources.edit.validationFailed",
            detail: extractValidationDetail(error.body),
        };
    }
    return kind === "create"
        ? { messageKey: "admin.boardSvgs.sources.create.unexpectedError" }
        : { messageKey: "admin.boardSvgs.sources.edit.unexpectedError" };
}

export function AdminBoardSvgsRoute() {
    const { t } = useTranslation();
    const roles = useSessionStore((s) => s.user?.roles ?? []);
    const isAdmin = roles.includes("Admin");
    const queryClient = useQueryClient();

    const [createOpen, setCreateOpen] = useState(false);
    const [editing, setEditing] = useState<BoardSvgSourceDto | null>(null);
    const [deleting, setDeleting] = useState<BoardSvgSourceDto | null>(null);
    const [syncResult, setSyncResult] = useState<BoardSvgSyncResultDto | null>(
        null,
    );
    const [syncError, setSyncError] = useState<string | null>(null);

    const sourcesQuery = useQuery({
        queryKey: SOURCES_QUERY_KEY,
        queryFn: listBoardSvgSources,
        enabled: isAdmin,
        refetchOnWindowFocus: false,
    });

    const statusQuery = useQuery({
        queryKey: STATUS_QUERY_KEY,
        queryFn: getBoardSvgStatus,
        enabled: isAdmin,
        refetchOnWindowFocus: false,
    });

    const syncMutation = useMutation({
        mutationFn: syncBoardSvgsNow,
        onSuccess: (result) => {
            setSyncError(null);
            setSyncResult(result);
            void queryClient.invalidateQueries({ queryKey: SOURCES_QUERY_KEY });
            void queryClient.invalidateQueries({ queryKey: STATUS_QUERY_KEY });
        },
        onError: () => setSyncError(t("admin.boardSvgs.syncError")),
    });

    const dateFormatter = useDateTimeFormatter({
        dateStyle: "short",
        timeStyle: "medium",
    });

    if (!isAdmin) {
        return (
            <Stack gap="md">
                <Title order={2}>{t("admin.boardSvgs.title")}</Title>
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("admin.boardSvgs.forbidden")}
                </Alert>
            </Stack>
        );
    }

    const sources = sourcesQuery.data ?? [];
    const status = statusQuery.data;

    return (
        <Stack gap="lg">
            <Group justify="space-between" align="flex-end" wrap="wrap">
                <Stack gap={4}>
                    <Title order={2}>{t("admin.boardSvgs.title")}</Title>
                    <Text c="dimmed" size="sm">
                        {t("admin.boardSvgs.subtitle")}
                    </Text>
                </Stack>
                <Group gap="xs">
                    <Button
                        variant="default"
                        leftSection={<IconRefresh size={16} />}
                        onClick={() => {
                            void sourcesQuery.refetch();
                            void statusQuery.refetch();
                        }}
                        loading={
                            (sourcesQuery.isFetching && !sourcesQuery.isLoading) ||
                            (statusQuery.isFetching && !statusQuery.isLoading)
                        }
                    >
                        {t("admin.boardSvgs.reload")}
                    </Button>
                    <Button
                        leftSection={<IconCloudDownload size={16} />}
                        onClick={() => syncMutation.mutate()}
                        loading={syncMutation.isPending}
                    >
                        {syncMutation.isPending
                            ? t("admin.boardSvgs.syncRunning")
                            : t("admin.boardSvgs.syncNow")}
                    </Button>
                </Group>
            </Group>

            {(sourcesQuery.isError || statusQuery.isError) && (
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("admin.boardSvgs.loadError")}
                </Alert>
            )}
            {syncError && (
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                    withCloseButton
                    onClose={() => setSyncError(null)}
                >
                    {syncError}
                </Alert>
            )}

            <StatusCard status={status} dateFormatter={dateFormatter} />

            <SourcesCard
                sources={sources}
                isLoading={sourcesQuery.isLoading}
                onCreate={() => setCreateOpen(true)}
                onEdit={setEditing}
                onDelete={setDeleting}
                dateFormatter={dateFormatter}
            />

            <CreateSourceModal
                opened={createOpen}
                onClose={() => setCreateOpen(false)}
                onSuccess={() => {
                    setCreateOpen(false);
                    void queryClient.invalidateQueries({
                        queryKey: SOURCES_QUERY_KEY,
                    });
                    void queryClient.invalidateQueries({
                        queryKey: STATUS_QUERY_KEY,
                    });
                }}
            />
            <EditSourceModal
                source={editing}
                onClose={() => setEditing(null)}
                onSuccess={() => {
                    setEditing(null);
                    void queryClient.invalidateQueries({
                        queryKey: SOURCES_QUERY_KEY,
                    });
                    void queryClient.invalidateQueries({
                        queryKey: STATUS_QUERY_KEY,
                    });
                }}
            />
            <DeleteSourceModal
                source={deleting}
                onClose={() => setDeleting(null)}
                onDeleted={() => {
                    setDeleting(null);
                    void queryClient.invalidateQueries({
                        queryKey: SOURCES_QUERY_KEY,
                    });
                    void queryClient.invalidateQueries({
                        queryKey: STATUS_QUERY_KEY,
                    });
                }}
            />
            <SyncResultModal
                result={syncResult}
                onClose={() => setSyncResult(null)}
                dateFormatter={dateFormatter}
            />
        </Stack>
    );
}

// ---------------------------------------------------------------- Status ----

function StatusCard(props: {
    status: BoardSvgStatusDto | undefined;
    dateFormatter: Intl.DateTimeFormat;
}) {
    const { t } = useTranslation();
    const { status, dateFormatter } = props;

    return (
        <Card withBorder radius="md" padding="lg">
            <Stack gap="sm">
                <Text fw={600}>{t("admin.boardSvgs.status.heading")}</Text>
                {!status ? (
                    <Text c="dimmed">{t("common.loading")}</Text>
                ) : (
                    <>
                        <Group gap="xs" wrap="wrap">
                            <Text size="sm">
                                {t("admin.boardSvgs.status.cacheDirectory")}:
                            </Text>
                            <Text
                                size="sm"
                                style={{ fontFamily: "monospace" }}
                            >
                                {status.cacheDirectory}
                            </Text>
                            <Badge
                                color={
                                    status.cacheDirectoryExists ? "green" : "red"
                                }
                                variant="light"
                            >
                                {status.cacheDirectoryExists
                                    ? "OK"
                                    : t(
                                        "admin.boardSvgs.status.cacheDirectoryMissing",
                                    )}
                            </Badge>
                        </Group>
                        <Group gap="xs" wrap="wrap">
                            <Badge
                                color={status.syncEnabled ? "green" : "gray"}
                                variant="light"
                            >
                                {status.syncEnabled
                                    ? t("admin.boardSvgs.status.syncEnabled")
                                    : t("admin.boardSvgs.status.syncDisabled")}
                            </Badge>
                            <Badge variant="light">
                                {t("admin.boardSvgs.status.intervalSeconds", {
                                    seconds: status.intervalSeconds,
                                })}
                            </Badge>
                            <Badge variant="light">
                                {t("admin.boardSvgs.status.knownProducts", {
                                    count: status.knownProducts.length,
                                })}
                            </Badge>
                        </Group>

                        <Text size="sm" fw={500} mt="xs">
                            {t("admin.boardSvgs.status.cachedFiles")}
                        </Text>
                        {status.cache.length === 0 ? (
                            <Text c="dimmed" size="sm">
                                {t("admin.boardSvgs.status.cachedFilesEmpty")}
                            </Text>
                        ) : (
                            <Table striped withColumnBorders>
                                <Table.Thead>
                                    <Table.Tr>
                                        <Table.Th>
                                            {t(
                                                "admin.boardSvgs.status.columns.product",
                                            )}
                                        </Table.Th>
                                        <Table.Th>
                                            {t(
                                                "admin.boardSvgs.status.columns.file",
                                            )}
                                        </Table.Th>
                                        <Table.Th>
                                            {t(
                                                "admin.boardSvgs.status.columns.size",
                                            )}
                                        </Table.Th>
                                        <Table.Th>
                                            {t(
                                                "admin.boardSvgs.status.columns.lastWrite",
                                            )}
                                        </Table.Th>
                                    </Table.Tr>
                                </Table.Thead>
                                <Table.Tbody>
                                    {status.cache.map((c) => (
                                        <Table.Tr key={c.fileName}>
                                            <Table.Td>{c.productName}</Table.Td>
                                            <Table.Td>{c.fileName}</Table.Td>
                                            <Table.Td>{c.sizeBytes}</Table.Td>
                                            <Table.Td>
                                                {dateFormatter.format(
                                                    new Date(c.lastWriteTimeUtc),
                                                )}
                                            </Table.Td>
                                        </Table.Tr>
                                    ))}
                                </Table.Tbody>
                            </Table>
                        )}

                        <Text size="sm" fw={500} mt="xs">
                            {t("admin.boardSvgs.status.missingProducts")}
                        </Text>
                        {status.missingProducts.length === 0 ? (
                            <Text c="dimmed" size="sm">
                                {t(
                                    "admin.boardSvgs.status.missingProductsEmpty",
                                )}
                            </Text>
                        ) : (
                            <List size="sm" withPadding>
                                {status.missingProducts.map((p) => (
                                    <List.Item key={p}>{p}</List.Item>
                                ))}
                            </List>
                        )}
                    </>
                )}
            </Stack>
        </Card>
    );
}

// --------------------------------------------------------------- Sources ----

function SourcesCard(props: {
    sources: BoardSvgSourceDto[];
    isLoading: boolean;
    onCreate: () => void;
    onEdit: (row: BoardSvgSourceDto) => void;
    onDelete: (row: BoardSvgSourceDto) => void;
    dateFormatter: Intl.DateTimeFormat;
}) {
    const { t } = useTranslation();
    const { sources, isLoading, onCreate, onEdit, onDelete, dateFormatter } =
        props;

    return (
        <Card withBorder radius="md" padding="lg">
            <Stack gap="sm">
                <Group justify="space-between" align="center">
                    <Text fw={600}>{t("admin.boardSvgs.sources.heading")}</Text>
                    <Button
                        size="xs"
                        leftSection={<IconPlus size={14} />}
                        onClick={onCreate}
                    >
                        {t("admin.boardSvgs.sources.addButton")}
                    </Button>
                </Group>

                {isLoading ? (
                    <Text c="dimmed">{t("common.loading")}</Text>
                ) : sources.length === 0 ? (
                    <Text c="dimmed">
                        {t("admin.boardSvgs.sources.emptyState")}
                    </Text>
                ) : (
                    <Table striped highlightOnHover withColumnBorders>
                        <Table.Thead>
                            <Table.Tr>
                                <Table.Th>
                                    {t(
                                        "admin.boardSvgs.sources.columns.machineName",
                                    )}
                                </Table.Th>
                                <Table.Th>
                                    {t(
                                        "admin.boardSvgs.sources.columns.uncPath",
                                    )}
                                </Table.Th>
                                <Table.Th>
                                    {t(
                                        "admin.boardSvgs.sources.columns.enabled",
                                    )}
                                </Table.Th>
                                <Table.Th>
                                    {t(
                                        "admin.boardSvgs.sources.columns.lastSynced",
                                    )}
                                </Table.Th>
                                <Table.Th>
                                    {t(
                                        "admin.boardSvgs.sources.columns.lastError",
                                    )}
                                </Table.Th>
                                <Table.Th>
                                    {t(
                                        "admin.boardSvgs.sources.columns.actions",
                                    )}
                                </Table.Th>
                            </Table.Tr>
                        </Table.Thead>
                        <Table.Tbody>
                            {sources.map((row) => (
                                <Table.Tr key={row.id}>
                                    <Table.Td>{row.machineName}</Table.Td>
                                    <Table.Td
                                        style={{ fontFamily: "monospace" }}
                                    >
                                        {row.uncPath}
                                    </Table.Td>
                                    <Table.Td>
                                        <Badge
                                            color={
                                                row.isEnabled ? "green" : "gray"
                                            }
                                            variant="light"
                                        >
                                            {row.isEnabled
                                                ? t(
                                                    "admin.boardSvgs.sources.enabled",
                                                )
                                                : t(
                                                    "admin.boardSvgs.sources.disabled",
                                                )}
                                        </Badge>
                                    </Table.Td>
                                    <Table.Td>
                                        {row.lastSyncedUtc
                                            ? dateFormatter.format(
                                                new Date(row.lastSyncedUtc),
                                            )
                                            : t(
                                                "admin.boardSvgs.sources.never",
                                            )}
                                    </Table.Td>
                                    <Table.Td>
                                        {row.lastSyncError ? (
                                            <Text
                                                size="xs"
                                                c="red"
                                                title={row.lastSyncError}
                                                lineClamp={2}
                                            >
                                                {row.lastSyncError}
                                            </Text>
                                        ) : (
                                            <Text size="xs" c="dimmed">
                                                —
                                            </Text>
                                        )}
                                    </Table.Td>
                                    <Table.Td>
                                        <Group gap={4}>
                                            <Button
                                                size="xs"
                                                variant="default"
                                                leftSection={
                                                    <IconEdit size={14} />
                                                }
                                                onClick={() => onEdit(row)}
                                            >
                                                {t(
                                                    "admin.boardSvgs.sources.actions.edit",
                                                )}
                                            </Button>
                                            <Button
                                                size="xs"
                                                variant="default"
                                                color="red"
                                                leftSection={
                                                    <IconTrash size={14} />
                                                }
                                                onClick={() => onDelete(row)}
                                            >
                                                {t(
                                                    "admin.boardSvgs.sources.actions.delete",
                                                )}
                                            </Button>
                                        </Group>
                                    </Table.Td>
                                </Table.Tr>
                            ))}
                        </Table.Tbody>
                    </Table>
                )}
            </Stack>
        </Card>
    );
}

// ----------------------------------------------------------- Create modal ---

function CreateSourceModal(props: {
    opened: boolean;
    onClose: () => void;
    onSuccess: () => void;
}) {
    const { t } = useTranslation();
    const [error, setError] = useState<ServerErrorInfo | null>(null);
    const form = useForm<CreateBoardSvgSourceRequest>({
        mode: "controlled",
        initialValues: {
            machineName: "",
            uncPath: "",
            isEnabled: true,
        },
        validate: {
            machineName: (v) =>
                v.trim().length === 0
                    ? t("admin.boardSvgs.sources.create.machineNameRequired")
                    : null,
            uncPath: (v) =>
                v.trim().length === 0
                    ? t("admin.boardSvgs.sources.create.uncPathRequired")
                    : null,
        },
    });

    const mutation = useMutation({
        mutationFn: createBoardSvgSource,
        onSuccess: () => {
            setError(null);
            form.reset();
            props.onSuccess();
        },
        onError: (err) => setError(parseServerError(err, "create")),
    });

    const handleClose = () => {
        form.reset();
        setError(null);
        props.onClose();
    };

    return (
        <Modal
            opened={props.opened}
            onClose={handleClose}
            title={t("admin.boardSvgs.sources.create.title")}
            centered
        >
            <form
                onSubmit={form.onSubmit((values) =>
                    mutation.mutate({
                        machineName: values.machineName.trim(),
                        uncPath: values.uncPath.trim(),
                        isEnabled: values.isEnabled,
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
                            {error.detail && (
                                <Text size="sm">{error.detail}</Text>
                            )}
                        </Alert>
                    )}
                    <TextInput
                        label={t(
                            "admin.boardSvgs.sources.create.machineNameLabel",
                        )}
                        placeholder={t(
                            "admin.boardSvgs.sources.create.machineNamePlaceholder",
                        )}
                        required
                        {...form.getInputProps("machineName")}
                    />
                    <TextInput
                        label={t("admin.boardSvgs.sources.create.uncPathLabel")}
                        placeholder={t(
                            "admin.boardSvgs.sources.create.uncPathPlaceholder",
                        )}
                        required
                        {...form.getInputProps("uncPath")}
                    />
                    <Checkbox
                        label={t(
                            "admin.boardSvgs.sources.create.isEnabledLabel",
                        )}
                        description={t(
                            "admin.boardSvgs.sources.create.isEnabledHint",
                        )}
                        {...form.getInputProps("isEnabled", {
                            type: "checkbox",
                        })}
                    />
                    <Group justify="flex-end">
                        <Button variant="default" onClick={handleClose}>
                            {t("admin.boardSvgs.sources.create.cancel")}
                        </Button>
                        <Button type="submit" loading={mutation.isPending}>
                            {t("admin.boardSvgs.sources.create.submit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}

// ------------------------------------------------------------- Edit modal ---

function EditSourceModal(props: {
    source: BoardSvgSourceDto | null;
    onClose: () => void;
    onSuccess: () => void;
}) {
    const { t } = useTranslation();
    const [error, setError] = useState<ServerErrorInfo | null>(null);
    const form = useForm<UpdateBoardSvgSourceRequest>({
        mode: "controlled",
        initialValues: {
            machineName: "",
            uncPath: "",
            isEnabled: true,
        },
        validate: {
            machineName: (v) =>
                v.trim().length === 0
                    ? t("admin.boardSvgs.sources.create.machineNameRequired")
                    : null,
            uncPath: (v) =>
                v.trim().length === 0
                    ? t("admin.boardSvgs.sources.create.uncPathRequired")
                    : null,
        },
    });

    const currentId = props.source?.id;
    useEffect(() => {
        if (props.source) {
            form.setValues({
                machineName: props.source.machineName,
                uncPath: props.source.uncPath,
                isEnabled: props.source.isEnabled,
            });
            setError(null);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [currentId]);

    const mutation = useMutation({
        mutationFn: (values: UpdateBoardSvgSourceRequest) =>
            updateBoardSvgSource(props.source!.id, values),
        onSuccess: () => {
            setError(null);
            props.onSuccess();
        },
        onError: (err) => setError(parseServerError(err, "edit")),
    });

    return (
        <Modal
            opened={props.source !== null}
            onClose={props.onClose}
            title={
                props.source
                    ? `${t("admin.boardSvgs.sources.edit.title")} — ${props.source.machineName}`
                    : t("admin.boardSvgs.sources.edit.title")
            }
            centered
        >
            {props.source && (
                <form
                    onSubmit={form.onSubmit((values) =>
                        mutation.mutate({
                            machineName: values.machineName.trim(),
                            uncPath: values.uncPath.trim(),
                            isEnabled: values.isEnabled,
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
                                {error.detail && (
                                    <Text size="sm">{error.detail}</Text>
                                )}
                            </Alert>
                        )}
                        <TextInput
                            label={t(
                                "admin.boardSvgs.sources.create.machineNameLabel",
                            )}
                            required
                            {...form.getInputProps("machineName")}
                        />
                        <TextInput
                            label={t(
                                "admin.boardSvgs.sources.create.uncPathLabel",
                            )}
                            required
                            {...form.getInputProps("uncPath")}
                        />
                        <Checkbox
                            label={t(
                                "admin.boardSvgs.sources.create.isEnabledLabel",
                            )}
                            description={t(
                                "admin.boardSvgs.sources.create.isEnabledHint",
                            )}
                            {...form.getInputProps("isEnabled", {
                                type: "checkbox",
                            })}
                        />
                        <Group justify="flex-end">
                            <Button variant="default" onClick={props.onClose}>
                                {t("admin.boardSvgs.sources.create.cancel")}
                            </Button>
                            <Button
                                type="submit"
                                loading={mutation.isPending}
                            >
                                {t("admin.boardSvgs.sources.edit.submit")}
                            </Button>
                        </Group>
                    </Stack>
                </form>
            )}
        </Modal>
    );
}

// ----------------------------------------------------------- Delete modal ---

function DeleteSourceModal(props: {
    source: BoardSvgSourceDto | null;
    onClose: () => void;
    onDeleted: () => void;
}) {
    const { t } = useTranslation();
    const [serverError, setServerError] = useState<string | null>(null);
    const mutation = useMutation({
        mutationFn: () => deleteBoardSvgSource(props.source!.id),
        onSuccess: () => {
            setServerError(null);
            props.onDeleted();
        },
        onError: () => {
            setServerError(
                t("admin.boardSvgs.sources.delete.unexpectedError"),
            );
        },
    });

    return (
        <Modal
            opened={props.source !== null}
            onClose={props.onClose}
            title={t("admin.boardSvgs.sources.delete.confirmTitle")}
            centered
        >
            {props.source && (
                <Stack gap="sm">
                    <Text>
                        {t("admin.boardSvgs.sources.delete.confirmBody", {
                            name: props.source.machineName,
                        })}
                    </Text>
                    {serverError && (
                        <Alert
                            role="alert"
                            icon={<IconAlertCircle size={16} />}
                            color="red"
                            variant="light"
                        >
                            {serverError}
                        </Alert>
                    )}
                    <Group justify="flex-end" gap="sm">
                        <Button variant="subtle" onClick={props.onClose}>
                            {t("admin.boardSvgs.sources.delete.cancel")}
                        </Button>
                        <Button
                            color="red"
                            loading={mutation.isPending}
                            onClick={() => mutation.mutate()}
                        >
                            {t("admin.boardSvgs.sources.delete.submit")}
                        </Button>
                    </Group>
                </Stack>
            )}
        </Modal>
    );
}

// ------------------------------------------------------- Sync result modal ---

function SyncResultModal(props: {
    result: BoardSvgSyncResultDto | null;
    onClose: () => void;
    dateFormatter: Intl.DateTimeFormat;
}) {
    const { t } = useTranslation();
    const { result, onClose, dateFormatter } = props;

    return (
        <Modal
            opened={result !== null}
            onClose={onClose}
            title={t("admin.boardSvgs.syncResult.title")}
            centered
            size="lg"
        >
            {result && (
                <Stack gap="sm">
                    <Text size="sm" c="dimmed">
                        {t("admin.boardSvgs.syncResult.startedAt", {
                            when: dateFormatter.format(new Date(result.startedUtc)),
                        })}
                    </Text>
                    <Text size="sm" c="dimmed">
                        {t("admin.boardSvgs.syncResult.completedAt", {
                            when: dateFormatter.format(
                                new Date(result.completedUtc),
                            ),
                        })}
                    </Text>

                    <Text fw={500} mt="xs">
                        {t("admin.boardSvgs.syncResult.sourcesHeading")}
                    </Text>
                    {result.sources.length === 0 ? (
                        <Text c="dimmed" size="sm">
                            {t("admin.boardSvgs.syncResult.empty")}
                        </Text>
                    ) : (
                        <Table striped withColumnBorders>
                            <Table.Thead>
                                <Table.Tr>
                                    <Table.Th>
                                        {t(
                                            "admin.boardSvgs.syncResult.columns.machineName",
                                        )}
                                    </Table.Th>
                                    <Table.Th>
                                        {t(
                                            "admin.boardSvgs.syncResult.columns.reachable",
                                        )}
                                    </Table.Th>
                                    <Table.Th>
                                        {t(
                                            "admin.boardSvgs.syncResult.columns.files",
                                        )}
                                    </Table.Th>
                                    <Table.Th>
                                        {t(
                                            "admin.boardSvgs.syncResult.columns.error",
                                        )}
                                    </Table.Th>
                                </Table.Tr>
                            </Table.Thead>
                            <Table.Tbody>
                                {result.sources.map((s) => (
                                    <Table.Tr key={s.sourceId}>
                                        <Table.Td>{s.machineName}</Table.Td>
                                        <Table.Td>
                                            <Badge
                                                variant="light"
                                                color={
                                                    s.reachable
                                                        ? "green"
                                                        : "red"
                                                }
                                            >
                                                {s.reachable
                                                    ? t(
                                                        "admin.boardSvgs.syncResult.reachable",
                                                    )
                                                    : t(
                                                        "admin.boardSvgs.syncResult.unreachable",
                                                    )}
                                            </Badge>
                                        </Table.Td>
                                        <Table.Td>
                                            {t(
                                                "admin.boardSvgs.syncResult.filesEnumerated",
                                                { count: s.filesEnumerated },
                                            )}
                                        </Table.Td>
                                        <Table.Td>
                                            {s.error ? (
                                                <Text
                                                    size="xs"
                                                    c="red"
                                                    lineClamp={2}
                                                >
                                                    {s.error}
                                                </Text>
                                            ) : (
                                                <Text size="xs" c="dimmed">
                                                    —
                                                </Text>
                                            )}
                                        </Table.Td>
                                    </Table.Tr>
                                ))}
                            </Table.Tbody>
                        </Table>
                    )}

                    <Text fw={500} mt="xs">
                        {t("admin.boardSvgs.syncResult.productsHeading")}
                    </Text>
                    {result.products.length === 0 ? (
                        <Text c="dimmed" size="sm">
                            —
                        </Text>
                    ) : (
                        <Table striped withColumnBorders>
                            <Table.Thead>
                                <Table.Tr>
                                    <Table.Th>
                                        {t(
                                            "admin.boardSvgs.syncResult.columns.product",
                                        )}
                                    </Table.Th>
                                    <Table.Th>
                                        {t(
                                            "admin.boardSvgs.syncResult.columns.outcome",
                                        )}
                                    </Table.Th>
                                    <Table.Th>
                                        {t(
                                            "admin.boardSvgs.syncResult.columns.machineNameProduct",
                                        )}
                                    </Table.Th>
                                    <Table.Th>
                                        {t(
                                            "admin.boardSvgs.syncResult.columns.bytes",
                                        )}
                                    </Table.Th>
                                </Table.Tr>
                            </Table.Thead>
                            <Table.Tbody>
                                {result.products.map((p) => {
                                    const outcome = p.error
                                        ? t(
                                            "admin.boardSvgs.syncResult.error",
                                        )
                                        : p.copied
                                            ? t(
                                                "admin.boardSvgs.syncResult.copied",
                                            )
                                            : t(
                                                "admin.boardSvgs.syncResult.alreadyCached",
                                            );
                                    const outcomeColor = p.error
                                        ? "red"
                                        : p.copied
                                            ? "green"
                                            : "gray";
                                    return (
                                        <Table.Tr key={p.productName}>
                                            <Table.Td>{p.productName}</Table.Td>
                                            <Table.Td>
                                                <Badge
                                                    variant="light"
                                                    color={outcomeColor}
                                                    title={
                                                        p.error ?? undefined
                                                    }
                                                >
                                                    {outcome}
                                                </Badge>
                                            </Table.Td>
                                            <Table.Td>
                                                {p.sourceMachineName ?? (
                                                    <Text size="xs" c="dimmed">
                                                        —
                                                    </Text>
                                                )}
                                            </Table.Td>
                                            <Table.Td>
                                                {p.bytesCopied ?? (
                                                    <Text size="xs" c="dimmed">
                                                        —
                                                    </Text>
                                                )}
                                            </Table.Td>
                                        </Table.Tr>
                                    );
                                })}
                            </Table.Tbody>
                        </Table>
                    )}

                    <Group justify="flex-end">
                        <Button onClick={onClose}>
                            {t("admin.boardSvgs.syncResult.close")}
                        </Button>
                    </Group>
                </Stack>
            )}
        </Modal>
    );
}
