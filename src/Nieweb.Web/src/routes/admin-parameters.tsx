import { useEffect, useState } from "react";
import {
    Alert,
    Badge,
    Button,
    Card,
    Code,
    Group,
    Modal,
    Select,
    Stack,
    Table,
    Text,
    Textarea,
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
} from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    APP_PARAMETER_VALUE_TYPES,
    deleteAdminParameter,
    listAdminParameters,
    upsertAdminParameter,
    type AdminParameterDto,
    type AppParameterValueType,
    type UpsertParameterRequest,
} from "../api/adminParameters";
import { ApiError } from "../api/client";
import { useDateTimeFormatter } from "../i18n/formatters";
import { useSessionStore } from "../state/session";

/**
 * Admin-only route for the internal `AppParameter` table (F13 of
 * docs/phase-2.md §7.9). Lists every parameter (system + custom),
 * lets an admin edit any value, create new custom rows, and delete
 * non-system rows. System rows (tolerance intervals, MSA constants,
 * batch.enabled) can be updated but not removed — the DELETE
 * endpoint enforces that with a 409.
 */

const PARAMETERS_QUERY_KEY = ["admin", "parameters"] as const;

type ServerErrorInfo = {
    messageKey:
        | "admin.parameters.upsert.validationFailed"
        | "admin.parameters.upsert.unexpectedError"
        | "admin.parameters.delete.systemProtected"
        | "admin.parameters.delete.unexpectedError";
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

function parseUpsertError(error: unknown): ServerErrorInfo {
    if (error instanceof ApiError && error.status === 400) {
        return {
            messageKey: "admin.parameters.upsert.validationFailed",
            detail: extractValidationDetail(error.body),
        };
    }
    return { messageKey: "admin.parameters.upsert.unexpectedError" };
}

function parseDeleteError(error: unknown): ServerErrorInfo {
    if (error instanceof ApiError && error.status === 409) {
        return {
            messageKey: "admin.parameters.delete.systemProtected",
            detail: error.body || undefined,
        };
    }
    return { messageKey: "admin.parameters.delete.unexpectedError" };
}

export function AdminParametersRoute() {
    const { t } = useTranslation();
    const roles = useSessionStore((s) => s.user?.roles ?? []);
    const isAdmin = roles.includes("Admin");
    const queryClient = useQueryClient();

    const [createOpen, setCreateOpen] = useState(false);
    const [editing, setEditing] = useState<AdminParameterDto | null>(null);
    const [deleting, setDeleting] = useState<AdminParameterDto | null>(null);

    const query = useQuery({
        queryKey: PARAMETERS_QUERY_KEY,
        queryFn: listAdminParameters,
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
                <Title order={2}>{t("admin.parameters.title")}</Title>
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("admin.parameters.forbidden")}
                </Alert>
            </Stack>
        );
    }

    const rows = query.data ?? [];

    return (
        <Stack gap="lg">
            <Group justify="space-between" align="flex-end" wrap="wrap">
                <Stack gap={4}>
                    <Title order={2}>{t("admin.parameters.title")}</Title>
                    <Text c="dimmed" size="sm">
                        {t("admin.parameters.subtitle")}
                    </Text>
                </Stack>
                <Group gap="xs">
                    <Button
                        variant="default"
                        leftSection={<IconRefresh size={16} />}
                        onClick={() => query.refetch()}
                        loading={query.isFetching && !query.isLoading}
                    >
                        {t("admin.parameters.reload")}
                    </Button>
                    <Button
                        leftSection={<IconPlus size={16} />}
                        onClick={() => setCreateOpen(true)}
                    >
                        {t("admin.parameters.createButton")}
                    </Button>
                </Group>
            </Group>

            {query.isError && (
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("admin.parameters.loadError")}
                </Alert>
            )}

            <Card withBorder radius="md" padding="lg">
                {query.isLoading ? (
                    <Text c="dimmed">{t("common.loading")}</Text>
                ) : rows.length === 0 ? (
                    <Text c="dimmed">{t("admin.parameters.emptyState")}</Text>
                ) : (
                    <Table striped highlightOnHover withColumnBorders>
                        <Table.Thead>
                            <Table.Tr>
                                <Table.Th>{t("admin.parameters.columns.key")}</Table.Th>
                                <Table.Th>{t("admin.parameters.columns.valueType")}</Table.Th>
                                <Table.Th>{t("admin.parameters.columns.value")}</Table.Th>
                                <Table.Th>{t("admin.parameters.columns.description")}</Table.Th>
                                <Table.Th>{t("admin.parameters.columns.system")}</Table.Th>
                                <Table.Th>{t("admin.parameters.columns.lastModified")}</Table.Th>
                                <Table.Th>{t("admin.parameters.columns.actions")}</Table.Th>
                            </Table.Tr>
                        </Table.Thead>
                        <Table.Tbody>
                            {rows.map((row) => (
                                <Table.Tr
                                    key={row.key}
                                    data-testid={`admin-parameters-row-${row.key}`}
                                >
                                    <Table.Td>
                                        <Code>{row.key}</Code>
                                    </Table.Td>
                                    <Table.Td>{row.valueType}</Table.Td>
                                    <Table.Td>{row.value}</Table.Td>
                                    <Table.Td>{row.description ?? ""}</Table.Td>
                                    <Table.Td>
                                        {row.isSystem ? (
                                            <Badge color="blue" variant="light">
                                                {t("admin.parameters.system")}
                                            </Badge>
                                        ) : (
                                            <Badge color="gray" variant="light">
                                                {t("admin.parameters.custom")}
                                            </Badge>
                                        )}
                                    </Table.Td>
                                    <Table.Td>
                                        {dateFormatter.format(new Date(row.lastModifiedUtc))}
                                    </Table.Td>
                                    <Table.Td>
                                        <Group gap={4}>
                                            <Button
                                                size="xs"
                                                variant="default"
                                                leftSection={<IconEdit size={14} />}
                                                onClick={() => setEditing(row)}
                                                data-testid={`admin-parameters-edit-${row.key}`}
                                            >
                                                {t("admin.parameters.actions.edit")}
                                            </Button>
                                            {!row.isSystem && (
                                                <Button
                                                    size="xs"
                                                    variant="default"
                                                    color="red"
                                                    leftSection={<IconTrash size={14} />}
                                                    onClick={() => setDeleting(row)}
                                                    data-testid={`admin-parameters-delete-${row.key}`}
                                                >
                                                    {t("admin.parameters.actions.delete")}
                                                </Button>
                                            )}
                                        </Group>
                                    </Table.Td>
                                </Table.Tr>
                            ))}
                        </Table.Tbody>
                    </Table>
                )}
            </Card>

            <UpsertParameterModal
                mode="create"
                param={null}
                opened={createOpen}
                onClose={() => setCreateOpen(false)}
                onSuccess={() => {
                    setCreateOpen(false);
                    void queryClient.invalidateQueries({
                        queryKey: PARAMETERS_QUERY_KEY,
                    });
                }}
            />
            <UpsertParameterModal
                mode="edit"
                param={editing}
                opened={editing !== null}
                onClose={() => setEditing(null)}
                onSuccess={() => {
                    setEditing(null);
                    void queryClient.invalidateQueries({
                        queryKey: PARAMETERS_QUERY_KEY,
                    });
                }}
            />
            <DeleteParameterModal
                param={deleting}
                onClose={() => setDeleting(null)}
                onSuccess={() => {
                    setDeleting(null);
                    void queryClient.invalidateQueries({
                        queryKey: PARAMETERS_QUERY_KEY,
                    });
                }}
            />
        </Stack>
    );
}

// -------------------------------------------------------- Upsert modal ----

type UpsertFormValues = {
    key: string;
    valueType: AppParameterValueType;
    value: string;
    description: string;
};

function UpsertParameterModal(props: {
    mode: "create" | "edit";
    param: AdminParameterDto | null;
    opened: boolean;
    onClose: () => void;
    onSuccess: () => void;
}) {
    const { t } = useTranslation();
    const [error, setError] = useState<ServerErrorInfo | null>(null);

    const form = useForm<UpsertFormValues>({
        mode: "controlled",
        initialValues: {
            key: props.param?.key ?? "",
            valueType: props.param?.valueType ?? "string",
            value: props.param?.value ?? "",
            description: props.param?.description ?? "",
        },
        validate: {
            key: (v) =>
                props.mode === "create" && v.trim().length === 0
                    ? t("admin.parameters.upsert.keyRequired")
                    : null,
            value: (v) =>
                v.length === 0
                    ? t("admin.parameters.upsert.valueRequired")
                    : null,
        },
    });

    // Sync the form when the modal opens for a different row.
    useEffect(() => {
        if (props.opened) {
            form.setValues({
                key: props.param?.key ?? "",
                valueType: props.param?.valueType ?? "string",
                value: props.param?.value ?? "",
                description: props.param?.description ?? "",
            });
            setError(null);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [props.opened, props.param?.key]);

    const mutation = useMutation({
        mutationFn: async (values: UpsertFormValues) => {
            const body: UpsertParameterRequest = {
                valueType: values.valueType,
                value: values.value,
                description:
                    values.description.trim().length === 0
                        ? null
                        : values.description.trim(),
            };
            return upsertAdminParameter(values.key.trim(), body);
        },
        onSuccess: () => {
            setError(null);
            props.onSuccess();
        },
        onError: (err) => setError(parseUpsertError(err)),
    });

    const handleClose = () => {
        setError(null);
        props.onClose();
    };

    return (
        <Modal
            opened={props.opened}
            onClose={handleClose}
            title={
                props.mode === "create"
                    ? t("admin.parameters.upsert.createTitle")
                    : t("admin.parameters.upsert.editTitle")
            }
            centered
        >
            <form
                onSubmit={form.onSubmit((values) => mutation.mutate(values))}
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
                                <Text size="xs" c="dimmed" mt={4}>
                                    {error.detail}
                                </Text>
                            )}
                        </Alert>
                    )}
                    <TextInput
                        label={t("admin.parameters.upsert.keyLabel")}
                        placeholder={t("admin.parameters.upsert.keyPlaceholder")}
                        required={props.mode === "create"}
                        disabled={props.mode === "edit"}
                        data-testid="admin-parameters-key"
                        {...form.getInputProps("key")}
                    />
                    <Select
                        label={t("admin.parameters.upsert.valueTypeLabel")}
                        data={APP_PARAMETER_VALUE_TYPES.map((v) => ({
                            value: v,
                            label: t(`admin.parameters.valueTypes.${v}` as const),
                        }))}
                        allowDeselect={false}
                        data-testid="admin-parameters-valueType"
                        {...form.getInputProps("valueType")}
                    />
                    <TextInput
                        label={t("admin.parameters.upsert.valueLabel")}
                        required
                        data-testid="admin-parameters-value"
                        {...form.getInputProps("value")}
                    />
                    <Textarea
                        label={t("admin.parameters.upsert.descriptionLabel")}
                        placeholder={t("admin.parameters.upsert.descriptionPlaceholder")}
                        autosize
                        minRows={2}
                        maxRows={4}
                        {...form.getInputProps("description")}
                    />
                    <Group justify="flex-end">
                        <Button variant="default" onClick={handleClose}>
                            {t("admin.parameters.upsert.cancel")}
                        </Button>
                        <Button
                            type="submit"
                            loading={mutation.isPending}
                            data-testid="admin-parameters-submit"
                        >
                            {t("admin.parameters.upsert.submit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}

// -------------------------------------------------------- Delete modal ----

function DeleteParameterModal(props: {
    param: AdminParameterDto | null;
    onClose: () => void;
    onSuccess: () => void;
}) {
    const { t } = useTranslation();
    const [error, setError] = useState<ServerErrorInfo | null>(null);

    const mutation = useMutation({
        mutationFn: async (key: string) => {
            await deleteAdminParameter(key);
        },
        onSuccess: () => {
            setError(null);
            props.onSuccess();
        },
        onError: (err) => setError(parseDeleteError(err)),
    });

    const handleClose = () => {
        setError(null);
        props.onClose();
    };

    if (props.param === null) {
        return null;
    }

    const key = props.param.key;

    return (
        <Modal
            opened={props.param !== null}
            onClose={handleClose}
            title={t("admin.parameters.delete.title")}
            centered
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
                            <Text size="xs" c="dimmed" mt={4}>
                                {error.detail}
                            </Text>
                        )}
                    </Alert>
                )}
                <Text>
                    {t("admin.parameters.delete.confirm", { key })}
                </Text>
                <Group justify="flex-end">
                    <Button variant="default" onClick={handleClose}>
                        {t("admin.parameters.delete.cancel")}
                    </Button>
                    <Button
                        color="red"
                        onClick={() => mutation.mutate(key)}
                        loading={mutation.isPending}
                        data-testid="admin-parameters-delete-submit"
                    >
                        {t("admin.parameters.delete.submit")}
                    </Button>
                </Group>
            </Stack>
        </Modal>
    );
}
