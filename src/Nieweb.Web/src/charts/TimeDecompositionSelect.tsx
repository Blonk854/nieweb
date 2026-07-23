import { useMemo } from "react";
import { Select, type SelectProps } from "@mantine/core";
import { useTranslation } from "react-i18next";

import {
    TIME_BUCKETS,
    type TimeBucket,
} from "./timeDecomposition";

/**
 * Shared time-decomposition selector — F12 in `docs/phase-2.md`
 * §7.9. Every chart tile that offers a "By day / By shift / By
 * hour" axis reads / writes a {@link TimeBucket} and renders one
 * of these dropdowns so the picker looks and behaves identically
 * across Trend, Deviation, Pareto-over-time, Cp/Cpk histograms,
 * and any future report.
 *
 * Controlled: the parent owns the current {@link TimeBucket} and
 * receives a callback on change. When a caller wants to suppress
 * one of the buckets (e.g. a source that has no shift definition
 * cannot bucket by shift) it can pass an `exclude` list; the
 * dropdown then hides those entries and — if the current value is
 * hidden — surfaces the removed entry as a disabled option so the
 * user still understands why it is unavailable.
 */
export type TimeDecompositionSelectProps = {
    /** Currently selected bucket. */
    value: TimeBucket;
    /** Called on user selection. */
    onChange: (next: TimeBucket) => void;
    /** Optional buckets to hide (e.g. no shift configured). */
    exclude?: readonly TimeBucket[];
    /**
     * Optional buckets that are visible but not selectable, with a
     * `(disabled)` marker. Useful when a source *could* support a
     * bucket but the current query doesn't (missing tolerance
     * values, for example). Buckets that appear in both `exclude`
     * and `disable` are hidden.
     */
    disable?: readonly TimeBucket[];
    /** Show a label above the input. Defaults to `true`. */
    withLabel?: boolean;
    /** Optional override for the label. */
    label?: string;
    /** data-testid root (defaults to `time-decomposition-select`). */
    testId?: string;
    /** Passed through to Mantine's underlying Select. */
    selectProps?: Omit<
        SelectProps,
        "data" | "value" | "onChange" | "label"
    >;
};

const DEFAULT_TEST_ID = "time-decomposition-select";

export function TimeDecompositionSelect(props: TimeDecompositionSelectProps) {
    const { t } = useTranslation();

    const excluded = useMemo(
        () => new Set(props.exclude ?? []),
        [props.exclude],
    );
    const disabled = useMemo(
        () => new Set(props.disable ?? []),
        [props.disable],
    );

    const options = useMemo(() => {
        const items = TIME_BUCKETS.filter((b) => !excluded.has(b)).map(
            (bucket) => ({
                value: bucket,
                label: t(`charts.timeDecomposition.buckets.${bucket}`),
                disabled: disabled.has(bucket),
            }),
        );
        // If the current value has been excluded, surface it at the
        // top as a disabled option so the user sees why the dropdown
        // "moved on" without them touching it.
        if (excluded.has(props.value)) {
            items.unshift({
                value: props.value,
                label: `${t(`charts.timeDecomposition.buckets.${props.value}`)} (${t("charts.timeDecomposition.unavailable")})`,
                disabled: true,
            });
        }
        return items;
    }, [excluded, disabled, props.value, t]);

    const labelText = props.withLabel === false
        ? undefined
        : props.label ?? t("charts.timeDecomposition.label");

    return (
        <Select
            label={labelText}
            data={options}
            value={props.value}
            onChange={(v) => {
                if (v && !disabled.has(v as TimeBucket)) {
                    onValidChange(v as TimeBucket, props.onChange);
                }
            }}
            allowDeselect={false}
            searchable
            data-testid={props.testId ?? DEFAULT_TEST_ID}
            {...props.selectProps}
        />
    );
}

// Small indirection keeps the `onChange` reference stable for
// wrappers that memoise on it — Mantine's inline onChange is
// recreated per render otherwise.
function onValidChange(next: TimeBucket, cb: (next: TimeBucket) => void) {
    cb(next);
}
