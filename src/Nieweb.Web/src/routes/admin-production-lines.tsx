import { useEffect, useState, Fragment } from "react";
import {
    Alert,
    Badge,
    Button,
    Card,
    Group,
    Modal,
    NumberInput,
    Select,
    Stack,
    Table,
    Text,
    TextInput,
    Title,
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import {
    IconAlertCircle,
    IconChevronDown,
    IconChevronRight,
    IconEdit,
    IconPlus,
    IconRefresh,
    IconTrash,
} from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    addProductionLineMachine,
    createProductionLine,
    deleteProductionLine,
    getProductionLine,
    listProductionLines,
    removeProductionLineMachine,
    updateProductionLine,
    type AddMachineRequest,
    type ProductionLineDetailDto,
    type ProductionLineDto,
} from "../api/adminProductionLines";
import { fetchMachines, fetchSources, type SourceInfo } from "../api/sources";
import { ApiError } from "../api/client";
import { useSessionStore } from "../state/session";

/**
 * Admin-only route for production lines and their machine assignments
 * (F13 of docs/phase-2.md §7.9, backed by PL1). Shows every line in a
 * table; expanding a row lists its assigned machines and lets an admin
 * add / remove them. Lines themselves can be created, renamed,
 * reordered, or deleted.
 *
 * A physical machine (`sourceId` + Superviseur `MACHINE_ID`) is unique
 * across all lines — the server returns HTTP 409 if the admin tries to
 * assign it a second time.
 */

const LINES_QUERY_KEY = ["admin", "production-lines"] as const;
const SOURCES_QUERY_KEY = ["admin", "production-lines", "sources"] as const;

type ServerErrorInfo = {
    key: string;
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

function parseLineError(
    error: unknown,
    kind: "create" | "edit" | "delete",
): ServerErrorInfo {
    if (error instanceof ApiError) {
        if (error.status === 409) {
            return {
                key: `admin.productionLines.line.${kind}.conflict`,
                detail: error.body || undefined,
            };
        }
        if (error.status === 400) {
            return {
                key: `admin.productionLines.line.${kind}.validationFailed`,
                detail: extractValidationDetail(error.body),
            };
        }
    }
    return { key: `admin.productionLines.line.${kind}.unexpectedError` };
}

function parseMachineError(
    error: unknown,
    kind: "add" | "remove",
): ServerErrorInfo {
    if (error instanceof ApiError) {
        if (error.status === 409) {
            return {
                key: `admin.productionLines.machine.${kind}.conflict`,
                detail: error.body || undefined,
            };
        }
        if (error.status === 400) {
            return {
                key: `admin.productionLines.machine.${kind}.validationFailed`,
                detail: extractValidationDetail(error.body),
            };
        }
    }
    return { key: `admin.productionLines.machine.${kind}.unexpectedError` };
}

export function AdminProductionLinesRoute() {
    const { t } = useTranslation();
    const roles = useSessionStore((s) => s.user?.roles ?? []);
    const isAdmin = roles.includes("Admin");
    const queryClient = useQueryClient();

    const [createOpen, setCreateOpen] = useState(false);
    const [editing, setEditing] = useState<ProductionLineDto | null>(null);
    const [deleting, setDeleting] = useState<ProductionLineDto | null>(null);
    const [expandedId, setExpandedId] = useState<number | null>(null);

    const linesQuery = useQuery({
        queryKey: LINES_QUERY_KEY,
        queryFn: listProductionLines,
        enabled: isAdmin,
        refetchOnWindowFocus: false,
    });

    if (!isAdmin) {
        return (
            <Stack gap="md">
                <Title order={2}>{t("admin.productionLines.title")}</Title>
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("admin.productionLines.forbidden")}
                </Alert>
            </Stack>
        );
    }

    const rows = linesQuery.data ?? [];

    return (
        <Stack gap="lg">
            <Group justify="space-between" align="flex-end" wrap="wrap">
                <Stack gap={4}>
                    <Title order={2}>{t("admin.productionLines.title")}</Title>
                    <Text c="dimmed" size="sm">
                        {t("admin.productionLines.subtitle")}
                    </Text>
                </Stack>
                <Group gap="xs">
                    <Button
                        variant="default"
                        leftSection={<IconRefresh size={16} />}
                        onClick={() => linesQuery.refetch()}
                        loading={linesQuery.isFetching && !linesQuery.isLoading}
                    >
                        {t("admin.productionLines.reload")}
                    </Button>
                    <Button
                        leftSection={<IconPlus size={16} />}
                        onClick={() => setCreateOpen(true)}
                    >
                        {t("admin.productionLines.createButton")}
                    </Button>
                </Group>
            </Group>

            {linesQuery.isError && (
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("admin.productionLines.loadError")}
                </Alert>
            )}

            <Card withBorder radius="md" padding="lg">
                {linesQuery.isLoading ? (
                    <Text c="dimmed">{t("common.loading")}</Text>
                ) : rows.length === 0 ? (
                    <Text c="dimmed">{t("admin.productionLines.emptyState")}</Text>
                ) : (
                    <Table striped highlightOnHover withColumnBorders>
                        <Table.Thead>
                            <Table.Tr>
                                <Table.Th style={{ width: 40 }}></Table.Th>
                                <Table.Th>{t("admin.productionLines.columns.name")}</Table.Th>
                                <Table.Th style={{ width: 140 }}>
                                    {t("admin.productionLines.columns.displayOrder")}
                                </Table.Th>
                                <Table.Th style={{ width: 140 }}>
                                    {t("admin.productionLines.columns.machineCount")}
                                </Table.Th>
                                <Table.Th style={{ width: 220 }}>
                                    {t("admin.productionLines.columns.actions")}
                                </Table.Th>
                            </Table.Tr>
                        </Table.Thead>
                        <Table.Tbody>
                            {rows.map((row) => {
                                const isOpen = expandedId === row.id;
                                return (
                                    <Fragment key={row.id}>
                                        <Table.Tr
                                            data-testid={`admin-production-lines-row-${row.id}`}
                                        >
                                            <Table.Td>
                                                <Button
                                                    size="xs"
                                                    variant="subtle"
                                                    onClick={() =>
                                                        setExpandedId(isOpen ? null : row.id)
                                                    }
                                                    aria-label={t(
                                                        isOpen
                                                            ? "admin.productionLines.actions.collapse"
                                                            : "admin.productionLines.actions.expand",
                                                    )}
                                                    data-testid={`admin-production-lines-expand-${row.id}`}
                                                >
                                                    {isOpen ? (
                                                        <IconChevronDown size={16} />
                                                    ) : (
                                                        <IconChevronRight size={16} />
                                                    )}
                                                </Button>
                                            </Table.Td>
                                            <Table.Td>{row.name}</Table.Td>
                                            <Table.Td>{row.displayOrder}</Table.Td>
                                            <Table.Td>
                                                <Badge variant="light">{row.machineCount}</Badge>
                                            </Table.Td>
                                            <Table.Td>
                                                <Group gap={4}>
                                                    <Button
                                                        size="xs"
                                                        variant="default"
                                                        leftSection={<IconEdit size={14} />}
                                                        onClick={() => setEditing(row)}
                                                        data-testid={`admin-production-lines-edit-${row.id}`}
                                                    >
                                                        {t("admin.productionLines.actions.edit")}
                                                    </Button>
                                                    <Button
                                                        size="xs"
                                                        variant="default"
                                                        color="red"
                                                        leftSection={<IconTrash size={14} />}
                                                        onClick={() => setDeleting(row)}
                                                        data-testid={`admin-production-lines-delete-${row.id}`}
                                                    >
                                                        {t("admin.productionLines.actions.delete")}
                                                    </Button>
                                                </Group>
                                            </Table.Td>
                                        </Table.Tr>
                                        {isOpen && (
                                            <Table.Tr>
                                                <Table.Td colSpan={5}>
                                                    <MachinesPanel
                                                        lineId={row.id}
                                                        onChanged={() => {
                                                            void queryClient.invalidateQueries({
                                                                queryKey: LINES_QUERY_KEY,
                                                            });
                                                        }}
                                                    />
                                                </Table.Td>
                                            </Table.Tr>
                                        )}
                                    </Fragment>
                                );
                            })}
                        </Table.Tbody>
                    </Table>
                )}
            </Card>

            <UpsertLineModal
                mode="create"
                line={null}
                opened={createOpen}
                onClose={() => setCreateOpen(false)}
                onSuccess={() => {
                    setCreateOpen(false);
                    void queryClient.invalidateQueries({ queryKey: LINES_QUERY_KEY });
                }}
            />
            <UpsertLineModal
                mode="edit"
                line={editing}
                opened={editing !== null}
                onClose={() => setEditing(null)}
                onSuccess={() => {
                    setEditing(null);
                    void queryClient.invalidateQueries({ queryKey: LINES_QUERY_KEY });
                }}
            />
            <DeleteLineModal
                line={deleting}
                onClose={() => setDeleting(null)}
                onSuccess={() => {
                    setDeleting(null);
                    if (expandedId === deleting?.id) {
                        setExpandedId(null);
                    }
                    void queryClient.invalidateQueries({ queryKey: LINES_QUERY_KEY });
                }}
            />
        </Stack>
    );
}

// -------------------------------------------------------- Line upsert ----

type LineFormValues = {
    name: string;
    displayOrder: number;
};

function UpsertLineModal(props: {
    mode: "create" | "edit";
    line: ProductionLineDto | null;
    opened: boolean;
    onClose: () => void;
    onSuccess: () => void;
}) {
    const { t } = useTranslation();
    const [error, setError] = useState<ServerErrorInfo | null>(null);

    const form = useForm<LineFormValues>({
        mode: "controlled",
        initialValues: {
            name: props.line?.name ?? "",
            displayOrder: props.line?.displayOrder ?? 0,
        },
        validate: {
            name: (v) =>
                v.trim().length === 0
                    ? t("admin.productionLines.line.nameRequired")
                    : null,
        },
    });

    useEffect(() => {
        if (props.opened) {
            form.setValues({
                name: props.line?.name ?? "",
                displayOrder: props.line?.displayOrder ?? 0,
            });
            setError(null);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [props.opened, props.line?.id]);

    const mutation = useMutation({
        mutationFn: async (values: LineFormValues) => {
            const payload = {
                name: values.name.trim(),
                displayOrder: values.displayOrder,
            };
            if (props.mode === "create") {
                return createProductionLine(payload);
            }
            return updateProductionLine(props.line!.id, payload);
        },
        onSuccess: () => {
            setError(null);
            props.onSuccess();
        },
        onError: (err) => setError(parseLineError(err, props.mode)),
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
                    ? t("admin.productionLines.line.create.title")
                    : t("admin.productionLines.line.edit.title")
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
                            <Text>{t(error.key as never)}</Text>
                            {error.detail && (
                                <Text size="xs" c="dimmed" mt={4}>
                                    {error.detail}
                                </Text>
                            )}
                        </Alert>
                    )}
                    <TextInput
                        label={t("admin.productionLines.line.nameLabel")}
                        placeholder={t("admin.productionLines.line.namePlaceholder")}
                        required
                        data-testid="admin-production-lines-name"
                        {...form.getInputProps("name")}
                    />
                    <NumberInput
                        label={t("admin.productionLines.line.displayOrderLabel")}
                        min={0}
                        {...form.getInputProps("displayOrder")}
                    />
                    <Group justify="flex-end">
                        <Button variant="default" onClick={handleClose}>
                            {t("admin.productionLines.line.cancel")}
                        </Button>
                        <Button
                            type="submit"
                            loading={mutation.isPending}
                            data-testid="admin-production-lines-submit"
                        >
                            {t("admin.productionLines.line.submit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}

// -------------------------------------------------------- Line delete ----

function DeleteLineModal(props: {
    line: ProductionLineDto | null;
    onClose: () => void;
    onSuccess: () => void;
}) {
    const { t } = useTranslation();
    const [error, setError] = useState<ServerErrorInfo | null>(null);

    const mutation = useMutation({
        mutationFn: async (id: number) => {
            await deleteProductionLine(id);
        },
        onSuccess: () => {
            setError(null);
            props.onSuccess();
        },
        onError: (err) => setError(parseLineError(err, "delete")),
    });

    const handleClose = () => {
        setError(null);
        props.onClose();
    };

    if (props.line === null) {
        return null;
    }

    return (
        <Modal
            opened={props.line !== null}
            onClose={handleClose}
            title={t("admin.productionLines.line.delete.title")}
            centered
        >
            <Stack gap="md">
                {error && (
                    <Alert
                        color="red"
                        icon={<IconAlertCircle size={18} />}
                        role="alert"
                    >
                        <Text>{t(error.key as never)}</Text>
                        {error.detail && (
                            <Text size="xs" c="dimmed" mt={4}>
                                {error.detail}
                            </Text>
                        )}
                    </Alert>
                )}
                <Text>
                    {t("admin.productionLines.line.delete.confirm", {
                        name: props.line.name,
                    })}
                </Text>
                <Group justify="flex-end">
                    <Button variant="default" onClick={handleClose}>
                        {t("admin.productionLines.line.cancel")}
                    </Button>
                    <Button
                        color="red"
                        onClick={() => mutation.mutate(props.line!.id)}
                        loading={mutation.isPending}
                        data-testid="admin-production-lines-delete-submit"
                    >
                        {t("admin.productionLines.line.delete.submit")}
                    </Button>
                </Group>
            </Stack>
        </Modal>
    );
}

// -------------------------------------------------------- Machines panel ----

const LINE_DETAIL_QUERY_KEY = (lineId: number) =>
    ["admin", "production-lines", "detail", lineId] as const;

function MachinesPanel(props: {
    lineId: number;
    onChanged: () => void;
}) {
    const { t } = useTranslation();
    const queryClient = useQueryClient();
    const [addOpen, setAddOpen] = useState(false);
    const [removeError, setRemoveError] = useState<ServerErrorInfo | null>(null);

    const detailQuery = useQuery({
        queryKey: LINE_DETAIL_QUERY_KEY(props.lineId),
        queryFn: () => getProductionLine(props.lineId),
        refetchOnWindowFocus: false,
    });

    const removeMutation = useMutation({
        mutationFn: (assignmentId: number) =>
            removeProductionLineMachine(props.lineId, assignmentId),
        onSuccess: () => {
            setRemoveError(null);
            void queryClient.invalidateQueries({
                queryKey: LINE_DETAIL_QUERY_KEY(props.lineId),
            });
            props.onChanged();
        },
        onError: (err) => setRemoveError(parseMachineError(err, "remove")),
    });

    const detail: ProductionLineDetailDto | undefined = detailQuery.data;

    return (
        <Stack gap="sm">
            <Group justify="space-between">
                <Text fw={500}>
                    {t("admin.productionLines.machine.heading")}
                </Text>
                <Button
                    size="xs"
                    leftSection={<IconPlus size={14} />}
                    onClick={() => setAddOpen(true)}
                    data-testid={`admin-production-lines-add-machine-${props.lineId}`}
                >
                    {t("admin.productionLines.machine.addButton")}
                </Button>
            </Group>

            {removeError && (
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                    withCloseButton
                    onClose={() => setRemoveError(null)}
                >
                    <Text>{t(removeError.key as never)}</Text>
                    {removeError.detail && (
                        <Text size="xs" c="dimmed" mt={4}>
                            {removeError.detail}
                        </Text>
                    )}
                </Alert>
            )}

            {detailQuery.isLoading ? (
                <Text c="dimmed" size="sm">{t("common.loading")}</Text>
            ) : detailQuery.isError ? (
                <Alert color="red" icon={<IconAlertCircle size={16} />}>
                    {t("admin.productionLines.machine.loadError")}
                </Alert>
            ) : detail && detail.machines.length === 0 ? (
                <Text c="dimmed" size="sm">
                    {t("admin.productionLines.machine.empty")}
                </Text>
            ) : detail ? (
                <Table striped withColumnBorders>
                    <Table.Thead>
                        <Table.Tr>
                            <Table.Th>{t("admin.productionLines.machine.columns.source")}</Table.Th>
                            <Table.Th>{t("admin.productionLines.machine.columns.machineId")}</Table.Th>
                            <Table.Th>{t("admin.productionLines.machine.columns.name")}</Table.Th>
                            <Table.Th>{t("admin.productionLines.machine.columns.category")}</Table.Th>
                            <Table.Th>{t("admin.productionLines.machine.columns.actions")}</Table.Th>
                        </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                        {detail.machines.map((m) => (
                            <Table.Tr
                                key={m.id}
                                data-testid={`admin-production-lines-machine-${m.id}`}
                            >
                                <Table.Td>{m.sourceId}</Table.Td>
                                <Table.Td>{m.machineId}</Table.Td>
                                <Table.Td>{m.machineName}</Table.Td>
                                <Table.Td>{m.category ?? ""}</Table.Td>
                                <Table.Td>
                                    <Button
                                        size="xs"
                                        variant="default"
                                        color="red"
                                        leftSection={<IconTrash size={14} />}
                                        onClick={() => removeMutation.mutate(m.id)}
                                        loading={
                                            removeMutation.isPending &&
                                            removeMutation.variables === m.id
                                        }
                                        data-testid={`admin-production-lines-remove-machine-${m.id}`}
                                    >
                                        {t("admin.productionLines.machine.actions.remove")}
                                    </Button>
                                </Table.Td>
                            </Table.Tr>
                        ))}
                    </Table.Tbody>
                </Table>
            ) : null}

            <AddMachineModal
                lineId={props.lineId}
                opened={addOpen}
                onClose={() => setAddOpen(false)}
                onSuccess={() => {
                    setAddOpen(false);
                    void queryClient.invalidateQueries({
                        queryKey: LINE_DETAIL_QUERY_KEY(props.lineId),
                    });
                    props.onChanged();
                }}
            />
        </Stack>
    );
}

// -------------------------------------------------------- Add machine ----

type MachineFormValues = {
    sourceId: string;
    machineId: string;
    machineName: string;
    category: string;
    displayOrder: number;
};

function AddMachineModal(props: {
    lineId: number;
    opened: boolean;
    onClose: () => void;
    onSuccess: () => void;
}) {
    const { t } = useTranslation();
    const [error, setError] = useState<ServerErrorInfo | null>(null);

    const sourcesQuery = useQuery({
        queryKey: SOURCES_QUERY_KEY,
        queryFn: fetchSources,
        enabled: props.opened,
        refetchOnWindowFocus: false,
    });

    const form = useForm<MachineFormValues>({
        mode: "controlled",
        initialValues: {
            sourceId: "",
            machineId: "",
            machineName: "",
            category: "",
            displayOrder: 0,
        },
        validate: {
            sourceId: (v) =>
                v.trim().length === 0
                    ? t("admin.productionLines.machine.add.sourceRequired")
                    : null,
            machineId: (v) => {
                const n = Number.parseInt(v, 10);
                if (!Number.isFinite(n) || n <= 0) {
                    return t("admin.productionLines.machine.add.machineIdRequired");
                }
                return null;
            },
            machineName: (v) =>
                v.trim().length === 0
                    ? t("admin.productionLines.machine.add.nameRequired")
                    : null,
        },
    });

    // When the source changes, fetch its machines so the admin can pick
    // one from a dropdown instead of typing a raw MACHINE_ID.
    const sources: SourceInfo[] = sourcesQuery.data ?? [];
    const machinesQueries = useQueries({
        queries: sources.map((s) => ({
            queryKey: ["admin", "production-lines", "machines", s.id] as const,
            queryFn: () => fetchMachines(s.id),
            enabled: props.opened && form.values.sourceId === s.id,
            refetchOnWindowFocus: false,
        })),
    });
    const selectedIndex = sources.findIndex((s) => s.id === form.values.sourceId);
    const machinesForSource =
        selectedIndex >= 0 ? machinesQueries[selectedIndex]?.data ?? [] : [];

    useEffect(() => {
        if (props.opened) {
            form.reset();
            setError(null);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [props.opened]);

    const mutation = useMutation({
        mutationFn: async (values: MachineFormValues) => {
            const payload: AddMachineRequest = {
                sourceId: values.sourceId.trim(),
                machineId: Number.parseInt(values.machineId, 10),
                machineName: values.machineName.trim(),
                category:
                    values.category.trim().length === 0
                        ? null
                        : values.category.trim(),
                displayOrder: values.displayOrder,
            };
            return addProductionLineMachine(props.lineId, payload);
        },
        onSuccess: () => {
            setError(null);
            props.onSuccess();
        },
        onError: (err) => setError(parseMachineError(err, "add")),
    });

    const handleClose = () => {
        setError(null);
        props.onClose();
    };

    return (
        <Modal
            opened={props.opened}
            onClose={handleClose}
            title={t("admin.productionLines.machine.add.title")}
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
                            <Text>{t(error.key as never)}</Text>
                            {error.detail && (
                                <Text size="xs" c="dimmed" mt={4}>
                                    {error.detail}
                                </Text>
                            )}
                        </Alert>
                    )}
                    <Select
                        label={t("admin.productionLines.machine.add.sourceLabel")}
                        data={sources.map((s) => ({
                            value: s.id,
                            label: s.displayName,
                        }))}
                        data-testid="admin-production-lines-add-source"
                        {...form.getInputProps("sourceId")}
                        onChange={(v) => {
                            form.setFieldValue("sourceId", v ?? "");
                            // Reset the machine pick so we don't
                            // accidentally submit a stale MACHINE_ID.
                            form.setFieldValue("machineId", "");
                            form.setFieldValue("machineName", "");
                        }}
                    />
                    <Select
                        label={t("admin.productionLines.machine.add.machinePickLabel")}
                        placeholder={t(
                            "admin.productionLines.machine.add.machinePickPlaceholder",
                        )}
                        data={machinesForSource.map((m) => ({
                            value: m.id.toString(),
                            label: `${m.name} (${m.typeName})`,
                        }))}
                        disabled={
                            form.values.sourceId.length === 0 ||
                            machinesForSource.length === 0
                        }
                        clearable
                        data-testid="admin-production-lines-add-machine-pick"
                        value={form.values.machineId || null}
                        onChange={(v) => {
                            if (v) {
                                form.setFieldValue("machineId", v);
                                const found = machinesForSource.find(
                                    (m) => m.id.toString() === v,
                                );
                                if (found) {
                                    form.setFieldValue("machineName", found.name);
                                    form.setFieldValue("category", found.typeName);
                                }
                            } else {
                                form.setFieldValue("machineId", "");
                            }
                        }}
                    />
                    <TextInput
                        label={t("admin.productionLines.machine.add.machineIdLabel")}
                        placeholder={t(
                            "admin.productionLines.machine.add.machineIdPlaceholder",
                        )}
                        required
                        data-testid="admin-production-lines-add-machineId"
                        {...form.getInputProps("machineId")}
                    />
                    <TextInput
                        label={t("admin.productionLines.machine.add.nameLabel")}
                        required
                        data-testid="admin-production-lines-add-name"
                        {...form.getInputProps("machineName")}
                    />
                    <TextInput
                        label={t("admin.productionLines.machine.add.categoryLabel")}
                        placeholder={t(
                            "admin.productionLines.machine.add.categoryPlaceholder",
                        )}
                        {...form.getInputProps("category")}
                    />
                    <NumberInput
                        label={t("admin.productionLines.machine.add.displayOrderLabel")}
                        min={0}
                        {...form.getInputProps("displayOrder")}
                    />
                    <Group justify="flex-end">
                        <Button variant="default" onClick={handleClose}>
                            {t("admin.productionLines.machine.add.cancel")}
                        </Button>
                        <Button
                            type="submit"
                            loading={mutation.isPending}
                            data-testid="admin-production-lines-add-submit"
                        >
                            {t("admin.productionLines.machine.add.submit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}
