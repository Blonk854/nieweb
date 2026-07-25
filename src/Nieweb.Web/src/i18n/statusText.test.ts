import { describe, expect, it } from "vitest";
import {
    SKIPPED_ANOMALY_BIT,
    cardStatusKey,
    cardStatusOrSkippedKey,
    panelStatusKey,
    panelStatusOrSkippedKey,
} from "./statusText";

describe("panelStatusKey / cardStatusKey", () => {
    const cases: Array<[number | null | undefined, string, string]> = [
        [-2, "koOperator", "koOperator"],
        [-1, "ko", "ko"],
        [0, "notInspected", "notInspected"],
        [1, "ok", "ok"],
        [2, "okOperator", "okOperator"],
        [3, "okRepaired", "okRepaired"],
        [99, "unknown", "unknown"],
        [null, "unknown", "unknown"],
        [undefined, "unknown", "unknown"],
    ];

    it.each(cases)("status=%s → %s / %s", (status, panelSuffix, cardSuffix) => {
        expect(panelStatusKey(status)).toBe(`traceability.board.panelStatus.${panelSuffix}`);
        expect(cardStatusKey(status)).toBe(`traceability.board.cardStatus.${cardSuffix}`);
    });
});

describe("panelStatusOrSkippedKey", () => {
    it("returns .skipped when status=0 and skip bit set on Anomaly_BR", () => {
        expect(panelStatusOrSkippedKey(0, SKIPPED_ANOMALY_BIT, 0)).toBe(
            "traceability.board.panelStatus.skipped",
        );
    });

    it("returns .skipped when status=0 and skip bit set on Anomaly_AR only", () => {
        expect(panelStatusOrSkippedKey(0, 0, SKIPPED_ANOMALY_BIT)).toBe(
            "traceability.board.panelStatus.skipped",
        );
    });

    it("returns .skipped when status=0 and skip bit is mixed with other anomaly bits", () => {
        // bit 1 (Fiducial fail) + bit 9 (Skipped) both set — Vieweb's rule is that the
        // presence of the skip bit at Panel_Status=0 disambiguates to Skipped.
        expect(panelStatusOrSkippedKey(0, SKIPPED_ANOMALY_BIT | 1, 0)).toBe(
            "traceability.board.panelStatus.skipped",
        );
    });

    it("returns .notInspected when status=0 and skip bit NOT set (fiducial fail / ejection / wash)", () => {
        expect(panelStatusOrSkippedKey(0, 1, 0)).toBe(
            "traceability.board.panelStatus.notInspected",
        );
        expect(panelStatusOrSkippedKey(0, 0, 0)).toBe(
            "traceability.board.panelStatus.notInspected",
        );
    });

    it("does NOT trip on the skip bit for non-zero statuses", () => {
        // Skip disambiguation is only defined at Panel_Status = 0. If the DB
        // ever reports skip-bit + KO_OPERATOR we must show the KO_OPERATOR
        // label, not "Skipped".
        expect(panelStatusOrSkippedKey(-2, SKIPPED_ANOMALY_BIT, 0)).toBe(
            "traceability.board.panelStatus.koOperator",
        );
        expect(panelStatusOrSkippedKey(1, SKIPPED_ANOMALY_BIT, 0)).toBe(
            "traceability.board.panelStatus.ok",
        );
    });

    it("tolerates null / undefined anomaly fields", () => {
        expect(panelStatusOrSkippedKey(0, null, null)).toBe(
            "traceability.board.panelStatus.notInspected",
        );
        expect(panelStatusOrSkippedKey(0, undefined, undefined)).toBe(
            "traceability.board.panelStatus.notInspected",
        );
        expect(panelStatusOrSkippedKey(1, null, undefined)).toBe(
            "traceability.board.panelStatus.ok",
        );
    });
});

describe("cardStatusOrSkippedKey", () => {
    it("returns .skipped when Card_Status=0 and skip bit set on Anomaly_BR", () => {
        expect(cardStatusOrSkippedKey(0, SKIPPED_ANOMALY_BIT, 0)).toBe(
            "traceability.board.cardStatus.skipped",
        );
    });

    it("returns .skipped when Card_Status=0 and skip bit set on Anomaly_AR only", () => {
        // e.g. subpanel was marked skipped during review even though it was
        // originally inspected — mirrors the AR half of the same rule.
        expect(cardStatusOrSkippedKey(0, 0, SKIPPED_ANOMALY_BIT)).toBe(
            "traceability.board.cardStatus.skipped",
        );
    });

    it("returns .notInspected when Card_Status=0 and skip bit NOT set", () => {
        expect(cardStatusOrSkippedKey(0, 0, 0)).toBe(
            "traceability.board.cardStatus.notInspected",
        );
        expect(cardStatusOrSkippedKey(0, 2, 4)).toBe(
            "traceability.board.cardStatus.notInspected",
        );
    });

    it("passes through non-zero statuses unchanged even if skip bit set", () => {
        expect(cardStatusOrSkippedKey(1, SKIPPED_ANOMALY_BIT, 0)).toBe(
            "traceability.board.cardStatus.ok",
        );
        expect(cardStatusOrSkippedKey(-1, SKIPPED_ANOMALY_BIT, SKIPPED_ANOMALY_BIT)).toBe(
            "traceability.board.cardStatus.ko",
        );
    });

    it("tolerates null / undefined anomaly fields", () => {
        expect(cardStatusOrSkippedKey(0, null, null)).toBe(
            "traceability.board.cardStatus.notInspected",
        );
        expect(cardStatusOrSkippedKey(0, undefined, undefined)).toBe(
            "traceability.board.cardStatus.notInspected",
        );
    });
});

describe("SKIPPED_ANOMALY_BIT", () => {
    it("is bit 9 (value 256) per CR4 CARDS.Anomaly_BR spec", () => {
        expect(SKIPPED_ANOMALY_BIT).toBe(256);
        expect(SKIPPED_ANOMALY_BIT).toBe(1 << 8);
    });
});
