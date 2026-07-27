import { useMemo, useState } from "react";
import {
    ActionIcon,
    Alert,
    Badge,
    Button,
    Card,
    Group,
    Modal,
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
import { IconAlertCircle, IconCopy, IconLock, IconPencil, IconPlus, IconTrash } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    createMyReport,
    addMyReportEntity,
    deleteMyReport,
    duplicateMyReport,
    listMyReports,
} from "../api/authorReports";
import type { ReportDto } from "../api/adminReports";
import {
    REPORT_TEMPLATES,
    DEFAULT_TEMPLATE_ID,
    type ReportTemplate,
} from "../components/reportConfig/reportTemplates";
import { useSessionStore } from "../state/session";
import { relativeFromNow } from "../components/freshness";

/**
 * `/reports` — self-service "My Reports" list for `Author` (and
 * `Admin`) users (docs/phase-2.md §7.6 RC2). Lists the caller's own
 * reports and lets them create a new one from a template, duplicate an
 * existing report into an owned copy, or delete one of their own. All
 * calls go through the owner-scoped {@link authorReports} client.
 */

const MY_REPORTS_QUERY_KEY = ["reports", "mine"] as const;

export function MyReportsRoute() {
    const { t } = useTranslation();
    const user = useSessionStore((s) => s.user);
    const canAuthor =
        (user?.roles.includes("Author") || user?.roles.includes("Admin")) ?? false;
    const queryClient = useQueryClient();

    const reportsQuery = useQuery({
        queryKey: MY_REPORTS_QUERY_KEY,
        queryFn: listMyReports,
        enabled: canAuthor,
    });

    const [createOpen, setCreateOpen] = useState(false);
    const [deleteTarget, setDeleteTarget] = useState<ReportDto | null>(null);

    const invalidate = () => queryClient.invalidateQueries({ queryKey: MY_REPORTS_QUERY_KEY });

    const duplicateMutation = useMutation({
        mutationFn: (report: ReportDto) => duplicateMyReport(report.id, { title: null }),
        onSuccess: () => invalidate(),
    });

    const deleteMutation = useMutation({
        mutationFn: (id: number) => deleteMyReport(id),
        onSuccess: async () => {
            setDeleteTarget(null);
            await invalidate();
        },
    });

    if (!canAuthor) {
        return (
            <Stack gap="md">
                <Title order={2}>{t("myReports.title")}</Title>
                <Alert
                    role="alert"
                    icon={<IconAlertCircle size={16} />}
                    color="red"
                    variant="light"
                >
                    {t("myReports.forbidden")}
                </Alert>
            </Stack>
        );
    }

    const reports = reportsQuery.data ?? [];

    return (
        <Stack gap="lg">
            <Group justify="space-between" align="center">
                <Title order={2}>{t("myReports.title")}</Title>
                <Button
                    leftSection={<IconPlus size={16} />}
                    onClick={() => setCreateOpen(true)}
                >
                    {t("myReports.newReport")}
                </Button>
            </Group>
            <Text c="dimmed" size="sm">
                {t("myReports.subtitle")}
            </Text>

            {reports.length === 0 ? (
                <Card withBorder padding="lg" radius="md">
                    <Text c="dimmed" ta="center">
                        {t("myReports.empty")}
                    </Text>
                </Card>
            ) : (
                <Table.ScrollContainer minWidth={520}>
                    <Table striped highlightOnHover>
                        <Table.Thead>
                            <Table.Tr>
                                <Table.Th>{t("myReports.columns.title")}</Table.Th>
                                <Table.Th>{t("myReports.columns.tiles")}</Table.Th>
                                <Table.Th>{t("myReports.columns.updated")}</Table.Th>
                                <Table.Th />
                            </Table.Tr>
                        </Table.Thead>
                        <Table.Tbody>
                            {reports.map((report) => (
                                <Table.Tr key={report.id} data-testid={`my-report-${report.id}`}>
                                    <Table.Td>
                                        <Group gap="xs">
                                            <Text fw={500}>{report.title}</Text>
                                            {report.isLocked && (
                                                <Tooltip label={t("myReports.locked")}>
                                                    <IconLock size={14} />
                                                </Tooltip>
                                            )}
                                        </Group>
                                        {report.description && (
                                            <Text size="xs" c="dimmed">
                                                {report.description}
                                            </Text>
                                        )}
                                    </Table.Td>
                                    <Table.Td>
                                        <Badge variant="light">{report.entityCount}</Badge>
                                    </Table.Td>
                                    <Table.Td>
                                        <Text size="sm" c="dimmed">
                                            {(() => {
                                                const rel = relativeFromNow(
                                                    new Date(report.lastModifiedUtc),
                                                );
                                                return t(rel.key, rel.params);
                                            })()}
                                        </Text>
                                    </Table.Td>
                                    <Table.Td>
                                        <Group gap="xs" justify="flex-end">
                                            <Button
                                                component={Link}
                                                to={`/reports/${report.id}`}
                                                size="xs"
                                                variant="light"
                                                leftSection={<IconPencil size={14} />}
                                            >
                                                {t("myReports.open")}
                                            </Button>
                                            <Tooltip label={t("myReports.duplicate")}>
                                                <ActionIcon
                                                    variant="subtle"
                                                    aria-label={t("myReports.duplicate")}
                                                    loading={
                                                        duplicateMutation.isPending &&
                                                        duplicateMutation.variables?.id === report.id
                                                    }
                                                    onClick={() => duplicateMutation.mutate(report)}
                                                >
                                                    <IconCopy size={16} />
                                                </ActionIcon>
                                            </Tooltip>
                                            <Tooltip label={t("myReports.delete")}>
                                                <ActionIcon
                                                    variant="subtle"
                                                    color="red"
                                                    aria-label={t("myReports.delete")}
                                                    onClick={() => setDeleteTarget(report)}
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
                </Table.ScrollContainer>
            )}

            <CreateMyReportModal
                open={createOpen}
                onClose={() => setCreateOpen(false)}
                onSaved={async () => {
                    setCreateOpen(false);
                    await invalidate();
                }}
            />

            <Modal
                opened={deleteTarget !== null}
                onClose={() => setDeleteTarget(null)}
                title={t("myReports.deleteConfirmTitle")}
                centered
            >
                <Stack gap="md">
                    <Text>
                        {t("myReports.deleteConfirmBody", { title: deleteTarget?.title ?? "" })}
                    </Text>
                    <Group justify="flex-end" gap="sm">
                        <Button variant="subtle" onClick={() => setDeleteTarget(null)}>
                            {t("myReports.cancel")}
                        </Button>
                        <Button
                            color="red"
                            loading={deleteMutation.isPending}
                            onClick={() => {
                                if (deleteTarget) deleteMutation.mutate(deleteTarget.id);
                            }}
                        >
                            {t("myReports.delete")}
                        </Button>
                    </Group>
                </Stack>
            </Modal>
        </Stack>
    );
}

function CreateMyReportModal(props: {
    open: boolean;
    onClose: () => void;
    onSaved: () => Promise<void> | void;
}) {
    const { t } = useTranslation();
    const tr = t as unknown as (key: string) => string;
    const [serverError, setServerError] = useState<string | null>(null);
    const [templateId, setTemplateId] = useState<string>(DEFAULT_TEMPLATE_ID);

    const form = useForm<{ title: string; description: string }>({
        initialValues: { title: "", description: "" },
        validate: {
            title: (v) =>
                v.trim().length === 0 ? t("myReports.create.titleRequired") : null,
        },
    });

    const mutation = useMutation({
        mutationFn: async (input: { title: string; description: string; template: ReportTemplate }) => {
            const report = await createMyReport({
                title: input.title,
                description: input.description.length > 0 ? input.description : null,
                chromeJson: input.template.chromeJson,
                displayOrder: 0,
            });
            let order = 0;
            for (const tile of input.template.tiles) {
                await addMyReportEntity(report.id, {
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
        onError: () => setServerError(t("myReports.create.unexpectedError")),
    });

    const templateCards = useMemo(
        () =>
            REPORT_TEMPLATES.map((tpl) => {
                const selected = tpl.id === templateId;
                return (
                    <Card
                        key={tpl.id}
                        withBorder
                        padding="sm"
                        radius="md"
                        data-testid={`my-report-template-${tpl.id}`}
                        role="button"
                        tabIndex={0}
                        aria-pressed={selected}
                        onClick={() => {
                            setTemplateId(tpl.id);
                            if (tpl.id !== "blank" && form.values.title.trim().length === 0) {
                                form.setFieldValue("title", tr(tpl.nameKey));
                            }
                        }}
                        style={{
                            cursor: "pointer",
                            borderColor: selected ? "var(--mantine-color-blue-filled)" : undefined,
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
            }),
        // eslint-disable-next-line react-hooks/exhaustive-deps
        [templateId, form.values.title],
    );

    return (
        <Modal
            opened={props.open}
            onClose={() => {
                form.reset();
                setServerError(null);
                setTemplateId(DEFAULT_TEMPLATE_ID);
                props.onClose();
            }}
            title={t("myReports.create.title")}
            centered
        >
            <form
                onSubmit={form.onSubmit((values) => {
                    const template =
                        REPORT_TEMPLATES.find((tpl) => tpl.id === templateId) ??
                        REPORT_TEMPLATES[0];
                    mutation.mutate({
                        title: values.title.trim(),
                        description: values.description.trim(),
                        template,
                    });
                })}
            >
                <Stack gap="sm">
                    <Stack gap={4}>
                        <Text size="sm" fw={500}>
                            {t("myReports.create.templateLabel")}
                        </Text>
                        <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
                            {templateCards}
                        </SimpleGrid>
                    </Stack>
                    <TextInput
                        label={t("myReports.create.titleLabel")}
                        placeholder={t("myReports.create.titlePlaceholder")}
                        withAsterisk
                        data-autofocus
                        {...form.getInputProps("title")}
                    />
                    <Textarea
                        label={t("myReports.create.descriptionLabel")}
                        autosize
                        minRows={2}
                        {...form.getInputProps("description")}
                    />
                    {serverError !== null && (
                        <Alert role="alert" icon={<IconAlertCircle size={16} />} color="red" variant="light">
                            {serverError}
                        </Alert>
                    )}
                    <Group justify="flex-end" gap="sm">
                        <Button
                            variant="subtle"
                            onClick={() => {
                                form.reset();
                                setServerError(null);
                                setTemplateId(DEFAULT_TEMPLATE_ID);
                                props.onClose();
                            }}
                        >
                            {t("myReports.cancel")}
                        </Button>
                        <Button type="submit" loading={mutation.isPending}>
                            {t("myReports.create.submit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}
