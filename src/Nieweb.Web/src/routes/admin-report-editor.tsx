import { useEffect, useMemo, useState } from "react";
import {
    Accordion,
    ActionIcon,
    Alert,
    Badge,
    Button,
    Card,
    Checkbox,
    Group,
    Loader,
    Menu,
    Modal,
    NumberInput,
    PasswordInput,
    Select,
    Stack,
    Text,
    TextInput,
    Textarea,
    Title,
    Tooltip,
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams, useRouter } from "@tanstack/react-router";
import {
    IconAlertCircle,
    IconArrowDown,
    IconArrowLeft,
    IconArrowUp,
    IconCode,
    IconCopy,
    IconDownload,
    IconEye,
    IconFileTypeCsv,
    IconFileTypePdf,
    IconLock,
    IconLockOpen,
    IconPlus,
    IconTrash,
} from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    type DuplicateReportRequest,
    type EntityRequest,
    type ReportDetailDto,
    type ReportEntityDto,
    type ReportGroupDto,
    type ReportPasswordRequest,
    type UpdateReportRequest,
} from "../api/adminReports";
import {
    ReportsApiProvider,
    adminReportsAdapter,
    authorReportsAdapter,
    useReportsApi,
} from "../api/reportsApi";
import {
    downloadReportExport,
    reportExportUrl,
    type ReportExportFilter,
    type ReportExportFormat,
} from "../api/reportExport";
import { fetchSources, type SourceInfo } from "../api/sources";
import { TILE_LABEL_KEYS, TILE_TYPES, type TileType } from "../components/canvas/tileTypes";
import { TileConfigForm } from "../components/reportConfig/TileConfigForm";
import { hasTileConfigForm } from "../components/reportConfig/tileConfigSchema";
import {
    readChromeDefaults,
    writeChromeDefaults,
    resolveWindowPreset,
    REPORT_WINDOW_PRESETS,
    type ReportWindowPreset,
} from "../components/reportConfig/reportChrome";
import { PdfPreviewModal } from "../components/PdfPreviewModal";
import {
    instantIsoToWallClock,
    wallClockToInstantIso,
} from "../i18n/zoneConverters";
import { resolveTimeZone, usePreferencesStore } from "../state/preferences";
import { useSessionStore } from "../state/session";

/**
 * RC2 report editor route (`/admin/reports/$id`).
 *
 * Loads a single report + its ordered tiles and renders three
 * cards:
 *
 * 1. A header form that lets the admin edit the report's title,
 *    description, group, refresh cadence and locked / pinned
 *    flags. Persisted via `PUT /api/admin/reports/{id}`.
 * 2. A palette + ordered tile list. Each tile row exposes
 *    move-up / move-down / remove controls and a small form for
 *    the tile's `TileType`, human `Title`, and opaque
 *    `ConfigJson` payload. Persisted via
 *    `POST/PUT/DELETE /api/admin/reports/{id}/entities/[...]`.
 * 3. An empty-state prompt when the report has no tiles yet.
 *
 * All writes go straight to the server (no local dirty state);
 * TanStack Query invalidates the report query on success so the
 * server is the single source of truth. That matches how the
 * admin-users route behaves and keeps the RC2 UI simple —
 * client-side undo / batching is deliberately deferred.
 */

const groupsQueryKey = (mode: string) => [mode, "report-groups"] as const;
const reportQueryKey = (mode: string, id: number) => [mode, "report", id] as const;
// The list query the two surfaces invalidate on write differs: the
// admin list is `["admin","reports"]`, the author "My Reports" list is
// `["reports","mine"]`. Invalidating the inactive one is a harmless
// no-op, but targeting the right key keeps each list fresh in place.
const listQueryKey = (mode: string) =>
    mode === "author" ? (["reports", "mine"] as const) : (["admin", "reports"] as const);

export function AdminReportEditorRoute() {
    return (
        <ReportsApiProvider adapter={adminReportsAdapter}>
            <ReportEditorScreen />
        </ReportsApiProvider>
    );
}

export function MyReportEditorRoute() {
    return (
        <ReportsApiProvider adapter={authorReportsAdapter}>
            <ReportEditorScreen />
        </ReportsApiProvider>
    );
}

function ReportEditorScreen() {
    const { t } = useTranslation();
    const api = useReportsApi();
    const params = useParams({ strict: false }) as { id?: string };
    const user = useSessionStore((s) => s.user);
    const allowed =
        api.mode === "author"
            ? ((user?.roles.includes("Author") || user?.roles.includes("Admin")) ?? false)
            : (user?.roles.includes("Admin") ?? false);

    const reportId = params.id !== undefined ? Number(params.id) : NaN;
    const idValid = Number.isFinite(reportId) && Number.isInteger(reportId);

    if (!allowed) {
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

    if (!idValid) {
        return (
            <Stack gap="md">
                <BackLink />
                <Alert
                    role="alert"
                    icon={<IconAlertCircle size={16} />}
                    color="red"
                    variant="light"
                >
                    {t("admin.reports.editor.notFound")}
                </Alert>
            </Stack>
        );
    }

    return <EditorBody reportId={reportId} />;
}

function BackLink() {
    const { t } = useTranslation();
    const api = useReportsApi();
    return (
        <Group gap="xs">
            <Button
                component={Link}
                to={api.mode === "author" ? "/reports" : "/admin/reports"}
                variant="subtle"
                size="xs"
                leftSection={<IconArrowLeft size={14} />}
            >
                {t("admin.reports.editor.backLink")}
            </Button>
        </Group>
    );
}

function EditorBody(props: { reportId: number }) {
    const { t } = useTranslation();
    const { reportId } = props;
    const api = useReportsApi();

    const groupsQuery = useQuery({
        queryKey: groupsQueryKey(api.mode),
        queryFn: api.listGroups,
    });
    const reportQuery = useQuery({
        queryKey: reportQueryKey(api.mode, reportId),
        queryFn: () => api.getReport(reportId),
    });

    if (reportQuery.isPending) {
        return (
            <Stack gap="md">
                <BackLink />
                <Group>
                    <Loader size="sm" />
                    <Text c="dimmed">{t("common.loading")}</Text>
                </Group>
            </Stack>
        );
    }

    if (reportQuery.isError) {
        return (
            <Stack gap="md">
                <BackLink />
                <Alert
                    role="alert"
                    icon={<IconAlertCircle size={16} />}
                    color="red"
                    variant="light"
                >
                    {t("admin.reports.editor.loadError")}
                </Alert>
            </Stack>
        );
    }

    return (
        <Stack gap="lg">
            <BackLink />
            <Title order={2}>{reportQuery.data.report.title}</Title>
            <HeaderForm
                detail={reportQuery.data}
                groups={groupsQuery.data ?? []}
            />
            <LockActionsCard detail={reportQuery.data} />
            <TilesCard detail={reportQuery.data} />
            <ExportReportCard detail={reportQuery.data} />
        </Stack>
    );
}

// -------------------- Header form --------------------

type HeaderFormValues = {
    title: string;
    description: string;
    groupId: string | null;
    refreshSeconds: number | "";
    displayOrder: number;
    isLocked: boolean;
    isPinnedHome: boolean;
    defaultSourceId: string | null;
    defaultWindowPreset: string | null;
};

function HeaderForm(props: {
    detail: ReportDetailDto;
    groups: ReportGroupDto[];
}) {
    const { t } = useTranslation();
    const queryClient = useQueryClient();
    const api = useReportsApi();
    const [savedFlash, setSavedFlash] = useState(false);
    const [serverError, setServerError] = useState<string | null>(null);
    const report = props.detail.report;

    const sourcesQuery = useQuery({
        queryKey: ["sources"] as const,
        queryFn: fetchSources,
    });

    const initial: HeaderFormValues = useMemo(
        () => {
            const chrome = readChromeDefaults(report.chromeJson);
            return {
                title: report.title,
                description: report.description ?? "",
                groupId: report.reportGroupId !== null ? String(report.reportGroupId) : null,
                refreshSeconds:
                    report.refreshFrequencySeconds ?? "",
                displayOrder: report.displayOrder,
                isLocked: report.isLocked,
                isPinnedHome: report.isPinnedHome,
                defaultSourceId: chrome.defaultSourceId ?? null,
                defaultWindowPreset: chrome.defaultWindowPreset ?? null,
            };
        },
        [report],
    );

    const form = useForm<HeaderFormValues>({
        initialValues: initial,
        validate: {
            title: (v) =>
                v.trim().length === 0
                    ? t("admin.reports.list.create.titleRequired")
                    : null,
        },
    });

    // Re-seed the form whenever the underlying report changes (e.g. after
    // a successful save invalidates the query and TanStack Query refetches).
    useEffect(() => {
        form.setValues(initial);
        form.resetDirty(initial);
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [initial]);

    const groupOptions = useMemo(
        () =>
            props.groups.map((g) => ({
                value: String(g.id),
                label: g.name,
            })),
        [props.groups],
    );

    const sourceOptions = useMemo(
        () =>
            (sourcesQuery.data ?? []).map((s: SourceInfo) => ({
                value: s.id,
                label: s.available ? s.displayName : `${s.displayName} (unavailable)`,
            })),
        [sourcesQuery.data],
    );

    const presetOptions = useMemo(
        () =>
            REPORT_WINDOW_PRESETS.map((p) => ({
                value: p,
                label: t(`admin.reports.editor.header.windowPreset.${p}` as const),
            })),
        [t],
    );

    const mutation = useMutation({
        mutationFn: (body: UpdateReportRequest) => api.updateReport(report.id, body),
        onSuccess: async () => {
            setServerError(null);
            setSavedFlash(true);
            window.setTimeout(() => setSavedFlash(false), 1500);
            await queryClient.invalidateQueries({ queryKey: reportQueryKey(api.mode, report.id) });
            await queryClient.invalidateQueries({ queryKey: listQueryKey(api.mode) });
        },
        onError: () => {
            setServerError(t("admin.reports.editor.header.unexpectedError"));
        },
    });

    return (
        <Card withBorder padding="lg" radius="md">
            <Title order={4} mb="sm">
                {t("admin.reports.editor.header.heading")}
            </Title>
            <form
                onSubmit={form.onSubmit((values) => {
                    const desc = values.description.trim();
                    mutation.mutate({
                        title: values.title.trim(),
                        description: desc.length > 0 ? desc : null,
                        reportGroupId:
                            values.groupId !== null ? Number(values.groupId) : null,
                        isLocked: values.isLocked,
                        isPinnedHome: values.isPinnedHome,
                        refreshFrequencySeconds:
                            values.refreshSeconds === "" ? null : values.refreshSeconds,
                        chromeJson: writeChromeDefaults(report.chromeJson, {
                            defaultSourceId: values.defaultSourceId ?? undefined,
                            defaultWindowPreset:
                                (values.defaultWindowPreset as ReportWindowPreset | null) ??
                                undefined,
                        }),
                        displayOrder: values.displayOrder,
                    });
                })}
            >
                <Stack gap="sm">
                    <TextInput
                        label={t("admin.reports.editor.header.titleLabel")}
                        withAsterisk
                        {...form.getInputProps("title")}
                    />
                    <Textarea
                        label={t("admin.reports.editor.header.descriptionLabel")}
                        autosize
                        minRows={2}
                        {...form.getInputProps("description")}
                    />
                    <Group grow>
                        <Select
                            label={t("admin.reports.editor.header.groupLabel")}
                            placeholder={t("admin.reports.editor.header.groupPlaceholder")}
                            data={groupOptions}
                            clearable
                            {...form.getInputProps("groupId")}
                        />
                        <NumberInput
                            label={t("admin.reports.editor.header.displayOrderLabel")}
                            min={0}
                            value={form.values.displayOrder}
                            onChange={(v) =>
                                form.setFieldValue(
                                    "displayOrder",
                                    typeof v === "number" ? v : 0,
                                )
                            }
                        />
                    </Group>
                    <NumberInput
                        label={t("admin.reports.editor.header.refreshLabel")}
                        description={t("admin.reports.editor.header.refreshHint")}
                        min={1}
                        value={
                            form.values.refreshSeconds === ""
                                ? undefined
                                : form.values.refreshSeconds
                        }
                        onChange={(v) =>
                            form.setFieldValue(
                                "refreshSeconds",
                                typeof v === "number" ? v : "",
                            )
                        }
                    />
                    <Group grow>
                        <Select
                            label={t("admin.reports.editor.header.defaultSourceLabel")}
                            description={t("admin.reports.editor.header.defaultSourceHint")}
                            placeholder={t("admin.reports.editor.header.defaultSourcePlaceholder")}
                            data={sourceOptions}
                            clearable
                            disabled={sourcesQuery.isPending}
                            {...form.getInputProps("defaultSourceId")}
                        />
                        <Select
                            label={t("admin.reports.editor.header.defaultWindowLabel")}
                            description={t("admin.reports.editor.header.defaultWindowHint")}
                            placeholder={t("admin.reports.editor.header.defaultWindowPlaceholder")}
                            data={presetOptions}
                            clearable
                            {...form.getInputProps("defaultWindowPreset")}
                        />
                    </Group>
                    <Group>
                        <Checkbox
                            label={t("admin.reports.editor.header.isPinnedHomeLabel")}
                            checked={form.values.isPinnedHome}
                            onChange={(e) =>
                                form.setFieldValue(
                                    "isPinnedHome",
                                    e.currentTarget.checked,
                                )
                            }
                        />
                    </Group>
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
                    <Group justify="flex-end" gap="sm" align="center">
                        {savedFlash && (
                            <Text c="green" size="sm">
                                {t("admin.reports.editor.header.saved")}
                            </Text>
                        )}
                        <Button type="submit" loading={mutation.isPending}>
                            {mutation.isPending
                                ? t("admin.reports.editor.header.saving")
                                : t("admin.reports.editor.header.submit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Card>
    );
}

// -------------------- Tiles card --------------------

function TilesCard(props: { detail: ReportDetailDto }) {
    const { t } = useTranslation();
    const queryClient = useQueryClient();
    const api = useReportsApi();
    const router = useRouter();
    const detail = props.detail;
    const reportId = detail.report.id;

    const invalidate = async () => {
        await queryClient.invalidateQueries({ queryKey: reportQueryKey(api.mode, reportId) });
        await queryClient.invalidateQueries({ queryKey: listQueryKey(api.mode) });
        // Re-render the parent so `detail` refreshes with new tiles.
        await router.invalidate();
    };

    const addMutation = useMutation({
        mutationFn: (type: TileType) =>
            api.addEntity(reportId, {
                tileType: type,
                displayOrder: -1,
                configJson: "{}",
            }),
        onSuccess: invalidate,
    });

    const moveMutation = useMutation({
        mutationFn: (args: { entity: ReportEntityDto; newOrder: number }) =>
            api.updateEntity(reportId, args.entity.id, {
                tileType: args.entity.tileType,
                title: args.entity.title,
                displayOrder: args.newOrder,
                configJson: args.entity.configJson,
            }),
        onSuccess: invalidate,
    });

    const removeMutation = useMutation({
        mutationFn: (entityId: number) => api.removeEntity(reportId, entityId),
        onSuccess: invalidate,
    });

    const entities = detail.entities;

    const handleMove = (index: number, direction: -1 | 1) => {
        const target = index + direction;
        if (target < 0 || target >= entities.length) return;
        const source = entities[index];
        const swap = entities[target];
        // Two moves are needed to genuinely reorder because DisplayOrder is
        // not required to be a contiguous 0-based sequence — we shuffle the
        // two neighbours to trade their positions.
        moveMutation.mutate(
            { entity: source, newOrder: swap.displayOrder },
            {
                onSuccess: () => {
                    moveMutation.mutate({ entity: swap, newOrder: source.displayOrder });
                },
            },
        );
    };

    return (
        <Card withBorder padding="lg" radius="md">
            <Group justify="space-between" mb="xs">
                <Title order={4}>{t("admin.reports.editor.tiles.heading")}</Title>
                <Menu shadow="md" position="bottom-end">
                    <Menu.Target>
                        <Button
                            size="xs"
                            leftSection={<IconPlus size={14} />}
                            loading={addMutation.isPending}
                        >
                            {t("admin.reports.editor.tiles.add")}
                        </Button>
                    </Menu.Target>
                    <Menu.Dropdown>
                        <Menu.Label>
                            {t("admin.reports.editor.tiles.addMenuHeading")}
                        </Menu.Label>
                        {TILE_TYPES.map((type) => (
                            <Menu.Item
                                key={type}
                                onClick={() => addMutation.mutate(type)}
                            >
                                {t(TILE_LABEL_KEYS[type])}
                            </Menu.Item>
                        ))}
                    </Menu.Dropdown>
                </Menu>
            </Group>
            <Text c="dimmed" size="sm" mb="md">
                {t("admin.reports.editor.tiles.subtitle")}
            </Text>
            {entities.length === 0 ? (
                <Text c="dimmed">
                    {t("admin.reports.editor.tiles.emptyState")}
                </Text>
            ) : (
                <Stack gap="md">
                    {entities.map((entity, index) => (
                        <TileRow
                            key={entity.id}
                            entity={entity}
                            index={index}
                            total={entities.length}
                            onMoveUp={() => handleMove(index, -1)}
                            onMoveDown={() => handleMove(index, 1)}
                            onRemove={() => removeMutation.mutate(entity.id)}
                            onSave={(body) =>
                                api.updateEntity(reportId, entity.id, body).then(
                                    invalidate,
                                )
                            }
                        />
                    ))}
                </Stack>
            )}
        </Card>
    );
}

// -------------------- Tile row --------------------

function TileRow(props: {
    entity: ReportEntityDto;
    index: number;
    total: number;
    onMoveUp: () => void;
    onMoveDown: () => void;
    onRemove: () => void;
    onSave: (body: EntityRequest) => Promise<unknown>;
}) {
    const { t } = useTranslation();
    const { entity, index, total, onMoveUp, onMoveDown, onRemove, onSave } = props;
    const [tileType, setTileType] = useState<string>(entity.tileType);
    const [tileTitle, setTileTitle] = useState<string>(entity.title ?? "");
    const [configText, setConfigText] = useState<string>(entity.configJson);
    const [status, setStatus] = useState<
        { kind: "idle" } | { kind: "saving" } | { kind: "saved" } | { kind: "error"; message: string }
    >({ kind: "idle" });

    // Re-hydrate local state when the underlying entity changes (e.g. after a
    // successful reorder mutation invalidates the report query).
    useEffect(() => {
        setTileType(entity.tileType);
        setTileTitle(entity.title ?? "");
        setConfigText(entity.configJson);
    }, [entity]);

    const knownType = (TILE_TYPES as readonly string[]).includes(tileType);
    const isComment = tileType === "comment";

    // Derive the current markdown from `configText` whenever the tile is a
    // comment tile. Malformed JSON or a missing `markdown` field degrades
    // gracefully to "" so the admin can start fresh without confusing errors.
    const currentMarkdown = useMemo(() => {
        if (!isComment) return "";
        try {
            const parsed: unknown = JSON.parse(configText);
            if (parsed !== null && typeof parsed === "object" && "markdown" in parsed) {
                const md = (parsed as { markdown: unknown }).markdown;
                return typeof md === "string" ? md : "";
            }
        } catch {
            // Fall through
        }
        return "";
    }, [isComment, configText]);
    const tileOptions = useMemo<Array<{ value: string; label: string }>>(() => {
        const opts: Array<{ value: string; label: string }> = TILE_TYPES.map((type) => ({
            value: type,
            label: t(TILE_LABEL_KEYS[type]),
        }));
        // If the stored tile type is not in the catalogue (older / removed
        // tile), keep it selectable so the admin can pick a replacement
        // without silently losing the row.
        if (!knownType) {
            opts.push({
                value: tileType,
                label: t("admin.reports.editor.tiles.unknownType", { type: tileType }),
            });
        }
        return opts;
    }, [knownType, t, tileType]);

    async function handleSave() {
        let parsed: unknown;
        try {
            parsed = JSON.parse(configText);
        } catch {
            setStatus({
                kind: "error",
                message: t("admin.reports.editor.tiles.configInvalid"),
            });
            return;
        }
        setStatus({ kind: "saving" });
        try {
            await onSave({
                tileType,
                title: tileTitle.trim().length > 0 ? tileTitle.trim() : null,
                displayOrder: entity.displayOrder,
                configJson: JSON.stringify(parsed),
            });
            setStatus({ kind: "saved" });
            window.setTimeout(() => {
                setStatus((s) => (s.kind === "saved" ? { kind: "idle" } : s));
            }, 1500);
        } catch {
            setStatus({
                kind: "error",
                message: t("admin.reports.editor.tiles.saveError"),
            });
        }
    }

    return (
        <Card
            withBorder
            padding="md"
            radius="md"
            data-testid={`tile-row-${entity.id}`}
        >
            <Group justify="space-between" mb="sm">
                <Text fw={500}>
                    {knownType
                        ? t(TILE_LABEL_KEYS[tileType as TileType])
                        : t("admin.reports.editor.tiles.unknownType", {
                            type: tileType,
                        })}
                </Text>
                <Group gap="xs">
                    <Tooltip label={t("admin.reports.editor.tiles.moveUp")}>
                        <ActionIcon
                            variant="subtle"
                            aria-label={t("admin.reports.editor.tiles.moveUp")}
                            disabled={index === 0}
                            onClick={onMoveUp}
                        >
                            <IconArrowUp size={16} />
                        </ActionIcon>
                    </Tooltip>
                    <Tooltip label={t("admin.reports.editor.tiles.moveDown")}>
                        <ActionIcon
                            variant="subtle"
                            aria-label={t("admin.reports.editor.tiles.moveDown")}
                            disabled={index === total - 1}
                            onClick={onMoveDown}
                        >
                            <IconArrowDown size={16} />
                        </ActionIcon>
                    </Tooltip>
                    <Tooltip label={t("admin.reports.editor.tiles.remove")}>
                        <ActionIcon
                            variant="subtle"
                            color="red"
                            aria-label={t("admin.reports.editor.tiles.remove")}
                            onClick={onRemove}
                        >
                            <IconTrash size={16} />
                        </ActionIcon>
                    </Tooltip>
                </Group>
            </Group>
            <Stack gap="sm">
                <Group grow>
                    <Select
                        label={t("admin.reports.editor.tiles.tileTypeLabel")}
                        data={tileOptions}
                        value={tileType}
                        onChange={(v) => {
                            if (v !== null) setTileType(v);
                        }}
                        allowDeselect={false}
                    />
                    <TextInput
                        label={t("admin.reports.editor.tiles.titleLabel")}
                        placeholder={t("admin.reports.editor.tiles.titlePlaceholder")}
                        value={tileTitle}
                        onChange={(e) => setTileTitle(e.currentTarget.value)}
                    />
                </Group>
                {isComment ? (
                    <Textarea
                        label={t("admin.reports.editor.tiles.commentLabel")}
                        description={t("admin.reports.editor.tiles.commentHint")}
                        placeholder={t("admin.reports.editor.tiles.commentPlaceholder")}
                        autosize
                        minRows={6}
                        maxRows={20}
                        value={currentMarkdown}
                        onChange={(e) => {
                            setConfigText(
                                JSON.stringify({ markdown: e.currentTarget.value }),
                            );
                        }}
                    />
                ) : hasTileConfigForm(tileType) ? (
                    <Stack gap="sm">
                        <TileConfigForm
                            tileType={tileType}
                            value={configText}
                            onChange={setConfigText}
                        />
                        <Accordion variant="contained">
                            <Accordion.Item value="advanced-json">
                                <Accordion.Control
                                    icon={<IconCode size={16} />}
                                >
                                    {t("admin.reports.editor.tiles.advancedLabel")}
                                </Accordion.Control>
                                <Accordion.Panel>
                                    <Textarea
                                        aria-label={t("admin.reports.editor.tiles.configLabel")}
                                        description={t("admin.reports.editor.tiles.configHint")}
                                        autosize
                                        minRows={3}
                                        maxRows={12}
                                        styles={{
                                            input: {
                                                fontFamily:
                                                    "ui-monospace, SFMono-Regular, Menlo, monospace",
                                            },
                                        }}
                                        value={configText}
                                        onChange={(e) =>
                                            setConfigText(e.currentTarget.value)
                                        }
                                    />
                                </Accordion.Panel>
                            </Accordion.Item>
                        </Accordion>
                    </Stack>
                ) : (
                    <Textarea
                        label={t("admin.reports.editor.tiles.configLabel")}
                        description={t("admin.reports.editor.tiles.configHint")}
                        autosize
                        minRows={3}
                        maxRows={12}
                        styles={{
                            input: {
                                fontFamily:
                                    "ui-monospace, SFMono-Regular, Menlo, monospace",
                            },
                        }}
                        value={configText}
                        onChange={(e) => setConfigText(e.currentTarget.value)}
                    />
                )}
                {status.kind === "error" && (
                    <Alert
                        role="alert"
                        icon={<IconAlertCircle size={16} />}
                        color="red"
                        variant="light"
                    >
                        {status.message}
                    </Alert>
                )}
                <Group justify="flex-end" gap="sm" align="center">
                    {status.kind === "saved" && (
                        <Text c="green" size="sm">
                            {t("admin.reports.editor.tiles.saved")}
                        </Text>
                    )}
                    <Button
                        size="xs"
                        onClick={() => {
                            void handleSave();
                        }}
                        loading={status.kind === "saving"}
                    >
                        {t("admin.reports.editor.tiles.save")}
                    </Button>
                </Group>
            </Stack>
        </Card>
    );
}

// -------------------- Lock actions (RC3) --------------------

function LockActionsCard(props: { detail: ReportDetailDto }) {
    const { t } = useTranslation();
    const user = useSessionStore((s) => s.user);
    const report = props.detail.report;
    const [lockOpen, setLockOpen] = useState(false);
    const [unlockOpen, setUnlockOpen] = useState(false);
    const [dupOpen, setDupOpen] = useState(false);

    return (
        <Card withBorder padding="lg" radius="md">
            <Group justify="space-between" align="center">
                <Group gap="sm" align="center">
                    <Title order={4}>{t("admin.reports.editor.lock.heading")}</Title>
                    {report.isLocked ? (
                        <Badge
                            color="gray"
                            variant="light"
                            leftSection={<IconLock size={12} />}
                        >
                            {t("admin.reports.editor.lock.statusLocked")}
                        </Badge>
                    ) : (
                        <Badge
                            color="green"
                            variant="light"
                            leftSection={<IconLockOpen size={12} />}
                        >
                            {t("admin.reports.editor.lock.statusUnlocked")}
                        </Badge>
                    )}
                </Group>
                <Group gap="xs">
                    {report.isLocked ? (
                        <Button
                            variant="light"
                            leftSection={<IconLockOpen size={16} />}
                            onClick={() => setUnlockOpen(true)}
                        >
                            {t("admin.reports.editor.lock.unlockButton")}
                        </Button>
                    ) : (
                        <Button
                            variant="light"
                            leftSection={<IconLock size={16} />}
                            onClick={() => setLockOpen(true)}
                        >
                            {t("admin.reports.editor.lock.lockButton")}
                        </Button>
                    )}
                    <Button
                        variant="light"
                        color="blue"
                        leftSection={<IconCopy size={16} />}
                        onClick={() => setDupOpen(true)}
                    >
                        {t("admin.reports.editor.lock.duplicateButton")}
                    </Button>
                </Group>
            </Group>
            <Text c="dimmed" size="sm" mt="xs">
                {report.isLocked
                    ? t("admin.reports.editor.lock.hintLocked")
                    : t("admin.reports.editor.lock.hintUnlocked")}
            </Text>

            {lockOpen && (
                <LockReportModal
                    reportId={report.id}
                    onClose={() => setLockOpen(false)}
                />
            )}
            {unlockOpen && (
                <UnlockReportModal
                    reportId={report.id}
                    onClose={() => setUnlockOpen(false)}
                />
            )}
            {dupOpen && (
                <DuplicateFromEditorModal
                    report={report}
                    defaultOwner={user?.displayName ?? user?.email ?? ""}
                    onClose={() => setDupOpen(false)}
                />
            )}
        </Card>
    );
}

function LockReportModal(props: { reportId: number; onClose: () => void }) {
    const { t } = useTranslation();
    const queryClient = useQueryClient();
    const api = useReportsApi();
    const [serverError, setServerError] = useState<string | null>(null);
    const form = useForm<ReportPasswordRequest>({
        initialValues: { password: "" },
        validate: {
            password: (v) =>
                v.trim().length === 0
                    ? t("admin.reports.editor.lock.passwordRequired")
                    : null,
        },
    });
    const mutation = useMutation({
        mutationFn: (body: ReportPasswordRequest) => api.lock(props.reportId, body),
        onSuccess: async () => {
            setServerError(null);
            await queryClient.invalidateQueries({ queryKey: reportQueryKey(api.mode, props.reportId) });
            await queryClient.invalidateQueries({ queryKey: listQueryKey(api.mode) });
            props.onClose();
        },
        onError: () => setServerError(t("admin.reports.editor.lock.unexpectedError")),
    });

    return (
        <Modal
            opened={true}
            onClose={props.onClose}
            title={t("admin.reports.editor.lock.lockTitle")}
            centered
        >
            <form onSubmit={form.onSubmit((v) => mutation.mutate({ password: v.password }))}>
                <Stack gap="sm">
                    <Text size="sm" c="dimmed">
                        {t("admin.reports.editor.lock.lockBody")}
                    </Text>
                    <PasswordInput
                        label={t("admin.reports.editor.lock.passwordLabel")}
                        withAsterisk
                        {...form.getInputProps("password")}
                    />
                    {serverError !== null && (
                        <Alert role="alert" icon={<IconAlertCircle size={16} />} color="red" variant="light">
                            {serverError}
                        </Alert>
                    )}
                    <Group justify="flex-end" gap="sm">
                        <Button variant="subtle" onClick={props.onClose}>
                            {t("admin.reports.editor.lock.cancel")}
                        </Button>
                        <Button type="submit" loading={mutation.isPending}>
                            {t("admin.reports.editor.lock.lockSubmit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}

function UnlockReportModal(props: { reportId: number; onClose: () => void }) {
    const { t } = useTranslation();
    const queryClient = useQueryClient();
    const api = useReportsApi();
    const [serverError, setServerError] = useState<string | null>(null);
    const form = useForm<ReportPasswordRequest>({
        initialValues: { password: "" },
        validate: {
            password: (v) =>
                v.trim().length === 0
                    ? t("admin.reports.editor.lock.passwordRequired")
                    : null,
        },
    });
    const mutation = useMutation({
        mutationFn: (body: ReportPasswordRequest) => api.unlock(props.reportId, body),
        onSuccess: async () => {
            setServerError(null);
            await queryClient.invalidateQueries({ queryKey: reportQueryKey(api.mode, props.reportId) });
            await queryClient.invalidateQueries({ queryKey: listQueryKey(api.mode) });
            props.onClose();
        },
        onError: (err) => {
            if (err instanceof Error && err.message.toLowerCase().includes("unauthorized")) {
                setServerError(t("admin.reports.editor.lock.wrongPassword"));
            } else {
                // ApiError from the fetch helper carries the HTTP status.
                const status = (err as { status?: number } | null)?.status;
                if (status === 401) {
                    setServerError(t("admin.reports.editor.lock.wrongPassword"));
                } else {
                    setServerError(t("admin.reports.editor.lock.unexpectedError"));
                }
            }
        },
    });

    return (
        <Modal
            opened={true}
            onClose={props.onClose}
            title={t("admin.reports.editor.lock.unlockTitle")}
            centered
        >
            <form onSubmit={form.onSubmit((v) => mutation.mutate({ password: v.password }))}>
                <Stack gap="sm">
                    <Text size="sm" c="dimmed">
                        {t("admin.reports.editor.lock.unlockBody")}
                    </Text>
                    <PasswordInput
                        label={t("admin.reports.editor.lock.passwordLabel")}
                        withAsterisk
                        {...form.getInputProps("password")}
                    />
                    {serverError !== null && (
                        <Alert role="alert" icon={<IconAlertCircle size={16} />} color="red" variant="light">
                            {serverError}
                        </Alert>
                    )}
                    <Group justify="flex-end" gap="sm">
                        <Button variant="subtle" onClick={props.onClose}>
                            {t("admin.reports.editor.lock.cancel")}
                        </Button>
                        <Button type="submit" loading={mutation.isPending}>
                            {t("admin.reports.editor.lock.unlockSubmit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}

function DuplicateFromEditorModal(props: {
    report: { id: number; title: string };
    defaultOwner: string;
    onClose: () => void;
}) {
    const { t } = useTranslation();
    const queryClient = useQueryClient();
    const api = useReportsApi();
    const router = useRouter();
    const [serverError, setServerError] = useState<string | null>(null);
    const form = useForm<DuplicateReportRequest>({
        initialValues: {
            title: `Copy of ${props.report.title}`,
            ownerDisplayName: props.defaultOwner,
        },
        validate: {
            ownerDisplayName: (v) =>
                (v ?? "").trim().length === 0
                    ? t("admin.reports.editor.lock.ownerRequired")
                    : null,
        },
    });
    const mutation = useMutation({
        mutationFn: (body: DuplicateReportRequest) =>
            api.duplicate(props.report.id, body),
        onSuccess: async (dto) => {
            setServerError(null);
            await queryClient.invalidateQueries({ queryKey: listQueryKey(api.mode) });
            props.onClose();
            const prefix = api.mode === "author" ? "/reports" : "/admin/reports";
            void router.navigate({ to: `${prefix}/${dto.id}` });
        },
        onError: () => setServerError(t("admin.reports.editor.lock.unexpectedError")),
    });

    return (
        <Modal
            opened={true}
            onClose={props.onClose}
            title={t("admin.reports.editor.lock.duplicateTitle")}
            centered
        >
            <form
                onSubmit={form.onSubmit((v) =>
                    mutation.mutate({
                        title: (v.title ?? "").trim(),
                        ownerDisplayName: v.ownerDisplayName.trim(),
                    }),
                )}
            >
                <Stack gap="sm">
                    <TextInput
                        label={t("admin.reports.editor.lock.duplicateTitleField")}
                        {...form.getInputProps("title")}
                    />
                    {api.mode === "admin" && (
                        <TextInput
                            label={t("admin.reports.editor.lock.duplicateOwner")}
                            withAsterisk
                            {...form.getInputProps("ownerDisplayName")}
                        />
                    )}
                    {serverError !== null && (
                        <Alert role="alert" icon={<IconAlertCircle size={16} />} color="red" variant="light">
                            {serverError}
                        </Alert>
                    )}
                    <Group justify="flex-end" gap="sm">
                        <Button variant="subtle" onClick={props.onClose}>
                            {t("admin.reports.editor.lock.cancel")}
                        </Button>
                        <Button type="submit" loading={mutation.isPending}>
                            {t("admin.reports.editor.lock.duplicateSubmit")}
                        </Button>
                    </Group>
                </Stack>
            </form>
        </Modal>
    );
}

// -------------------- Export report card --------------------

/**
 * RC5 export panel: lets the admin pick a source + UTC window then
 * download the multi-tile report as XLSX or PDF. Every tile inside
 * the report is rendered against the same filter so the workbook /
 * document is a single consistent snapshot.
 */
function ExportReportCard(props: { detail: ReportDetailDto }) {
    const { t } = useTranslation();
    const reportId = props.detail.report.id;

    const sourcesQuery = useQuery({
        queryKey: ["sources"] as const,
        queryFn: fetchSources,
    });

    // Interpret the naive `datetime-local` inputs in the user's
    // configured time zone (Settings -> Timezone) rather than UTC.
    const timeZone = resolveTimeZone(
        usePreferencesStore((s) => s.timeZone),
    );

    // Default the window to "yesterday 00:00 -> today 00:00" in the
    // user's configured time zone. Working in the zone rather than
    // UTC means a user in JST who opens the dialog just after midnight
    // Tokyo time sees "yesterday" as the day that just ended locally,
    // not the previous UTC day.
    const chromeDefaults = readChromeDefaults(props.detail.report.chromeJson);
    const defaultWindowPreset = chromeDefaults.defaultWindowPreset;

    // Seed the window from the report's saved default preset when the
    // author set one; otherwise fall back to "yesterday 00:00 -> today
    // 00:00" in the user's configured time zone.
    const defaults = useMemo(() => {
        if (defaultWindowPreset) {
            return resolveWindowPreset(defaultWindowPreset, timeZone);
        }
        // Today's Y-M-D as it appears in `timeZone`.
        const todayWall = instantIsoToWallClock(new Date().toISOString(), timeZone, "T");
        const todayDate = todayWall.slice(0, 10);
        // Compute yesterday's Y-M-D. Doing string decrement is brittle
        // around month boundaries, so anchor via a real Date at UTC
        // noon of `todayDate` (safe for any zone) and subtract a day.
        const anchor = new Date(`${todayDate}T12:00:00Z`);
        anchor.setUTCDate(anchor.getUTCDate() - 1);
        const yesterdayDate = anchor.toISOString().slice(0, 10);
        return {
            start: `${yesterdayDate}T00:00`,
            end: `${todayDate}T00:00`,
        };
    }, [timeZone, defaultWindowPreset]);

    const [sourceId, setSourceId] = useState<string | null>(
        chromeDefaults.defaultSourceId ?? null,
    );
    const [startLocal, setStartLocal] = useState<string>(defaults.start);
    const [endLocal, setEndLocal] = useState<string>(defaults.end);
    const [busy, setBusy] = useState<ReportExportFormat | null>(null);
    const [error, setError] = useState<string | null>(null);
    // F15 - PDF preview modal state.
    const [pdfPreviewOpen, setPdfPreviewOpen] = useState(false);

    // Auto-select the first available source once the query resolves.
    useEffect(() => {
        if (sourceId !== null) return;
        const first = sourcesQuery.data?.find((s: SourceInfo) => s.available) ?? sourcesQuery.data?.[0];
        if (first) setSourceId(first.id);
    }, [sourcesQuery.data, sourceId]);

    const sourceOptions = useMemo(
        () => (sourcesQuery.data ?? []).map((s: SourceInfo) => ({
            value: s.id,
            label: s.available ? s.displayName : `${s.displayName} (unavailable)`,
            disabled: !s.available,
        })),
        [sourcesQuery.data],
    );

    const canExport = sourceId !== null
        && startLocal.length > 0
        && endLocal.length > 0
        && startLocal < endLocal;

    // Precompute the UTC ISO instants used by the PDF preview URL
    // (below in JSX). Memoized so `wallClockToInstantIso`'s Intl
    // work doesn't run on every render — recomputes only when the
    // wall-clock input or the zone actually changes.
    const previewStartUtc = useMemo(
        () => wallClockToInstantIso(startLocal, timeZone),
        [startLocal, timeZone],
    );
    const previewEndUtc = useMemo(
        () => wallClockToInstantIso(endLocal, timeZone),
        [endLocal, timeZone],
    );

    async function handleExport(format: ReportExportFormat) {
        if (!canExport || sourceId === null) return;
        setBusy(format);
        setError(null);
        try {
            const startUtc = wallClockToInstantIso(startLocal, timeZone);
            const endUtc = wallClockToInstantIso(endLocal, timeZone);
            if (startUtc === null || endUtc === null) {
                setError(t("admin.reports.editor.export.errorPrefix"));
                return;
            }
            const filter: ReportExportFilter = {
                sourceId,
                startUtc,
                endUtc,
                timeZone,
            };
            await downloadReportExport(reportId, format, filter);
        }
        catch (err) {
            setError(err instanceof Error ? err.message : String(err));
        }
        finally {
            setBusy(null);
        }
    }

    return (
        <Card withBorder>
            <Stack gap="sm">
                <Group justify="space-between">
                    <Group gap="xs">
                        <IconDownload size={18} />
                        <Title order={4}>{t("admin.reports.editor.export.heading")}</Title>
                    </Group>
                </Group>
                <Text size="sm" c="dimmed">
                    {t("admin.reports.editor.export.description")}
                </Text>
                <Group grow align="flex-end">
                    <Select
                        label={t("admin.reports.editor.export.sourceLabel")}
                        data={sourceOptions}
                        value={sourceId}
                        onChange={setSourceId}
                        placeholder={t("admin.reports.editor.export.sourcePlaceholder")}
                        disabled={sourcesQuery.isPending || sourceOptions.length === 0}
                        allowDeselect={false}
                    />
                    <TextInput
                        type="datetime-local"
                        label={t("admin.reports.editor.export.startLabel")}
                        value={startLocal}
                        onChange={(e) => setStartLocal(e.currentTarget.value)}
                    />
                    <TextInput
                        type="datetime-local"
                        label={t("admin.reports.editor.export.endLabel")}
                        value={endLocal}
                        onChange={(e) => setEndLocal(e.currentTarget.value)}
                    />
                </Group>
                {error !== null && (
                    <Alert
                        role="alert"
                        icon={<IconAlertCircle size={16} />}
                        color="red"
                        variant="light"
                    >
                        {t("admin.reports.editor.export.errorPrefix")}: {error}
                    </Alert>
                )}
                <Group>
                    <Button
                        leftSection={<IconDownload size={14} />}
                        disabled={!canExport || busy !== null}
                        loading={busy === "xlsx"}
                        onClick={() => void handleExport("xlsx")}
                    >
                        {t("admin.reports.editor.export.downloadXlsx")}
                    </Button>
                    <Button
                        leftSection={<IconFileTypePdf size={14} />}
                        disabled={!canExport || busy !== null}
                        loading={busy === "pdf"}
                        onClick={() => void handleExport("pdf")}
                    >
                        {t("admin.reports.editor.export.downloadPdf")}
                    </Button>
                    <Button
                        leftSection={<IconFileTypeCsv size={14} />}
                        disabled={!canExport || busy !== null}
                        loading={busy === "csv"}
                        onClick={() => void handleExport("csv")}
                    >
                        {t("admin.reports.editor.export.downloadCsv")}
                    </Button>
                    <Button
                        variant="default"
                        leftSection={<IconEye size={14} />}
                        disabled={!canExport || busy !== null}
                        onClick={() => setPdfPreviewOpen(true)}
                        data-testid="admin-report-editor-preview-pdf"
                    >
                        {t("common.pdfPreview.openAction")}
                    </Button>
                </Group>
            </Stack>
            <PdfPreviewModal
                opened={pdfPreviewOpen}
                onClose={() => setPdfPreviewOpen(false)}
                pdfUrl={canExport && sourceId !== null
                    && previewStartUtc !== null && previewEndUtc !== null
                    ? reportExportUrl(reportId, "pdf", {
                        sourceId,
                        startUtc: previewStartUtc,
                        endUtc: previewEndUtc,
                        timeZone,
                    } satisfies ReportExportFilter)
                    : null}
                fallbackFilename={`report-${reportId}.pdf`}
            />
        </Card>
    );
}


