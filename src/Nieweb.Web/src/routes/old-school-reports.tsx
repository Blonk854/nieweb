import { useState } from "react";
import {
    Alert,
    Button,
    Group,
    Modal,
    Select,
    Stack,
    Text,
    TextInput,
} from "@mantine/core";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate } from "@tanstack/react-router";
import { IconAlertCircle } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    addMyReportEntity,
    createMyReport,
    deleteMyReport,
    duplicateMyReport,
    listMyReports,
} from "../api/authorReports";
import type { ReportDto } from "../api/adminReports";
import {
    DEFAULT_TEMPLATE_ID,
    REPORT_TEMPLATES,
} from "../components/reportConfig/reportTemplates";
import { VwbFrame, oldSchoolStyles as styles } from "../components/oldSchool/VwbFrame";
import { useSessionStore } from "../state/session";

export const OLD_SCHOOL_QUERY_KEY = ["oldSchool", "reports"] as const;

/**
 * `/old-school/reports` — the Vieweb-style "Reports list" screen. Lists
 * the caller's own reports (shared with My Reports) and lets them create
 * a new one from a template, open the layout designer, duplicate, or
 * delete. Author + Admin only.
 */
export function OldSchoolReportsRoute() {
    const { t } = useTranslation();
    const navigate = useNavigate();
    const user = useSessionStore((s) => s.user);
    const canAuthor =
        (user?.roles.includes("Author") || user?.roles.includes("Admin")) ?? false;
    const queryClient = useQueryClient();

    const reportsQuery = useQuery({
        queryKey: OLD_SCHOOL_QUERY_KEY,
        queryFn: listMyReports,
        enabled: canAuthor,
    });

    const [createOpen, setCreateOpen] = useState(false);
    const [title, setTitle] = useState("");
    const [templateId, setTemplateId] = useState(DEFAULT_TEMPLATE_ID);
    const [createError, setCreateError] = useState<string | null>(null);

    const invalidate = () => queryClient.invalidateQueries({ queryKey: OLD_SCHOOL_QUERY_KEY });

    const createMutation = useMutation({
        mutationFn: async () => {
            const template = REPORT_TEMPLATES.find((x) => x.id === templateId) ?? REPORT_TEMPLATES[0];
            const report = await createMyReport({
                title: title.trim(),
                description: null,
                reportGroupId: null,
                refreshFrequencySeconds: null,
                chromeJson: template.chromeJson,
                displayOrder: 0,
            });
            for (const tile of template.tiles) {
                await addMyReportEntity(report.id, {
                    tileType: tile.tileType,
                    title: null,
                    displayOrder: -1,
                    configJson: tile.configJson,
                });
            }
            return report;
        },
        onSuccess: async (report) => {
            setCreateOpen(false);
            setTitle("");
            await invalidate();
            void navigate({ to: "/old-school/reports/$id", params: { id: String(report.id) } });
        },
        onError: () => setCreateError(t("oldSchool.list.create.unexpectedError")),
    });

    const duplicateMutation = useMutation({
        mutationFn: (report: ReportDto) => duplicateMyReport(report.id, { title: null }),
        onSuccess: () => invalidate(),
    });

    const deleteMutation = useMutation({
        mutationFn: (id: number) => deleteMyReport(id),
        onSuccess: () => invalidate(),
    });

    if (!canAuthor) {
        return (
            <VwbFrame title={t("oldSchool.title")}>
                <Alert role="alert" icon={<IconAlertCircle size={16} />} color="red" variant="light">
                    {t("oldSchool.forbidden")}
                </Alert>
            </VwbFrame>
        );
    }

    const reports = reportsQuery.data ?? [];

    return (
        <VwbFrame
            title={t("oldSchool.title")}
            crumbs={[{ label: t("oldSchool.breadcrumbRoot") }]}
            toolbar={
                <Button size="xs" onClick={() => setCreateOpen(true)}>
                    {t("oldSchool.list.newReport")}
                </Button>
            }
        >
            <Stack gap="sm">
                <Text size="xs" c="dimmed">
                    {t("oldSchool.subtitle")}
                </Text>

                {reports.length === 0 ? (
                    <Text size="sm" c="dimmed">
                        {t("oldSchool.list.empty")}
                    </Text>
                ) : (
                    <table className={styles.table} data-testid="old-school-report-table">
                        <thead>
                            <tr>
                                <th>{t("oldSchool.list.columns.title")}</th>
                                <th style={{ width: 90 }}>{t("oldSchool.list.columns.entities")}</th>
                                <th style={{ width: 140 }}>{t("oldSchool.list.columns.updated")}</th>
                                <th style={{ width: 220 }} />
                            </tr>
                        </thead>
                        <tbody>
                            {reports.map((report) => (
                                <tr key={report.id} data-testid={`old-school-report-${report.id}`}>
                                    <td>{report.title}</td>
                                    <td>{report.entityCount}</td>
                                    <td>{new Date(report.lastModifiedUtc).toLocaleDateString()}</td>
                                    <td>
                                        <div className={styles.rowActions}>
                                            <Button
                                                size="compact-xs"
                                                variant="light"
                                                component={Link}
                                                to={`/old-school/reports/${report.id}`}
                                            >
                                                {t("oldSchool.list.open")}
                                            </Button>
                                            <Button
                                                size="compact-xs"
                                                variant="subtle"
                                                onClick={() => duplicateMutation.mutate(report)}
                                            >
                                                {t("oldSchool.list.duplicate")}
                                            </Button>
                                            <Button
                                                size="compact-xs"
                                                variant="subtle"
                                                color="red"
                                                onClick={() => deleteMutation.mutate(report.id)}
                                            >
                                                {t("oldSchool.list.delete")}
                                            </Button>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </Stack>

            <Modal
                opened={createOpen}
                onClose={() => setCreateOpen(false)}
                title={t("oldSchool.list.create.title")}
            >
                <Stack gap="sm">
                    <TextInput
                        label={t("oldSchool.list.create.titleLabel")}
                        placeholder={t("oldSchool.list.create.titlePlaceholder")}
                        value={title}
                        onChange={(e) => setTitle(e.currentTarget.value)}
                        data-autofocus
                    />
                    <Select
                        label={t("oldSchool.list.create.templateLabel")}
                        data={REPORT_TEMPLATES.map((tpl) => ({
                            value: tpl.id,
                            label: t(tpl.nameKey as "oldSchool.title"),
                        }))}
                        value={templateId}
                        allowDeselect={false}
                        onChange={(v) => v && setTemplateId(v)}
                    />
                    {createError ? (
                        <Alert color="red" variant="light">
                            {createError}
                        </Alert>
                    ) : null}
                    <Group justify="flex-end">
                        <Button variant="default" onClick={() => setCreateOpen(false)}>
                            {t("oldSchool.list.cancel")}
                        </Button>
                        <Button
                            disabled={title.trim().length === 0}
                            loading={createMutation.isPending}
                            onClick={() => {
                                setCreateError(null);
                                createMutation.mutate();
                            }}
                        >
                            {t("oldSchool.list.create.submit")}
                        </Button>
                    </Group>
                </Stack>
            </Modal>
        </VwbFrame>
    );
}
