/**
 * i18n key resolvers for the Vision3D CR4 / CR5 Superviseur
 * `PANELS.Panel_Status` and `CARDS.Card_Status` enums. Kept in a
 * dedicated module so it can be reused by any traceability /
 * yield surface that renders a status without importing the whole
 * board-trace route.
 *
 * Canonical values (identical for panels and cards) — see
 * `Database fields and constants (Vision3D CR4).pdf` §5.1/§5.2 and
 * the `vit-aoi-database` skill:
 *
 * | Value | Meaning                                         | Sigmalink label   |
 * |-------|-------------------------------------------------|-------------------|
 * | -2    | Still faulty after review                       | `KO_OPERATOR`     |
 * | -1    | Faulty after inspection (not yet reviewed)      | `KO`              |
 * |  0    | Not inspected                                   | `NOT_INSPECTED`   |
 * |  1    | Good after inspection                           | `OK`              |
 * |  2    | Good — all defects were dummy faults            | `OK_OPERATOR`     |
 * |  3    | Good after review (repaired)                    | `OK_REPAIRED`     |
 *
 * The `koOperator` / `okOperator` / `okRepaired` key names mirror
 * Sigmalink's `KO_OPERATOR` / `OK_OPERATOR` / `OK_REPAIRED` so line
 * engineers who cross-reference the two apps can align semantics.
 * The display strings themselves are shop-floor terminology set by
 * each locale bundle.
 *
 * <h3>"Skipped" derivation from the anomaly bit-field</h3>
 *
 * <p>`Card_Status = 0` ("Not inspected") is an <b>aggregate</b>
 * outcome: fiducial fail, ejection, wash, invalidation, axis error,
 * and intentional skip all end up here. Vieweb deliberately kept
 * the enum flat and layered the disambiguation on top by reading
 * `Anomaly_BR/AR` bit 9 (value 256 = "Skipped sub-panel" per
 * `vit-aoi-database` §CARDS.Anomaly_BR). We do the same via
 * {@link panelStatusOrSkippedKey} / {@link cardStatusOrSkippedKey}
 * so a supplier-marked-bad subpanel reads as "Skipped" while a
 * fiducial fail or an ejected subpanel still reads as
 * "Not inspected".</p>
 */

/**
 * `CARDS.Anomaly_BR / AR` bit 9 (value 256) — the AOI's signal that
 * this sub-panel was intentionally skipped (supplier bad-mark
 * detected by the machine, operator-invoked whole-panel skip, or
 * MES-driven skip). Same bit on `PANELS.Anomaly_BR` means "panel
 * not inspected because all sub-panels were skipped".
 */
export const SKIPPED_ANOMALY_BIT = 256;

/** True when either the before-review or after-review anomaly field carries the skip bit. */
function hasSkipBit(anomalyBr: number | null | undefined, anomalyAr: number | null | undefined): boolean {
    const br = typeof anomalyBr === "number" ? anomalyBr : 0;
    const ar = typeof anomalyAr === "number" ? anomalyAr : 0;
    return ((br | ar) & SKIPPED_ANOMALY_BIT) !== 0;
}

/**
 * Decode `PANELS.Panel_Status` into an i18n key under
 * `traceability.board.panelStatus.*`. Callers pass the numeric
 * status straight from the server DTO.
 */
export function panelStatusKey(status: number | null | undefined): string {
    switch (status) {
        case -2:
            return "traceability.board.panelStatus.koOperator";
        case -1:
            return "traceability.board.panelStatus.ko";
        case 0:
            return "traceability.board.panelStatus.notInspected";
        case 1:
            return "traceability.board.panelStatus.ok";
        case 2:
            return "traceability.board.panelStatus.okOperator";
        case 3:
            return "traceability.board.panelStatus.okRepaired";
        default:
            return "traceability.board.panelStatus.unknown";
    }
}

/**
 * Like {@link panelStatusKey} but disambiguates <code>status = 0</code>
 * ("Not inspected") into <code>panelStatus.skipped</code> when the
 * skip anomaly bit (256) is set on either `Anomaly_BR` or
 * `Anomaly_AR`. Every other status value passes through unchanged.
 */
export function panelStatusOrSkippedKey(
    status: number | null | undefined,
    anomalyBr: number | null | undefined,
    anomalyAr: number | null | undefined,
): string {
    if (status === 0 && hasSkipBit(anomalyBr, anomalyAr)) {
        return "traceability.board.panelStatus.skipped";
    }
    return panelStatusKey(status);
}

/**
 * Decode `CARDS.Card_Status` into an i18n key under
 * `traceability.board.cardStatus.*`. Card_Status uses the same
 * enum as Panel_Status.
 */
export function cardStatusKey(status: number | null | undefined): string {
    switch (status) {
        case -2:
            return "traceability.board.cardStatus.koOperator";
        case -1:
            return "traceability.board.cardStatus.ko";
        case 0:
            return "traceability.board.cardStatus.notInspected";
        case 1:
            return "traceability.board.cardStatus.ok";
        case 2:
            return "traceability.board.cardStatus.okOperator";
        case 3:
            return "traceability.board.cardStatus.okRepaired";
        default:
            return "traceability.board.cardStatus.unknown";
    }
}

/**
 * Like {@link cardStatusKey} but disambiguates <code>status = 0</code>
 * ("Not inspected") into <code>cardStatus.skipped</code> when the
 * skip anomaly bit (256) is set on either `Anomaly_BR` or
 * `Anomaly_AR`. Every other status value passes through unchanged.
 */
export function cardStatusOrSkippedKey(
    status: number | null | undefined,
    anomalyBr: number | null | undefined,
    anomalyAr: number | null | undefined,
): string {
    if (status === 0 && hasSkipBit(anomalyBr, anomalyAr)) {
        return "traceability.board.cardStatus.skipped";
    }
    return cardStatusKey(status);
}

