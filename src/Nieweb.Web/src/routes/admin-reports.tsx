import { useMemo, useState } from "react";
import {
    ActionIcon,
    Alert,
    Badge,
    Button,
    Card,
    Group,
    Modal,
    NumberInput,
    Select,
    SimpleGrid,
    Stack,
    Table,
    Text,
    TextInput,
    Textarea,
    Title,
    Tooltip,
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import {
    IconAlertCircle,
    IconCopy,
    IconEdit,
    IconPin,
    IconPinnedOff,
    IconRefresh,
    IconTrash,
} from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    createAdminReport,
    addAdminReportEntity,
    createAdminReportGroup,
    deleteAdminReport,
    deleteAdminReportGroup,
    duplicateAdminReport,
    listAdminReportGroups,
    listAdminReports,
    pinAdminReport,
    unpinAdminReport,
    updateAdminReportGroup,
    type CreateReportRequest,
    type DuplicateReportRequest,
    type GroupRequest,
    type ReportDto,
    type ReportGroupDto,
} from "../api/adminReports";
import { ApiError } from "../api/client";
import { useSessionStore } from "../state/session";
import { relativeFromNow } from "../components/freshness";
import {
    REPORT_TEMPLATES,
    DEFAULT_TEMPLATE_ID,
    type ReportTemplate,
} from "../components/reportConfig/reportTemplates";

/**
 * Admin route `/admin/reports` — the RC2 entry point for
 * composing reports. Lists report groups (with rename / delete)
 * and reports (with edit / delete), and hosts modal forms for
 * creating a new group or a new report.
 *
 * Route-level auth-gating happens in the router's `beforeLoad`;
 * this component defends in depth by rendering a localised
 * "forbidden" alert if the current session lacks the Admin role.
 */

const GROUPS_QUERY_KEY = ["admin", "report-groups"] as const;
const REPORTS_QUERY_KEY = ["admin", "reports"] as const;

export function AdminReportsRoute() {
    const { t } = useTranslation();
    const user = useSessionStore((s) => s.user);
    const isAdmin = user?.roles.includes("Admin") ?? false;
    const queryClient = useQueryClient();

    const groupsQuery = useQuery({
        queryKey: GROUPS_QUERY_KEY,
        queryFn: listAdminReportGroups,
        enabled: isAdmin,
    });
    const reportsQuery = useQuery({
        queryKey: REPORTS_QUERY_KEY,
        queryFn: listAdminReports,
        enabled: isAdmin,
    });

    const [createGroupOpen, setCreateGroupOpen] = useState(false);
    const [editGroup, setEditGroup] = useState<ReportGroupDto | null>(null);
    const [deleteGroup, setDeleteGroup] = useState<ReportGroupDto | null>(null);
    const [createReportOpen, setCreateReportOpen] = useState(false);
    const [deleteReport, setDeleteReport] = useState<ReportDto | null>(null);
    const [duplicateReport, setDuplicateReport] = useState<ReportDto | null>(null);

    if (!isAdmin) {
        return (
            <Stack gap="md">
                <Title order={2}>{t("admin.reports.title")}</Title>
                <Alert
                    role="alert"
                    icon={<IconAlertCircle size={16} />}
                    color="red"
                    variant="light"
                >
                    {t("admin.reports.forbidden")}
                </Alert>
            </Stack>
        );
    }

    const invalidateAll = async () => {
        await queryClient.invalidateQueries({ queryKey: GROUPS_QUERY_KEY });
        await queryClient.invalidateQueries({ queryKey: REPORTS_QUERY_KEY });
    };

    // F14: pin / unpin from the reports list without opening the
    // editor. Invalidates the shared home-page query so the pinned
    // grid refreshes in-place.
    const togglePinMutation = useMutation({
        mutationFn: (report: ReportDto) =>
            report.isPinnedHome ? unpinAdminReport(report.id) : pinAdminReport(report.id),
        onSuccess: async () => {
            await queryClient.invalidateQueries({ queryKey: REPORTS_QUERY_KEY });
            await queryClient.invalidateQueries({ queryKey: ["home", "pinned-reports"] });
        },
    });

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Group justify="space-between" align="center">
                    <Title order={2}>{t("admin.reports.title")}</Title>
                    <Tooltip label={t("admin.reports.reload")}>
                        <ActionIcon
                            variant="subtle"
                            aria-label={t("admin.reports.reload")}
                            onClick={() => {
                                void invalidateAll();
                            }}
                        >
                            <IconRefresh size={18} />
                        </ActionIcon>
                    </Tooltip>
                </Group>
                <Text c="dimmed">{t("admin.reports.subtitle")}</Text>
            </Stack>

            {(groupsQuery.isError || reportsQuery.isError) && (
                <Alert
                    role="alert"
                    icon={<IconAlertCircle size={16} />}
                    color="red"
                    variant="light"
                >
                    {t("admin.reports.loadError")}
                </Alert>
            )}

            <GroupsCard
                groups={groupsQuery.data ?? []}
                onCreate={() => setCreateGroupOpen(true)}
                onEdit={(g) => setEditGroup(g)}
                onDelete={(g) => setDeleteGroup(g)}
            />

            <ReportsCard
                reports={reportsQuery.data ?? []}
                onCreate={() => setCreateReportOpen(true)}
                onDelete={(r) => setDeleteReport(r)}
                onDuplicate={(r) => setDuplicateReport(r)}
                onTogglePin={(r) => togglePinMutation.mutate(r)}
                pinPendingId={
                    togglePinMutation.isPending ? (togglePinMutation.variables?.id ?? null) : null
                }
            />

            <CreateGroupModal
                open={createGroupOpen}
                onClose={() => setCreateGroupOpen(false)}
                onSaved={async () => {
                    setCreateGroupOpen(false);
                    await invalidateAll();
                }}
            />
            {editGroup !== null && (
                <EditGroupModal
                    group={editGroup}
                    onClose={() => setEditGroup(null)}
                    onSaved={async () => {
                        setEditGroup(null);
                        await invalidateAll();
                    }}
                />
            )}
            {deleteGroup !== null && (
                <DeleteGroupModal
                    group={deleteGroup}
                    onClose={() => setDeleteGroup(null)}
                    onDeleted={async () => {
                        setDeleteGroup(null);
                        await invalidateAll();
                    }}
                />
            )}
            <CreateReportModal
                open={createReportOpen}
                groups={groupsQuery.data ?? []}
                defaultOwner={user?.displayName ?? user?.email ?? ""}
                onClose={() => setCreateReportOpen(false)}
                onSaved={async () => {
                    setCreateReportOpen(false);
                    await invalidateAll();
                }}
            />
            {deleteReport !== null && (
                <DeleteReportModal
                    report={deleteReport}
                    onClose={() => setDeleteReport(null)}
                    onDeleted={async () => {
                        setDeleteReport(null);
                        await invalidateAll();
                    }}
                />
            )}
            {duplicateReport !== null && (
                <DuplicateReportModal
                    report={duplicateReport}
                    defaultOwner={user?.displayName ?? user?.email ?? ""}
                    onClose={() => setDuplicateReport(null)}
                    onDuplicated={async () => {
                        setDuplicateReport(null);
                        await invalidateAll();
                    }}
                />
            )}
        </Stack>
    );
}

// -------------------- Groups card --------------------

function GroupsCard(props: {
    groups: ReportGroupDto[];
    onCreate: () => void;
    onEdit: (g: ReportGroupDto) => void;
    onDelete: (g: ReportGroupDto) => void;
}) {
    const { t } = useTranslation();
    const { groups, onCreate, onEdit, onDelete } = props;

    return (
        <Card withBorder padding="lg" radius="md">
            <Group justify="space-between" mb="sm">
                <Title order={4}>{t("admin.reports.groups.heading")}</Title>
                <Button size="xs" onClick={onCreate}>
                    {t("admin.reports.groups.createButton")}
                </Button>
            </Group>
            {groups.length === 0 ? (
                <Text c="dimmed" size="sm">
                    {t("admin.reports.groups.emptyState")}
                </Text>
            ) : (
                <Table striped withRowBorders>
                    <Table.Thead>
                        <Table.Tr>
                            <Table.Th>{t("admin.reports.groups.columns.name")}</Table.Th>
                            <Table.Th>{t("admin.reports.groups.columns.displayOrder")}</Table.Th>
                            <Table.Th>{t("admin.reports.groups.columns.reportCount")}</Table.Th>
                            <Table.Th style={{ width: 120 }}>
                                {t("admin.reports.groups.columns.actions")}
                            </Table.Th>
                        </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                        {groups.map((g) => (
                            <Table.Tr key={g.id} data-testid={`report-group-${g.id}`}>
                                <Table.Td>{g.name}</Table.Td>
                                <Table.Td>{g.displayOrder}</Table.Td>
                                <Table.Td>{g.reportCount}</Table.Td>
                                <Table.Td>
                                    <Group gap="xs">
                                        <Tooltip label={t("admin.reports.groups.edit.title")}>
                                            <ActionIcon
                                                variant="subtle"
                                                aria-label={t("admin.reports.groups.edit.title")}
                                                onClick={() => onEdit(g)}
                                            >
                                                <IconEdit size={16} />
                                            </ActionIcon>
                                        </Tooltip>
                                        <Tooltip label={t("admin.reports.groups.delete.confirmTitle")}>
                                            <ActionIcon
                                                variant="subtle"
                                                color="red"
                                                aria-label={t("admin.reports.groups.delete.confirmTitle")}
                                                onClick={() => onDelete(g)}
                                            >
                                                <IconTrash size={16} />
                                            </ActionIcon>
                                        </Tooltip>
                                    </Group>
                                </Table.Td>
                            </Table.Tr>
                        ))}
                    </Table.Tbody>
                </Table>
            )}
        </Card>
    );
}

// -------------------- Reports card --------------------

function ReportsCard(props: {
    reports: ReportDto[];
    onCreate: () => void;
    onDelete: (r: ReportDto) => void;
    onDuplicate: (r: ReportDto) => void;
    onTogglePin: (r: ReportDto) => void;
    pinPendingId: number | null;
}) {
    const { t } = useTranslation();
    const { reports, onCreate, onDelete, onDuplicate, onTogglePin, pinPendingId } = props;

    return (
        <Card withBorder padding="lg" radius="md">
            <Group justify="space-between" mb="sm">
                <Title order={4}>{t("admin.reports.list.heading")}</Title>
                <Button size="xs" onClick={onCreate}>
                    {t("admin.reports.list.createButton")}
                </Button>
            </Group>
            {reports.length === 0 ? (
                <Text c="dimmed" size="sm">
                    {t("admin.reports.list.emptyState")}
                </Text>
            ) : (
                <Table striped withRowBorders highlightOnHover>
                    <Table.Thead>
                        <Table.Tr>
                            <Table.Th>{t("admin.reports.list.columns.title")}</Table.Th>
                            <Table.Th>{t("admin.reports.list.columns.group")}</Table.Th>
                            <Table.Th>{t("admin.reports.list.columns.owner")}</Table.Th>
                            <Table.Th>{t("admin.reports.list.columns.tiles")}</Table.Th>
                            <Table.Th>{t("admin.reports.list.columns.lastModified")}</Table.Th>
                            <Table.Th style={{ width: 140 }}>
                                {t("admin.reports.list.columns.actions")}
                            </Table.Th>
                        </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                        {reports.map((r) => (
                            <Table.Tr key={r.id} data-testid={`report-row-${r.id}`}>
                                <Table.Td>
                                    <Group gap="xs">
                                        <Text fw={500}>{r.title}</Text>
                                        {r.isLocked && (
                                            <Badge size="xs" color="gray" variant="light">
                                                locked
                                            </Badge>
                                        )}
                                        {r.isPinnedHome && (
                                            <Badge size="xs" color="blue" variant="light">
                                                pinned
                                            </Badge>
                                        )}
                                    </Group>
                                </Table.Td>
                                <Table.Td>
                                    {r.groupName ?? (
                                        <Text c="dimmed" size="sm">
                                            {t("admin.reports.groups.unassigned")}
                                        </Text>
                                    )}
                                </Table.Td>
                                <Table.Td>{r.ownerDisplayName}</Table.Td>
                                <Table.Td>{r.entityCount}</Table.Td>
                                <Table.Td>
                                    {(() => {
                                        const rel = relativeFromNow(new Date(r.lastModifiedUtc));
                                        return t(rel.key, rel.params);
                                    })()}
                                </Table.Td>
                                <Table.Td>
                                    <Group gap="xs">
                                        <Button
                                            component={Link}
                                            to={`/admin/reports/${r.id}`}
                                            size="xs"
                                            variant="light"
                                        >
                                            {t("admin.reports.list.actions.edit")}
                                        </Button>
                                        <Tooltip label={t("admin.reports.list.actions.duplicate")}>
                                            <ActionIcon
                                                variant="subtle"
                                                color="blue"
                                                aria-label={t("admin.reports.list.actions.duplicate")}
                                                onClick={() => onDuplicate(r)}
                                            >
                                                <IconCopy size={16} />
                                            </ActionIcon>
                                        </Tooltip>
                                        <Tooltip
                                            label={
                                                r.isPinnedHome
                                                    ? t("admin.reports.list.actions.unpin")
                                                    : t("admin.reports.list.actions.pin")
                                            }
                                        >
                                            <ActionIcon
                                                variant="subtle"
                                                color={r.isPinnedHome ? "yellow" : "gray"}
                                                aria-label={
                                                    r.isPinnedHome
                                                        ? t("admin.reports.list.actions.unpin")
                                                        : t("admin.reports.list.actions.pin")
                                                }
                                                onClick={() => onTogglePin(r)}
                                                loading={pinPendingId === r.id}
                                                data-testid={`report-pin-toggle-${r.id}`}
                                            >
                                                {r.isPinnedHome ? (
                                                    <IconPinnedOff size={16} />
                                                ) : (
                                                    <IconPin size={16} />
                                                )}
                                            </ActionIcon>
                                        </Tooltip>
                                        <Tooltip label={t("admin.reports.list.actions.delete")}>
                                            <ActionIcon
                                                variant="subtle"
                                                color="red"
                                                aria-label={t("admin.reports.list.actions.delete")}
                                                onClick={() => onDelete(r)}
                                            >
                                                <IconTrash size={16} />
                                            </ActionIcon>
                                        </Tooltip>
                                    </Group>
                                </Table.Td>
                            </Table.Tr>
                        ))}
                    </Table.Tbody>
                </Table>
            )}
        </Card>
    );
}

// -------------------- Create group modal --------------------

function CreateGroupModal(props: {
    open: boolean;
    onClose: () => void;
    onSaved: () => Promise<void> | void;
}) {
    const { t } = useTranslation();
    const [serverError, setServerError] = useState<string | null>(null);
    const form = useForm<GroupRequest>({
        initialValues: { name: "", displayOrder: 0 },
        validate: {
            name: (v) =>
                v.trim().length === 0
                    ? t("admin.reports.groups.create.nameRequired")
                    : null,
        },
    });
    const mutation = useMutation({
        mutationFn: (body: GroupRequest) => createAdminReportGroup(body),
        onSuccess: async () => {
            setServerError(null);
            form.reset();
            await props.onSaved();
        },
        onError: (err) => {
            if (err instanceof ApiError && err.status === 409) {
                setServerError(t("admin.reports.groups.create.conflict"));
                return;
            }
            setServerError(t("admin.reports.groups.create.unexpectedError"));
        },
    });

    return (
        <Modal
            opened={props.open}
            onClose={() => {
                form.reset();
                setServerError(null);
                props.onClose();
            }}
            title={t("admin.reports.groups.create.title")}
            centered
        >
            <form
                onSubmit={form.onSubmit((values) => {
                    mutation.mutate({
                        name: values.name.trim(),
                        displayOrder: values.displayOrder,
                    });
                })}
            >
                <Stack gap="sm">
                    <TextInput
                        label={t("admin.reports.groups.create.nameLabel")}
                        placeholder={t("admin.reports.groups.create.namePlaceholder")}
                        withAsterisk
                        data-autofocus
                        {...form.getInputProps("name")}
                    />
                    <NumberInput
                        label={t("admin.reports.groups.create.displayOrderLabel")}
                        min={0}
                        value={form.values.displayOrder}
                        onChange={(v) =>
                            form.setFieldValue(
                                "displayOrder",
                                typeof v === "number" ? v : 0,
                            )
                        }
                    />
                    {serverError !== null && (
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
                        <Button
                            variant="subtle"
                            onClick={() => {
                                form.reset();
                                setServerError(null);
                                props.onClose();
                            }}
                        >
                            {t("admin.reports.groups.create.cancel")}
                        </Button>
                        <Button type="submit" loading={mutation.isPending}>
                            {t("admin.reports.groups.create.submit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}

// -------------------- Edit group modal --------------------

function EditGroupModal(props: {
    group: ReportGroupDto;
    onClose: () => void;
    onSaved: () => Promise<void> | void;
}) {
    const { t } = useTranslation();
    const [serverError, setServerError] = useState<string | null>(null);
    const form = useForm<GroupRequest>({
        initialValues: {
            name: props.group.name,
            displayOrder: props.group.displayOrder,
        },
        validate: {
            name: (v) =>
                v.trim().length === 0
                    ? t("admin.reports.groups.create.nameRequired")
                    : null,
        },
    });
    const mutation = useMutation({
        mutationFn: (body: GroupRequest) => updateAdminReportGroup(props.group.id, body),
        onSuccess: async () => {
            setServerError(null);
            await props.onSaved();
        },
        onError: (err) => {
            if (err instanceof ApiError && err.status === 409) {
                setServerError(t("admin.reports.groups.create.conflict"));
                return;
            }
            setServerError(t("admin.reports.groups.create.unexpectedError"));
        },
    });

    return (
        <Modal
            opened={true}
            onClose={props.onClose}
            title={t("admin.reports.groups.edit.title")}
            centered
        >
            <form
                onSubmit={form.onSubmit((values) => {
                    mutation.mutate({
                        name: values.name.trim(),
                        displayOrder: values.displayOrder,
                    });
                })}
            >
                <Stack gap="sm">
                    <TextInput
                        label={t("admin.reports.groups.create.nameLabel")}
                        withAsterisk
                        {...form.getInputProps("name")}
                    />
                    <NumberInput
                        label={t("admin.reports.groups.create.displayOrderLabel")}
                        min={0}
                        value={form.values.displayOrder}
                        onChange={(v) =>
                            form.setFieldValue(
                                "displayOrder",
                                typeof v === "number" ? v : 0,
                            )
                        }
                    />
                    {serverError !== null && (
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
                            {t("admin.reports.groups.create.cancel")}
                        </Button>
                        <Button type="submit" loading={mutation.isPending}>
                            {t("admin.reports.groups.edit.submit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}

// -------------------- Delete group modal --------------------

function DeleteGroupModal(props: {
    group: ReportGroupDto;
    onClose: () => void;
    onDeleted: () => Promise<void> | void;
}) {
    const { t } = useTranslation();
    const [serverError, setServerError] = useState<string | null>(null);
    const mutation = useMutation({
        mutationFn: () => deleteAdminReportGroup(props.group.id),
        onSuccess: async () => {
            setServerError(null);
            await props.onDeleted();
        },
        onError: () => {
            setServerError(t("admin.reports.groups.delete.unexpectedError"));
        },
    });

    return (
        <Modal
            opened={true}
            onClose={props.onClose}
            title={t("admin.reports.groups.delete.confirmTitle")}
            centered
        >
            <Stack gap="sm">
                <Text>
                    {t("admin.reports.groups.delete.confirmBody", {
                        name: props.group.name,
                    })}
                </Text>
                {serverError !== null && (
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
                        {t("admin.reports.groups.delete.cancel")}
                    </Button>
                    <Button
                        color="red"
                        loading={mutation.isPending}
                        onClick={() => mutation.mutate()}
                    >
                        {t("admin.reports.groups.delete.submit")}
                    </Button>
                </Group>
            </Stack>
        </Modal>
    );
}

// -------------------- Create report modal --------------------

function CreateReportModal(props: {
    open: boolean;
    groups: ReportGroupDto[];
    defaultOwner: string;
    onClose: () => void;
    onSaved: () => Promise<void> | void;
}) {
    const { t } = useTranslation();
    const tr = t as unknown as (key: string) => string;
    const [serverError, setServerError] = useState<string | null>(null);
    const [templateId, setTemplateId] = useState<string>(DEFAULT_TEMPLATE_ID);
    type FormValues = {
        title: string;
        description: string;
        groupId: string | null;
        owner: string;
    };
    const form = useForm<FormValues>({
        initialValues: {
            title: "",
            description: "",
            groupId: null,
            owner: props.defaultOwner,
        },
        validate: {
            title: (v) =>
                v.trim().length === 0
                    ? t("admin.reports.list.create.titleRequired")
                    : null,
            owner: (v) =>
                v.trim().length === 0
                    ? t("admin.reports.list.create.ownerRequired")
                    : null,
        },
    });

    const groupOptions = useMemo(
        () =>
            props.groups.map((g) => ({
                value: String(g.id),
                label: g.name,
            })),
        [props.groups],
    );

    const mutation = useMutation({
        mutationFn: async (input: {
            body: CreateReportRequest;
            tiles: ReportTemplate["tiles"];
        }) => {
            const report = await createAdminReport(input.body);
            // Expand the chosen template's tiles onto the fresh report.
            let order = 0;
            for (const tile of input.tiles) {
                await addAdminReportEntity(report.id, {
                    tileType: tile.tileType,
                    title: null,
                    displayOrder: order++,
                    configJson: tile.configJson,
                });
            }
            return report;
        },
        onSuccess: async () => {
            setServerError(null);
            form.reset();
            setTemplateId(DEFAULT_TEMPLATE_ID);
            await props.onSaved();
        },
        onError: () => {
            setServerError(t("admin.reports.list.create.unexpectedError"));
        },
    });

    return (
        <Modal
            opened={props.open}
            onClose={() => {
                form.reset();
                setServerError(null);
                setTemplateId(DEFAULT_TEMPLATE_ID);
                props.onClose();
            }}
            title={t("admin.reports.list.create.title")}
            centered
        >
            <form
                onSubmit={form.onSubmit((values) => {
                    const template =
                        REPORT_TEMPLATES.find((tpl) => tpl.id === templateId) ??
                        REPORT_TEMPLATES[0];
                    const desc = values.description.trim();
                    mutation.mutate({
                        body: {
                            title: values.title.trim(),
                            description: desc.length > 0 ? desc : null,
                            reportGroupId:
                                values.groupId !== null
                                    ? Number(values.groupId)
                                    : null,
                            ownerDisplayName: values.owner.trim(),
                            isLocked: false,
                            isPinnedHome: false,
                            displayOrder: 0,
                            chromeJson: template.chromeJson,
                        },
                        tiles: template.tiles,
                    });
                })}
            >
                <Stack gap="sm">
                    <Stack gap={4}>
                        <Text size="sm" fw={500}>
                            {t("admin.reports.list.create.template.label")}
                        </Text>
                        <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
                            {REPORT_TEMPLATES.map((tpl) => {
                                const selected = tpl.id === templateId;
                                return (
                                    <Card
                                        key={tpl.id}
                                        withBorder
                                        padding="sm"
                                        radius="md"
                                        data-testid={`report-template-${tpl.id}`}
                                        role="button"
                                        tabIndex={0}
                                        aria-pressed={selected}
                                        onClick={() => {
                                            setTemplateId(tpl.id);
                                            if (
                                                tpl.id !== "blank" &&
                                                form.values.title.trim().length === 0
                                            ) {
                                                form.setFieldValue("title", tr(tpl.nameKey));
                                            }
                                        }}
                                        style={{
                                            cursor: "pointer",
                                            borderColor: selected
                                                ? "var(--mantine-color-blue-filled)"
                                                : undefined,
                                            borderWidth: selected ? 2 : undefined,
                                        }}
                                    >
                                        <Text fw={600} size="sm">
                                            {tr(tpl.nameKey)}
                                        </Text>
                                        <Text size="xs" c="dimmed">
                                            {tr(tpl.descKey)}
                                        </Text>
                                    </Card>
                                );
                            })}
                        </SimpleGrid>
                    </Stack>
                    <TextInput
                        label={t("admin.reports.list.create.titleLabel")}
                        placeholder={t("admin.reports.list.create.titlePlaceholder")}
                        withAsterisk
                        data-autofocus
                        {...form.getInputProps("title")}
                    />
                    <Textarea
                        label={t("admin.reports.list.create.descriptionLabel")}
                        placeholder={t("admin.reports.list.create.descriptionPlaceholder")}
                        autosize
                        minRows={2}
                        {...form.getInputProps("description")}
                    />
                    <Select
                        label={t("admin.reports.list.create.groupLabel")}
                        placeholder={t("admin.reports.list.create.groupPlaceholder")}
                        data={groupOptions}
                        clearable
                        {...form.getInputProps("groupId")}
                    />
                    <TextInput
                        label={t("admin.reports.list.create.ownerLabel")}
                        placeholder={t("admin.reports.list.create.ownerPlaceholder")}
                        withAsterisk
                        {...form.getInputProps("owner")}
                    />
                    {serverError !== null && (
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
                        <Button
                            variant="subtle"
                            onClick={() => {
                                form.reset();
                                setServerError(null);
                                props.onClose();
                            }}
                        >
                            {t("admin.reports.list.create.cancel")}
                        </Button>
                        <Button type="submit" loading={mutation.isPending}>
                            {t("admin.reports.list.create.submit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}

// -------------------- Delete report modal --------------------

function DeleteReportModal(props: {
    report: ReportDto;
    onClose: () => void;
    onDeleted: () => Promise<void> | void;
}) {
    const { t } = useTranslation();
    const [serverError, setServerError] = useState<string | null>(null);
    const mutation = useMutation({
        mutationFn: () => deleteAdminReport(props.report.id),
        onSuccess: async () => {
            setServerError(null);
            await props.onDeleted();
        },
        onError: () => {
            setServerError(t("admin.reports.list.delete.unexpectedError"));
        },
    });

    return (
        <Modal
            opened={true}
            onClose={props.onClose}
            title={t("admin.reports.list.delete.confirmTitle")}
            centered
        >
            <Stack gap="sm">
                <Text>
                    {t("admin.reports.list.delete.confirmBody", {
                        title: props.report.title,
                    })}
                </Text>
                {serverError !== null && (
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
                        {t("admin.reports.list.delete.cancel")}
                    </Button>
                    <Button
                        color="red"
                        loading={mutation.isPending}
                        onClick={() => mutation.mutate()}
                    >
                        {t("admin.reports.list.delete.submit")}
                    </Button>
                </Group>
            </Stack>
        </Modal>
    );
}

// -------------------- Duplicate report modal (RC3) --------------------

function DuplicateReportModal(props: {
    report: ReportDto;
    defaultOwner: string;
    onClose: () => void;
    onDuplicated: () => Promise<void> | void;
}) {
    const { t } = useTranslation();
    const [serverError, setServerError] = useState<string | null>(null);
    const form = useForm<DuplicateReportRequest>({
        initialValues: {
            title: `Copy of ${props.report.title}`,
            ownerDisplayName: props.defaultOwner,
        },
        validate: {
            ownerDisplayName: (v) =>
                (v ?? "").trim().length === 0
                    ? t("admin.reports.list.duplicate.ownerRequired")
                    : null,
        },
    });
    const mutation = useMutation({
        mutationFn: (body: DuplicateReportRequest) =>
            duplicateAdminReport(props.report.id, body),
        onSuccess: async () => {
            setServerError(null);
            form.reset();
            await props.onDuplicated();
        },
        onError: () => {
            setServerError(t("admin.reports.list.duplicate.unexpectedError"));
        },
    });

    return (
        <Modal
            opened={true}
            onClose={props.onClose}
            title={t("admin.reports.list.duplicate.title")}
            centered
        >
            <form
                onSubmit={form.onSubmit((values) => {
                    mutation.mutate({
                        title: (values.title ?? "").trim(),
                        ownerDisplayName: values.ownerDisplayName.trim(),
                    });
                })}
            >
                <Stack gap="sm">
                    <TextInput
                        label={t("admin.reports.list.duplicate.titleField")}
                        {...form.getInputProps("title")}
                    />
                    <TextInput
                        label={t("admin.reports.list.duplicate.owner")}
                        withAsterisk
                        {...form.getInputProps("ownerDisplayName")}
                    />
                    {serverError !== null && (
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
                            {t("admin.reports.list.duplicate.cancel")}
                        </Button>
                        <Button type="submit" loading={mutation.isPending}>
                            {t("admin.reports.list.duplicate.submit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}
