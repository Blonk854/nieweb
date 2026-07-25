import { useMemo } from "react";
import { Alert, Badge, Group, Loader, Stack, Table, Text } from "@mantine/core";
import { IconAlertTriangle } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";
import type { TestedObjectRow } from "../../api/traceability";
import type { BoardHighlight } from "../BoardViewer/BoardViewer";
import { formatDefectBits } from "../../i18n/defectBits";
import { useDateTimeFormatter } from "../../i18n/formatters";

/**
 * TC5 Phase D — enriched failed-objects table used inside the
 * `/traceability/board` drill-down (and reused later for TC5-driven
 * DPMO / Pareto drill-ins). Columns (after the board-trace UI
 * refresh):
 *
 * <ol>
 *   <li>Subpanel # (=<code>cardIdOnPanel</code>)</li>
 *   <li>Ref. Des (=<code>topology</code>)</li>
 *   <li>Face</li>
 *   <li>Error type (decoded via {@link formatDefectBits})</li>
 *   <li>Part Number</li>
 *   <li>Dev X (µm)</li>
 *   <li>Dev Y (µm)</li>
 *   <li>Dev θ (°)</li>
 *   <li>Operator classification (=<code>repairState</code>, enum -2..3)</li>
 *   <li>Review date (=<code>repairUtc</code>, formatted with the
 *       user's timezone + 12-hour clock preference)</li>
 *   <li>Review action (=<code>repairButtonComment</code> — the
 *       button the operator pressed on the review PC)</li>
 *   <li>Review operator (resolved to a name via
 *       <code>operatorLookup</code>; falls back to the raw id)</li>
 *   <li>Operator comment (free-form
 *       <code>repairOperatorComment</code>)</li>
 * </ol>
 *
 * <h3>Row ↔ marker two-way binding</h3>
 *
 * <p>The parent owns the {@link BoardHighlight} state so that
 * clicking a marker on the {@code BoardViewer} focuses the matching
 * row and vice-versa. This component fires
 * <code>onRowClick</code> with a highlight identifier derived from
 * the row (<code>cardIdOnPanel</code> + <code>topology</code>). A
 * row is visually highlighted when its identifier matches
 * <code>primaryHighlight</code>.</p>
 *
 * <p>The rendered <code>topology</code> is a free-text ref.des.
 * When empty (no CAD attribute assignment yet), the row still
 * renders but is NOT click-selectable — it has no stable identifier
 * to bind to a marker.</p>
 *
 * <h3>False-call semantics</h3>
 *
 * <p>The server has already applied the
 * <code>errorTableAr &lt;&gt; 0</code> filter (TC5 Phase C endpoint
 * override) so rows whose false-call was cleared during review do
 * NOT appear here. The decoder therefore reads
 * <code>errorTableAr</code> (post-review defect bits), not the raw
 * pre-review <code>errorTable</code> — matches the Vieweb DPMO
 * definition (numerator is <b>real</b> defects).</p>
 */
export type FailedObjectsTableProps = {
    /** Failing tested-objects for one panel on one stage. */
    objects: readonly TestedObjectRow[];
    /** Two-way primary highlight identifier (subpanel + reference). */
    primaryHighlight?: BoardHighlight | null;
    /** Fired when the user clicks a row that has a stable identifier. */
    onRowClick?: (h: BoardHighlight) => void;
    /** Localised heading / label displayed above the table. */
    heading?: string;
    /** Optional stage tint used for the heading badge (post / pre). */
    stageTint?: "post" | "pre";
    /** Loading spinner overlay (fetch in progress). */
    isLoading?: boolean;
    /** Optional error banner replaces the table. */
    error?: string | null;
    /** Optional test-id root so the parent can distinguish stages. */
    testIdRoot?: string;
    /**
     * Optional id → name resolver for the Review operator column.
     * Rows fall back to the raw <code>repairOperatorId</code> string
     * when the lookup returns <code>undefined</code>, so this prop
     * can be omitted entirely on surfaces that don't have an
     * OPERATOR roster to hand.
     */
    operatorLookup?: (id: number) => string | undefined;
};

/** Decode `repairState` (-2..3) into an i18n key. */
function repairStateKey(state: number | null): string {
    switch (state) {
        case -2:
            return "traceability.board.failures.repairState.notInspected";
        case -1:
            return "traceability.board.failures.repairState.notDetected";
        case 0:
            return "traceability.board.failures.repairState.pending";
        case 1:
            return "traceability.board.failures.repairState.repaired";
        case 2:
            return "traceability.board.failures.repairState.falseCall";
        case 3:
            return "traceability.board.failures.repairState.confirmed";
        default:
            return "traceability.board.failures.repairState.unknown";
    }
}

function stageBadgeColor(tint: "post" | "pre" | undefined): string {
    if (tint === "pre") return "grape";
    if (tint === "post") return "red";
    return "gray";
}

function formatNumber(v: number | null, digits: number): string {
    if (v === null || v === undefined || !Number.isFinite(v)) return "—";
    return v.toFixed(digits);
}

function highlightId(row: TestedObjectRow): BoardHighlight | null {
    const ref = row.topology?.trim();
    if (!ref) return null;
    return { subpanelIndex: row.cardIdOnPanel, reference: ref };
}

function sameHighlight(
    a: BoardHighlight | null | undefined,
    b: BoardHighlight | null | undefined,
): boolean {
    if (!a || !b) return false;
    return a.subpanelIndex === b.subpanelIndex && a.reference === b.reference;
}

export function FailedObjectsTable(props: FailedObjectsTableProps) {
    const { t } = useTranslation();
    const {
        objects,
        primaryHighlight,
        onRowClick,
        heading,
        stageTint,
        isLoading,
        error,
        testIdRoot,
        operatorLookup,
    } = props;

    // Review dates are formatted with the user's timezone preference
    // (Settings → Time zone) and a 12-hour clock, matching every
    // other timestamp surface in the app. `Repair_Numeric_Date_Hour`
    // is ANSI time_t (seconds since epoch UTC).
    const reviewDateFormat = useDateTimeFormatter({
        dateStyle: "short",
        timeStyle: "medium",
    });
    const formatReviewDate = (utc: number | null): string => {
        if (utc === null || utc === undefined || !Number.isFinite(utc) || utc <= 0) {
            return "—";
        }
        return reviewDateFormat.format(new Date(utc * 1000));
    };

    // The decoder resolver bridges i18next + the framework-neutral
    // formatDefectBits helper. `defaultValue` is what i18next falls
    // back to when the key isn't defined (identical to the English
    // catalogue) — that guarantees the table stays readable even if
    // an i18n bundle is missing.
    const translateDefect = useMemo(
        () =>
            (key: string, fallback: string) =>
                t(key, { defaultValue: fallback }),
        [t],
    );

    if (error) {
        return (
            <Alert
                color="red"
                icon={<IconAlertTriangle size={16} />}
                role="alert"
                data-testid={testIdRoot ? `${testIdRoot}-error` : undefined}
                title={t("traceability.board.failures.errorTitle")}
            >
                {error}
            </Alert>
        );
    }

    if (isLoading) {
        return (
            <Group gap="xs" data-testid={testIdRoot ? `${testIdRoot}-loading` : undefined}>
                <Loader size="sm" />
                <Text c="dimmed" size="sm">
                    {t("traceability.board.failures.loading")}
                </Text>
            </Group>
        );
    }

    return (
        <Stack gap="xs" data-testid={testIdRoot}>
            {heading && (
                <Group gap="xs">
                    <Text fw={600}>{heading}</Text>
                    {stageTint && (
                        <Badge color={stageBadgeColor(stageTint)} variant="light" size="sm">
                            {stageTint === "post"
                                ? t("traceability.board.failures.stagePost")
                                : t("traceability.board.failures.stagePre")}
                        </Badge>
                    )}
                    <Badge variant="outline" size="sm">
                        {t("traceability.board.failures.rowCount", { count: objects.length })}
                    </Badge>
                </Group>
            )}

            {objects.length === 0 ? (
                <Text c="dimmed" size="sm" data-testid={testIdRoot ? `${testIdRoot}-empty` : undefined}>
                    {t("traceability.board.failures.empty")}
                </Text>
            ) : (
                <Table.ScrollContainer minWidth={880} type="native">
                    <Table striped withTableBorder highlightOnHover>
                        <Table.Thead>
                            <Table.Tr>
                                <Table.Th>{t("traceability.board.failures.colBoardId")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colRefDes")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colFace")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colErrorType")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colPartNumber")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colDevX")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colDevY")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colDevTheta")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colRepairResult")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colRepairDate")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colRepairComment")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colRepairOperator")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colOperatorComment")}</Table.Th>
                            </Table.Tr>
                        </Table.Thead>
                        <Table.Tbody>
                            {objects.map((row) => {
                                const hid = highlightId(row);
                                const isPrimary = sameHighlight(hid, primaryHighlight);
                                const clickable = hid !== null && onRowClick !== undefined;
                                const operatorText =
                                    row.repairOperatorId === null
                                        ? "—"
                                        : (operatorLookup?.(row.repairOperatorId)
                                            ?? String(row.repairOperatorId));
                                return (
                                    <Table.Tr
                                        key={`${row.cardIdOnPanel}:${row.objectId}`}
                                        data-testid={
                                            testIdRoot
                                                ? `${testIdRoot}-row-${row.cardIdOnPanel}-${row.objectId}`
                                                : undefined
                                        }
                                        data-selected={isPrimary ? "true" : undefined}
                                        style={
                                            clickable
                                                ? {
                                                    cursor: "pointer",
                                                    backgroundColor: isPrimary
                                                        ? "var(--mantine-color-yellow-1)"
                                                        : undefined,
                                                }
                                                : undefined
                                        }
                                        onClick={
                                            clickable
                                                ? () => onRowClick!(hid!)
                                                : undefined
                                        }
                                    >
                                        <Table.Td>{row.cardIdOnPanel}</Table.Td>
                                        <Table.Td>{row.topology ?? "—"}</Table.Td>
                                        <Table.Td>{row.face ?? "—"}</Table.Td>
                                        <Table.Td>
                                            {formatDefectBits(row.errorTableAr, translateDefect) || "—"}
                                        </Table.Td>
                                        <Table.Td>{row.partNumberName ?? "—"}</Table.Td>
                                        <Table.Td>{formatNumber(row.deltaXUm, 1)}</Table.Td>
                                        <Table.Td>{formatNumber(row.deltaYUm, 1)}</Table.Td>
                                        <Table.Td>{formatNumber(row.deltaThetaDeg, 2)}</Table.Td>
                                        <Table.Td>
                                            {t(repairStateKey(row.repairState), {
                                                defaultValue: String(row.repairState ?? "—"),
                                            })}
                                        </Table.Td>
                                        <Table.Td>{formatReviewDate(row.repairUtc)}</Table.Td>
                                        <Table.Td>{row.repairButtonComment ?? "—"}</Table.Td>
                                        <Table.Td>{operatorText}</Table.Td>
                                        <Table.Td>{row.repairOperatorComment ?? "—"}</Table.Td>
                                    </Table.Tr>
                                );
                            })}
                        </Table.Tbody>
                    </Table>
                </Table.ScrollContainer>
            )}
        </Stack>
    );
}
