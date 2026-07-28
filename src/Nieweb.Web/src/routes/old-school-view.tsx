import { useEffect, useMemo, useState } from "react";
import {
    Alert,
    Button,
    Group,
    Select,
    Stack,
    Text,
    TextInput,
} from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { IconAlertCircle, IconFileTypeCsv, IconFileTypePdf, IconTable } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import { getMyReport } from "../api/authorReports";
import { fetchSources, type SourceInfo } from "../api/sources";
import {
    downloadReportExport,
    reportExportUrl,
    type ReportExportFilter,
    type ReportExportFormat,
} from "../api/reportExport";
import { readChromeDefaults, resolveWindowPreset } from "../components/reportConfig/reportChrome";
import { PdfPreviewModal } from "../components/PdfPreviewModal";
import { instantIsoToWallClock, wallClockToInstantIso } from "../i18n/zoneConverters";
import { VwbFrame, VwbSection, oldSchoolStyles as styles } from "../components/oldSchool/VwbFrame";
import { resolveTimeZone, usePreferencesStore } from "../state/preferences";
import { useSessionStore } from "../state/session";

function entityLabelKey(tileType: string): string {
    switch (tileType) {
        case "pareto":
            return "oldSchool.newEntity.chart";
        case "panelYield":
            return "oldSchool.newEntity.table";
        default:
            return "oldSchool.newEntity.comment";
    }
}

/**
 * `/old-school/reports/$id/view` — renders the saved report through the
 * existing server-side export/preview path, which honours each entity's
 * per-tile filters. Exposes a source + window picker and CSV / XLSX /
 * PDF export plus an inline PDF preview.
 */
export function OldSchoolViewRoute() {
    const { t } = useTranslation();
    const { id: idParam } = useParams({ strict: false }) as { id: string };
    const id = Number(idParam);
    const user = useSessionStore((s) => s.user);
    const canAuthor =
        (user?.roles.includes("Author") || user?.roles.includes("Admin")) ?? false;
    const timeZone = resolveTimeZone(usePreferencesStore((s) => s.timeZone));

    const detailQuery = useQuery({
        queryKey: ["oldSchool", "report", id],
        queryFn: () => getMyReport(id),
        enabled: canAuthor && Number.isFinite(id),
    });
    const sourcesQuery = useQuery({ queryKey: ["sources"], queryFn: fetchSources });

    const chrome = detailQuery.data
        ? readChromeDefaults(detailQuery.data.report.chromeJson)
        : undefined;

    const windowDefault = useMemo(() => {
        if (chrome?.defaultWindowPreset) {
            return resolveWindowPreset(chrome.defaultWindowPreset, timeZone);
        }
        const todayWall = instantIsoToWallClock(new Date().toISOString(), timeZone, "T");
        const todayDate = todayWall.slice(0, 10);
        const anchor = new Date(`${todayDate}T12:00:00Z`);
        anchor.setUTCDate(anchor.getUTCDate() - 1);
        const yesterday = anchor.toISOString().slice(0, 10);
        return { start: `${yesterday}T00:00`, end: `${todayDate}T00:00` };
    }, [chrome?.defaultWindowPreset, timeZone]);

    const [sourceId, setSourceId] = useState<string | null>(null);
    const [startLocal, setStartLocal] = useState("");
    const [endLocal, setEndLocal] = useState("");
    const [busy, setBusy] = useState<ReportExportFormat | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [previewOpen, setPreviewOpen] = useState(false);

    // Seed the window / source from chrome defaults once data is ready.
    useEffect(() => {
        setStartLocal((prev) => (prev.length === 0 ? windowDefault.start : prev));
        setEndLocal((prev) => (prev.length === 0 ? windowDefault.end : prev));
    }, [windowDefault]);
    useEffect(() => {
        if (sourceId !== null) return;
        const fromChrome = chrome?.defaultSourceId;
        const first = sourcesQuery.data?.find((s: SourceInfo) => s.available) ?? sourcesQuery.data?.[0];
        if (fromChrome) setSourceId(fromChrome);
        else if (first) setSourceId(first.id);
    }, [sourcesQuery.data, chrome?.defaultSourceId, sourceId]);

    const sourceOptions = (sourcesQuery.data ?? []).map((s: SourceInfo) => ({
        value: s.id,
        label: s.available ? s.displayName : `${s.displayName} (unavailable)`,
        disabled: !s.available,
    }));

    const startUtc = useMemo(() => wallClockToInstantIso(startLocal, timeZone), [startLocal, timeZone]);
    const endUtc = useMemo(() => wallClockToInstantIso(endLocal, timeZone), [endLocal, timeZone]);
    const canExport = sourceId !== null && startUtc !== null && endUtc !== null && startLocal < endLocal;

    const exportFilter = (): ReportExportFilter | null => {
        if (sourceId === null || startUtc === null || endUtc === null) return null;
        return { sourceId, startUtc, endUtc, timeZone };
    };

    async function handleExport(format: ReportExportFormat) {
        const filter = exportFilter();
        if (!filter) return;
        setBusy(format);
        setError(null);
        try {
            await downloadReportExport(id, format, filter);
        } catch (err) {
            setError(err instanceof Error ? err.message : String(err));
        } finally {
            setBusy(null);
        }
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

    const detail = detailQuery.data;
    const previewFilter = exportFilter();

    return (
        <VwbFrame
            title={detail ? `${t("oldSchool.view.heading")} — ${detail.report.title}` : t("oldSchool.view.heading")}
            crumbs={[
                { label: t("oldSchool.breadcrumbRoot"), to: "/old-school/reports" },
                { label: t("oldSchool.layout.heading"), to: `/old-school/reports/${id}` },
                { label: t("oldSchool.view.heading") },
            ]}
            toolbar={
                <Group gap="xs">
                    <Button
                        size="xs"
                        variant="default"
                        component={Link}
                        to={`/old-school/reports/${id}`}
                    >
                        {t("oldSchool.view.back")}
                    </Button>
                </Group>
            }
        >
            <Stack gap="md">
                <VwbSection heading={t("oldSchool.view.heading")}>
                    <Group align="flex-end" gap="sm" wrap="wrap">
                        <Select
                            label="Source"
                            data={sourceOptions}
                            value={sourceId}
                            onChange={setSourceId}
                            w={200}
                        />
                        <TextInput
                            type="datetime-local"
                            label="From"
                            value={startLocal}
                            onChange={(e) => setStartLocal(e.currentTarget.value)}
                        />
                        <TextInput
                            type="datetime-local"
                            label="To"
                            value={endLocal}
                            onChange={(e) => setEndLocal(e.currentTarget.value)}
                        />
                    </Group>
                    <Group gap="xs" mt="sm">
                        <Button
                            size="xs"
                            leftSection={<IconTable size={14} />}
                            variant="light"
                            disabled={!canExport}
                            loading={busy === "xlsx"}
                            onClick={() => handleExport("xlsx")}
                        >
                            XLSX
                        </Button>
                        <Button
                            size="xs"
                            leftSection={<IconFileTypeCsv size={14} />}
                            variant="light"
                            disabled={!canExport}
                            loading={busy === "csv"}
                            onClick={() => handleExport("csv")}
                        >
                            CSV
                        </Button>
                        <Button
                            size="xs"
                            leftSection={<IconFileTypePdf size={14} />}
                            variant="light"
                            disabled={!canExport}
                            onClick={() => setPreviewOpen(true)}
                        >
                            PDF
                        </Button>
                    </Group>
                    {error ? (
                        <Alert color="red" variant="light" mt="sm">
                            {error}
                        </Alert>
                    ) : null}
                </VwbSection>

                {detail && detail.entities.length > 0 ? (
                    <table className={styles.table}>
                        <tbody>
                            {[...detail.entities]
                                .sort((a, b) => a.displayOrder - b.displayOrder)
                                .map((entity) => (
                                    <tr key={entity.id}>
                                        <td style={{ width: 120 }}>
                                            <span className={styles.entityTag}>
                                                {t(entityLabelKey(entity.tileType) as "oldSchool.newEntity.chart")}
                                            </span>
                                        </td>
                                        <td>
                                            {entity.title ??
                                                t(entityLabelKey(entity.tileType) as "oldSchool.newEntity.chart")}
                                        </td>
                                    </tr>
                                ))}
                        </tbody>
                    </table>
                ) : (
                    <Text size="sm" c="dimmed">
                        {t("oldSchool.view.empty")}
                    </Text>
                )}
            </Stack>

            <PdfPreviewModal
                opened={previewOpen}
                onClose={() => setPreviewOpen(false)}
                pdfUrl={previewFilter ? reportExportUrl(id, "pdf", previewFilter) : null}
                fallbackFilename={`report-${id}.pdf`}
            />
        </VwbFrame>
    );
}
