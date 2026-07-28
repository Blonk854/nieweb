import { useEffect, useMemo, useState } from "react";
import {
    Alert,
    Button,
    Group,
    SegmentedControl,
    Stack,
    Text,
    TextInput,
    Textarea,
} from "@mantine/core";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { IconAlertCircle } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import { getMyReport, updateMyReportEntity } from "../api/authorReports";
import {
    parseCommentTileConfig,
    parsePanelYieldTileConfig,
    parseParetoTileConfig,
    serializeCommentTileConfig,
    serializePanelYieldTileConfig,
    serializeParetoTileConfig,
} from "../components/reportConfig/tileConfig";
import { TileConfigForm } from "../components/reportConfig/TileConfigForm";
import { hasTileConfigForm } from "../components/reportConfig/tileConfigSchema";
import { FilterBuilder } from "../components/oldSchool/FilterBuilder";
import { VwbFrame, VwbSection } from "../components/oldSchool/VwbFrame";
import {
    filterFieldsForTile,
    isClauseValid,
    type FilterClause,
} from "../api/filters";
import { useSessionStore } from "../state/session";

const QUERY_KEY = (id: number) => ["oldSchool", "report", id] as const;

/** Read the filters array out of a tile's configJson. */
function readFilters(tileType: string, configJson: string): FilterClause[] {
    if (tileType === "pareto") return parseParetoTileConfig(configJson).filters;
    if (tileType === "panelYield") return parsePanelYieldTileConfig(configJson).filters;
    return [];
}

/** Write a new filters array back into a tile's configJson (knobs preserved). */
function writeFilters(tileType: string, configJson: string, filters: FilterClause[]): string {
    if (tileType === "pareto") {
        return serializeParetoTileConfig({ ...parseParetoTileConfig(configJson), filters });
    }
    if (tileType === "panelYield") {
        return serializePanelYieldTileConfig({ ...parsePanelYieldTileConfig(configJson), filters });
    }
    return configJson;
}

/**
 * `/old-school/reports/$id/entity/$entityId` — the Vieweb entity
 * editor: General (title), Parameters (analytic knobs / comment body)
 * and Filters (per-entity generic operator filter builder). Saves the
 * whole entity via the owner-scoped author API.
 */
export function OldSchoolEntityRoute() {
    const { t } = useTranslation();
    const navigate = useNavigate();
    const params = useParams({ strict: false }) as { id: string; entityId: string };
    const id = Number(params.id);
    const entityId = Number(params.entityId);
    const user = useSessionStore((s) => s.user);
    const canAuthor =
        (user?.roles.includes("Author") || user?.roles.includes("Admin")) ?? false;
    const queryClient = useQueryClient();

    const detailQuery = useQuery({
        queryKey: QUERY_KEY(id),
        queryFn: () => getMyReport(id),
        enabled: canAuthor && Number.isFinite(id),
    });

    const entity = detailQuery.data?.entities.find((e) => e.id === entityId);

    const [titleMode, setTitleMode] = useState<"auto" | "manual">("auto");
    const [manualTitle, setManualTitle] = useState("");
    const [configJson, setConfigJson] = useState("{}");
    const [markdown, setMarkdown] = useState("");
    const [error, setError] = useState<string | null>(null);

    // Seed from the loaded entity once.
    useEffect(() => {
        if (!entity) return;
        setTitleMode(entity.title ? "manual" : "auto");
        setManualTitle(entity.title ?? "");
        setConfigJson(entity.configJson && entity.configJson.length > 0 ? entity.configJson : "{}");
        if (entity.tileType === "comment") {
            setMarkdown(parseCommentTileConfig(entity.configJson).markdown);
        }
    }, [entity]);

    const filters = useMemo(
        () => (entity ? readFilters(entity.tileType, configJson) : []),
        [entity, configJson],
    );
    const fields = entity ? filterFieldsForTile(entity.tileType) : [];

    const saveMutation = useMutation({
        mutationFn: () => {
            if (!entity) throw new Error("not loaded");
            const finalConfig =
                entity.tileType === "comment"
                    ? serializeCommentTileConfig({ markdown })
                    : configJson;
            return updateMyReportEntity(id, entityId, {
                tileType: entity.tileType,
                title: titleMode === "manual" && manualTitle.trim().length > 0 ? manualTitle.trim() : null,
                displayOrder: entity.displayOrder,
                configJson: finalConfig,
            });
        },
        onSuccess: async () => {
            await queryClient.invalidateQueries({ queryKey: QUERY_KEY(id) });
            void navigate({ to: "/old-school/reports/$id", params: { id: String(id) } });
        },
    });

    function handleSave() {
        if (filters.some((c) => !isClauseValid(c))) {
            setError(t("oldSchool.entity.invalidFilter"));
            return;
        }
        setError(null);
        saveMutation.mutate();
    }

    if (!canAuthor) {
        return (
            <VwbFrame title={t("oldSchool.title")}>
                <Alert role="alert" icon={<IconAlertCircle size={16} />} color="red" variant="light">
                    {t("oldSchool.forbidden")}
                </Alert>
            </VwbFrame>
        );
    }

    if (!entity) {
        return (
            <VwbFrame title={t("oldSchool.title")}>
                <Text size="sm">{t("oldSchool.loading")}</Text>
            </VwbFrame>
        );
    }

    return (
        <VwbFrame
            title={t("oldSchool.entity.heading")}
            crumbs={[
                { label: t("oldSchool.breadcrumbRoot"), to: "/old-school/reports" },
                { label: t("oldSchool.layout.heading"), to: `/old-school/reports/${id}` },
                { label: t("oldSchool.entity.heading") },
            ]}
            toolbar={
                <Button
                    size="xs"
                    variant="default"
                    component={Link}
                    to={`/old-school/reports/${id}`}
                >
                    {t("oldSchool.entity.back")}
                </Button>
            }
        >
            <VwbSection heading={t("oldSchool.entity.generalHeading")}>
                <Stack gap="sm">
                    <Group gap="sm" align="flex-end">
                        <div>
                            <Text size="xs" fw={700} mb={4}>
                                {t("oldSchool.entity.titleMode")}
                            </Text>
                            <SegmentedControl
                                value={titleMode}
                                data={[
                                    { value: "auto", label: t("oldSchool.entity.titleAuto") },
                                    { value: "manual", label: t("oldSchool.entity.titleManual") },
                                ]}
                                onChange={(v) => setTitleMode(v === "manual" ? "manual" : "auto")}
                            />
                        </div>
                        {titleMode === "manual" ? (
                            <TextInput
                                label={t("oldSchool.entity.titleLabel")}
                                value={manualTitle}
                                onChange={(e) => setManualTitle(e.currentTarget.value)}
                                w={280}
                            />
                        ) : null}
                    </Group>
                </Stack>
            </VwbSection>

            <VwbSection heading={t("oldSchool.entity.parametersHeading")}>
                {entity.tileType === "comment" ? (
                    <Textarea
                        label={t("oldSchool.entity.commentBody")}
                        autosize
                        minRows={4}
                        value={markdown}
                        onChange={(e) => setMarkdown(e.currentTarget.value)}
                    />
                ) : hasTileConfigForm(entity.tileType) ? (
                    <TileConfigForm
                        tileType={entity.tileType}
                        value={configJson}
                        onChange={setConfigJson}
                    />
                ) : (
                    <Text size="sm" c="dimmed">
                        {entity.tileType}
                    </Text>
                )}
            </VwbSection>

            {fields.length > 0 ? (
                <VwbSection heading={t("oldSchool.entity.filtersHeading")}>
                    <Text size="xs" c="dimmed" mb="xs">
                        {t("oldSchool.entity.filtersHelp")}
                    </Text>
                    <FilterBuilder
                        fields={fields}
                        value={filters}
                        onChange={(next) => setConfigJson(writeFilters(entity.tileType, configJson, next))}
                    />
                </VwbSection>
            ) : null}

            {error ? (
                <Alert color="red" variant="light" mb="sm">
                    {error}
                </Alert>
            ) : null}

            <Group>
                <Button
                    loading={saveMutation.isPending}
                    onClick={handleSave}
                >
                    {t("oldSchool.entity.save")}
                </Button>
                {saveMutation.isError ? (
                    <Text size="xs" c="red">
                        {t("oldSchool.layout.saveError")}
                    </Text>
                ) : null}
            </Group>
        </VwbFrame>
    );
}
