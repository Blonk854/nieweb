import { useMemo } from "react";
import { Alert, Badge, Group, Loader, Stack, Table, Text } from "@mantine/core";
import { IconAlertTriangle } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";
import type { TestedObjectRow } from "../../api/traceability";
import type { BoardHighlight } from "../BoardViewer/BoardViewer";
import { formatDefectBits } from "../../i18n/defectBits";

/**
 * TC5 Phase D — enriched failed-objects table used inside the
 * `/traceability/board` drill-down (and reused later for TC5-driven
 * DPMO / Pareto drill-ins). Renders every column called out in
 * `docs/phase-2.md` §7.5 TC5 spec:
 *
 * <ol>
 *   <li>Panel ID</li>
 *   <li>Board ID (=<code>cardIdOnPanel</code>)</li>
 *   <li>Ref. Des (=<code>topology</code>)</li>
 *   <li>Face</li>
 *   <li>Error type (decoded via {@link formatDefectBits})</li>
 *   <li>Part Number</li>
 *   <li>Package (=<code>jedecName</code>)</li>
 *   <li>Feeder</li>
 *   <li>Dev X (µm)</li>
 *   <li>Dev Y (µm)</li>
 *   <li>Dev θ (°)</li>
 *   <li>Dev S (%)</li>
 *   <li>Dev Thickness (µm)</li>
 *   <li>Repair result (enum: -2..3)</li>
 *   <li>Repair date (UTC)</li>
 *   <li>Repair comment (=<code>repairButtonComment</code> —
 *       operator button pressed)</li>
 *   <li>Repair operator (raw <code>Operator_Id</code>)</li>
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

function formatRepairDate(utc: number | null): string {
    if (utc === null || utc === undefined || !Number.isFinite(utc) || utc <= 0) {
        return "—";
    }
    // `Repair_Numeric_Date_Hour` is ANSI time_t (seconds since epoch UTC).
    return new Date(utc * 1000).toISOString().replace("T", " ").replace("Z", " UTC");
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
    } = props;

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
                <Table.ScrollContainer minWidth={1200} type="native">
                    <Table striped withTableBorder highlightOnHover>
                        <Table.Thead>
                            <Table.Tr>
                                <Table.Th>{t("traceability.board.failures.colPanelId")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colBoardId")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colRefDes")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colFace")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colErrorType")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colPartNumber")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colPackage")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colFeeder")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colDevX")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colDevY")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colDevTheta")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colDevSurface")}</Table.Th>
                                <Table.Th>{t("traceability.board.failures.colDevThickness")}</Table.Th>
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
                                        <Table.Td>{row.panelId}</Table.Td>
                                        <Table.Td>{row.cardIdOnPanel}</Table.Td>
                                        <Table.Td>{row.topology ?? "—"}</Table.Td>
                                        <Table.Td>{row.face ?? "—"}</Table.Td>
                                        <Table.Td>
                                            {formatDefectBits(row.errorTableAr, translateDefect) || "—"}
                                        </Table.Td>
                                        <Table.Td>{row.partNumberName ?? "—"}</Table.Td>
                                        <Table.Td>{row.jedecName ?? "—"}</Table.Td>
                                        <Table.Td>{row.feederName ?? "—"}</Table.Td>
                                        <Table.Td>{formatNumber(row.deltaXUm, 1)}</Table.Td>
                                        <Table.Td>{formatNumber(row.deltaYUm, 1)}</Table.Td>
                                        <Table.Td>{formatNumber(row.deltaThetaDeg, 2)}</Table.Td>
                                        <Table.Td>{formatNumber(row.deltaSurface, 1)}</Table.Td>
                                        <Table.Td>{formatNumber(row.deltaThicknessUm, 1)}</Table.Td>
                                        <Table.Td>
                                            {t(repairStateKey(row.repairState), {
                                                defaultValue: String(row.repairState ?? "—"),
                                            })}
                                        </Table.Td>
                                        <Table.Td>{formatRepairDate(row.repairUtc)}</Table.Td>
                                        <Table.Td>{row.repairButtonComment ?? "—"}</Table.Td>
                                        <Table.Td>
                                            {row.repairOperatorId !== null
                                                ? String(row.repairOperatorId)
                                                : "—"}
                                        </Table.Td>
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
