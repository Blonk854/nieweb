import { Card, Group, Stack, Text, Title, Tooltip } from "@mantine/core";
import { IconClock, IconDatabase, IconGauge } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";
import { colorForFpy, DEFAULT_FPY_THRESHOLDS, FPY_BAND_COLORS, type FpyThresholds } from "../charts/fpyThresholds";
import { freshnessBand, relativeFromNow } from "./freshness";

/**
 * KPI header row for a report. Three cards, in reading order:
 *
 *  1. Total panels processed in the selected window.
 *  2. Overall FPY across all machines, coloured by the same band model
 *     the chart uses (green >= 99.5, amber 98.0-99.5, red < 98.0 by
 *     default; thresholds can be overridden per report).
 *  3. Source freshness - how recent the source's latest PANELS row is.
 *     Coloured green (< 1h), amber (< 24h), or red (older / unknown).
 *
 * The freshness card renders a relative string ("12 minutes ago") with
 * the absolute UTC ISO timestamp in a tooltip for auditability. `now`
 * is a prop so tests / print layouts can freeze the clock.
 */
export type KpiCardsProps = {
    totalPanels: number;
    overallFpyPercent: number;
    /** ISO-8601 UTC string, or null if the source has no PANELS rows yet. */
    latestPanelUtc: string | null;
    /** Human-readable source name shown on the freshness card. */
    sourceDisplayName: string;
    thresholds?: FpyThresholds;
    /** Injectable clock. Default: `new Date()`. */
    now?: Date;
};

export function KpiCards(props: KpiCardsProps) {
    const {
        totalPanels,
        overallFpyPercent,
        latestPanelUtc,
        sourceDisplayName,
        thresholds = DEFAULT_FPY_THRESHOLDS,
        now = new Date(),
    } = props;
    const { t, i18n } = useTranslation();

    const fpyColor = colorForFpy(overallFpyPercent, thresholds);
    const latestDate = latestPanelUtc ? new Date(latestPanelUtc) : null;
    const relative = latestDate ? relativeFromNow(latestDate, now) : null;
    const band = freshnessBand(latestDate, now);
    const bandColor =
        band === "green" ? FPY_BAND_COLORS.green : band === "amber" ? FPY_BAND_COLORS.amber : FPY_BAND_COLORS.red;

    const numberFmt = new Intl.NumberFormat(i18n.language);

    return (
        <Group grow align="stretch" gap="md">
            <KpiCard
                icon={<IconDatabase size={20} aria-hidden />}
                label={t("panelYield.kpi.totalPanels")}
                value={numberFmt.format(totalPanels)}
            />
            <KpiCard
                icon={<IconGauge size={20} aria-hidden />}
                label={t("panelYield.kpi.overallFpy")}
                value={`${overallFpyPercent.toFixed(2)}%`}
                valueColor={fpyColor}
                subtitle={t(`panelYield.kpi.band.${bandForFpy(overallFpyPercent, thresholds)}`)}
            />
            <KpiCard
                icon={<IconClock size={20} aria-hidden />}
                label={t("panelYield.kpi.freshness")}
                value={
                    relative
                        ? t(relative.key, relative.params)
                        : t("panelYield.kpi.unknownFreshness")
                }
                valueColor={bandColor}
                subtitle={sourceDisplayName}
                tooltip={latestDate ? latestDate.toISOString() : t("panelYield.kpi.noPanels")}
            />
        </Group>
    );
}

// Duplicated from fpyThresholds.bandFor to avoid a public dep between
// KpiCards and the "band" name (KpiCard only needs the string).
function bandForFpy(fpy: number, thresholds: FpyThresholds): "green" | "amber" | "red" {
    if (!Number.isFinite(fpy)) return "red";
    if (fpy >= thresholds.green) return "green";
    if (fpy >= thresholds.amber) return "amber";
    return "red";
}

function KpiCard(props: {
    icon: React.ReactNode;
    label: string;
    value: string;
    valueColor?: string;
    subtitle?: string;
    tooltip?: string;
}) {
    const { icon, label, value, valueColor, subtitle, tooltip } = props;
    const body = (
        <Card withBorder padding="md" radius="md" h="100%">
            <Stack gap={4}>
                <Group gap={6} c="dimmed">
                    {icon}
                    <Text size="sm" fw={500}>
                        {label}
                    </Text>
                </Group>
                <Title order={2} c={valueColor}>
                    {value}
                </Title>
                {subtitle ? (
                    <Text size="xs" c="dimmed">
                        {subtitle}
                    </Text>
                ) : null}
            </Stack>
        </Card>
    );
    return tooltip ? <Tooltip label={tooltip}>{body}</Tooltip> : body;
}
