import { useEffect, useState } from "react";
import {
    Alert,
    Button,
    Group,
    NumberInput,
    SegmentedControl,
    Text,
    TextInput,
    Textarea,
} from "@mantine/core";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { IconAlertCircle } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    getMyReport,
    removeMyReportEntity,
    updateMyReport,
    updateMyReportEntity,
} from "../api/authorReports";
import type { ReportEntityDto } from "../api/adminReports";
import { parseParetoTileConfig, parsePanelYieldTileConfig } from "../components/reportConfig/tileConfig";
import { VwbFrame, VwbSection, oldSchoolStyles as styles } from "../components/oldSchool/VwbFrame";
import { useSessionStore } from "../state/session";

const LAYOUT_QUERY_KEY = (id: number) => ["oldSchool", "report", id] as const;

/** Read the `layoutColumns` key out of chromeJson, preserving other keys. */
function readColumns(chromeJson: string | null): 1 | 2 {
    if (!chromeJson) return 1;
    try {
        const raw = JSON.parse(chromeJson) as Record<string, unknown>;
        return raw.layoutColumns === 2 ? 2 : 1;
    } catch {
        return 1;
    }
}

function writeColumns(chromeJson: string | null, columns: 1 | 2): string {
    let raw: Record<string, unknown> = {};
    if (chromeJson) {
        try {
            const parsed = JSON.parse(chromeJson) as unknown;
            if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
                raw = parsed as Record<string, unknown>;
            }
        } catch {
            raw = {};
        }
    }
    raw.layoutColumns = columns;
    return JSON.stringify(raw);
}

/** Map a stored tile type to the Vieweb entity label. */
function entityLabelKey(tileType: string): string {
    switch (tileType) {
        case "pareto":
            return "oldSchool.newEntity.chart";
        case "panelYield":
            return "oldSchool.newEntity.table";
        case "comment":
            return "oldSchool.newEntity.comment";
        default:
            return "oldSchool.newEntity.comment";
    }
}

function filterCount(entity: ReportEntityDto): number {
    if (entity.tileType === "pareto") return parseParetoTileConfig(entity.configJson).filters.length;
    if (entity.tileType === "panelYield") return parsePanelYieldTileConfig(entity.configJson).filters.length;
    return 0;
}

/**
 * `/old-school/reports/$id` — the Vieweb "Layout" screen: report
 * properties on top, then the "Report content" list of entities with
 * add / edit / remove / reorder controls and a "View report" action.
 */
export function OldSchoolLayoutRoute() {
    const { t } = useTranslation();
    const navigate = useNavigate();
    const { id: idParam } = useParams({ strict: false }) as { id: string };
    const id = Number(idParam);
    const user = useSessionStore((s) => s.user);
    const canAuthor =
        (user?.roles.includes("Author") || user?.roles.includes("Admin")) ?? false;
    const queryClient = useQueryClient();

    const detailQuery = useQuery({
        queryKey: LAYOUT_QUERY_KEY(id),
        queryFn: () => getMyReport(id),
        enabled: canAuthor && Number.isFinite(id),
    });

    const [title, setTitle] = useState("");
    const [description, setDescription] = useState("");
    const [refreshMin, setRefreshMin] = useState<number | "">("");
    const [columns, setColumns] = useState<1 | 2>(1);
    const [saved, setSaved] = useState(false);

    // Seed the form once the report loads.
    const detail = detailQuery.data;
    useEffect(() => {
        if (!detail) return;
        setTitle(detail.report.title);
        setDescription(detail.report.description ?? "");
        setRefreshMin(
            detail.report.refreshFrequencySeconds
                ? Math.round(detail.report.refreshFrequencySeconds / 60)
                : "",
        );
        setColumns(readColumns(detail.report.chromeJson));
    }, [detail]);

    const invalidate = () =>
        queryClient.invalidateQueries({ queryKey: LAYOUT_QUERY_KEY(id) });

    const saveMutation = useMutation({
        mutationFn: () => {
            if (!detail) throw new Error("not loaded");
            return updateMyReport(id, {
                title: title.trim(),
                description: description.trim().length > 0 ? description.trim() : null,
                reportGroupId: detail.report.reportGroupId,
                refreshFrequencySeconds:
                    typeof refreshMin === "number" && refreshMin > 0 ? refreshMin * 60 : null,
                chromeJson: writeColumns(detail.report.chromeJson, columns),
                displayOrder: detail.report.displayOrder,
            });
        },
        onSuccess: async () => {
            setSaved(true);
            await invalidate();
        },
        onError: () => setSaved(false),
    });

    const removeMutation = useMutation({
        mutationFn: (entityId: number) => removeMyReportEntity(id, entityId),
        onSuccess: () => invalidate(),
    });

    const moveMutation = useMutation({
        mutationFn: async (payload: { a: ReportEntityDto; b: ReportEntityDto }) => {
            const { a, b } = payload;
            await updateMyReportEntity(id, a.id, {
                tileType: a.tileType,
                title: a.title,
                displayOrder: b.displayOrder,
                configJson: a.configJson,
            });
            await updateMyReportEntity(id, b.id, {
                tileType: b.tileType,
                title: b.title,
                displayOrder: a.displayOrder,
                configJson: b.configJson,
            });
        },
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

    if (!detail) {
        return (
            <VwbFrame title={t("oldSchool.title")}>
                <Text size="sm">{t("oldSchool.loading")}</Text>
            </VwbFrame>
        );
    }

    const entities = [...detail.entities].sort((a, b) => a.displayOrder - b.displayOrder);

    return (
        <VwbFrame
            title={`${t("oldSchool.layout.heading")} — ${detail.report.title}`}
            crumbs={[
                { label: t("oldSchool.breadcrumbRoot"), to: "/old-school/reports" },
                { label: detail.report.title },
            ]}
            toolbar={
                <Group gap="xs">
                    <Button
                        size="xs"
                        component={Link}
                        to={`/old-school/reports/${id}/view`}
                    >
                        {t("oldSchool.layout.view")}
                    </Button>
                    <Button
                        size="xs"
                        variant="default"
                        component={Link}
                        to="/old-school/reports"
                    >
                        {t("oldSchool.layout.back")}
                    </Button>
                </Group>
            }
        >
            <VwbSection heading={t("oldSchool.layout.propertiesHeading")}>
                <div className={styles.grid}>
                    <label className={styles.gridLabel}>{t("oldSchool.layout.titleLabel")}</label>
                    <TextInput
                        value={title}
                        onChange={(e) => {
                            setTitle(e.currentTarget.value);
                            setSaved(false);
                        }}
                    />
                    <label className={styles.gridLabel}>{t("oldSchool.layout.descriptionLabel")}</label>
                    <Textarea
                        autosize
                        minRows={2}
                        value={description}
                        onChange={(e) => {
                            setDescription(e.currentTarget.value);
                            setSaved(false);
                        }}
                    />
                    <label className={styles.gridLabel}>{t("oldSchool.layout.refreshLabel")}</label>
                    <NumberInput
                        min={1}
                        value={refreshMin}
                        description={t("oldSchool.layout.refreshHelp")}
                        onChange={(v) => {
                            setRefreshMin(typeof v === "number" ? v : "");
                            setSaved(false);
                        }}
                    />
                    <label className={styles.gridLabel}>{t("oldSchool.layout.columnsLabel")}</label>
                    <SegmentedControl
                        value={String(columns)}
                        data={[
                            { value: "1", label: t("oldSchool.layout.oneColumn") },
                            { value: "2", label: t("oldSchool.layout.twoColumns") },
                        ]}
                        onChange={(v) => {
                            setColumns(v === "2" ? 2 : 1);
                            setSaved(false);
                        }}
                    />
                </div>
                <Group mt="sm" gap="sm">
                    <Button
                        size="xs"
                        loading={saveMutation.isPending}
                        disabled={title.trim().length === 0}
                        onClick={() => saveMutation.mutate()}
                    >
                        {t("oldSchool.layout.save")}
                    </Button>
                    {saved ? (
                        <Text size="xs" c="green">
                            {t("oldSchool.layout.saved")}
                        </Text>
                    ) : null}
                    {saveMutation.isError ? (
                        <Text size="xs" c="red">
                            {t("oldSchool.layout.saveError")}
                        </Text>
                    ) : null}
                </Group>
            </VwbSection>

            <VwbSection heading={t("oldSchool.layout.contentHeading")}>
                <Group justify="flex-end" mb="xs">
                    <Button
                        size="xs"
                        component={Link}
                        to={`/old-school/reports/${id}/new-entity`}
                    >
                        {t("oldSchool.layout.addEntity")}
                    </Button>
                </Group>

                {entities.length === 0 ? (
                    <Text size="sm" c="dimmed">
                        {t("oldSchool.layout.noEntities")}
                    </Text>
                ) : (
                    entities.map((entity, index) => (
                        <div
                            key={entity.id}
                            className={styles.entityCard}
                            data-testid={`old-school-entity-${entity.id}`}
                        >
                            <Group gap="sm">
                                <span className={styles.entityTag}>
                                    {t(entityLabelKey(entity.tileType) as "oldSchool.newEntity.chart")}
                                </span>
                                <Text size="sm" fw={500}>
                                    {entity.title ??
                                        t(entityLabelKey(entity.tileType) as "oldSchool.newEntity.chart")}
                                </Text>
                                {filterCount(entity) > 0 ? (
                                    <Text size="xs" c="dimmed">
                                        {`${t("oldSchool.entity.filtersHeading")}: ${filterCount(entity)}`}
                                    </Text>
                                ) : null}
                            </Group>
                            <div className={styles.rowActions}>
                                <Button
                                    size="compact-xs"
                                    variant="subtle"
                                    disabled={index === 0}
                                    onClick={() =>
                                        moveMutation.mutate({ a: entity, b: entities[index - 1] })
                                    }
                                >
                                    {t("oldSchool.layout.moveUp")}
                                </Button>
                                <Button
                                    size="compact-xs"
                                    variant="subtle"
                                    disabled={index === entities.length - 1}
                                    onClick={() =>
                                        moveMutation.mutate({ a: entity, b: entities[index + 1] })
                                    }
                                >
                                    {t("oldSchool.layout.moveDown")}
                                </Button>
                                <Button
                                    size="compact-xs"
                                    variant="light"
                                    onClick={() =>
                                        void navigate({
                                            to: "/old-school/reports/$id/entity/$entityId",
                                            params: { id: String(id), entityId: String(entity.id) },
                                        })
                                    }
                                >
                                    {t("oldSchool.layout.edit")}
                                </Button>
                                <Button
                                    size="compact-xs"
                                    variant="subtle"
                                    color="red"
                                    onClick={() => removeMutation.mutate(entity.id)}
                                >
                                    {t("oldSchool.layout.remove")}
                                </Button>
                            </div>
                        </div>
                    ))
                )}
            </VwbSection>
        </VwbFrame>
    );
}
