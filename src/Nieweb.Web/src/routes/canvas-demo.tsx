import { useCallback, useMemo, useState } from "react";
import {
    Card,
    Group,
    Select,
    Stack,
    Text,
    Title,
} from "@mantine/core";
import { DateTimePicker } from "@mantine/dates";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import "@mantine/dates/styles.css";
import {
    fetchMachines,
    fetchProducts,
    fetchSources,
} from "../api/sources";
import {
    CanvasFilterProvider,
    type CanvasFilters,
} from "../components/canvas/FilterContext";
import {
    ReportCanvas,
    newTileId,
    type CanvasTile,
} from "../components/canvas/ReportCanvas";
import type { TileType } from "../components/canvas/tileTypes";
import { MultiSelectField } from "../components/MultiSelectField";
import {
    pickDefaultSourceId,
    type CanvasDemoSearch,
} from "./canvas-demo.search";
import {
    instantIsoToWallClock,
    wallClockToInstantIso,
} from "../i18n/zoneConverters";
import { resolveTimeZone, usePreferencesStore } from "../state/preferences";

/**
 * F10 canvas demo route (`/report/canvas-demo`).
 *
 * Hosts a single canvas-level filter form, a
 * `<CanvasFilterProvider>` that fans the filters out to every
 * tile, and a `<ReportCanvas>` that renders the ordered tile list.
 *
 * URL-first design (same story as Panel Yield / Pareto): source /
 * window / narrowing filters and the tile list all live in the
 * search params so a whole dashboard is bookmarkable. Local form
 * state hydrates once from the URL and pushes back on Submit.
 */
export function CanvasDemoRoute() {
    const { t } = useTranslation();
    const rawSearch = useSearch({ strict: false });
    const search = rawSearch as CanvasDemoSearch;
    const navigate = useNavigate();

    const sourcesQuery = useQuery({
        queryKey: ["sources"],
        queryFn: fetchSources,
    });
    const sources = useMemo(() => sourcesQuery.data ?? [], [sourcesQuery.data]);

    // Derive the effective source id in the URL, defaulting to the
    // first available source when the URL is blank. This keeps the
    // demo working out of the box without forcing the user to pick
    // a source manually every time.
    const effectiveSourceId = useMemo(
        () => search.sourceId ?? pickDefaultSourceId(sources),
        [search.sourceId, sources],
    );

    // ----- Local form state. Initialised once from the URL; each
    // control writes back into the URL immediately (canvas-scale
    // filters change infrequently and every tile re-queries on
    // change, so debouncing is unnecessary at this scope).
    // The naive `from`/`to` wall clocks are interpreted in the
    // user's configured time zone (Settings -> Timezone).
    const timeZone = resolveTimeZone(
        usePreferencesStore((s) => s.timeZone),
    );
    const [form, setForm] = useState<FormState>(() => searchToForm(search, timeZone));

    const machinesQuery = useQuery({
        queryKey: ["machines", effectiveSourceId],
        queryFn: () => fetchMachines(effectiveSourceId ?? ""),
        enabled: Boolean(effectiveSourceId),
    });
    const productsQuery = useQuery({
        queryKey: ["products", effectiveSourceId],
        queryFn: () => fetchProducts(effectiveSourceId ?? ""),
        enabled: Boolean(effectiveSourceId),
    });

    // ----- Tile list also lives in the URL, hydrated once on first
    // render and pushed back through `navigate` on every add /
    // reorder / remove. TileIds are regenerated per navigation —
    // that's fine because tile identity isn't persisted across page
    // reloads, only across the current session.
    const [tiles, setTiles] = useState<CanvasTile[]>(() =>
        (search.tiles ?? []).map((type) => ({ id: newTileId(), type })),
    );

    // ----- Push the current form + tile list into the URL. Called
    // on every user interaction; TanStack Router de-dupes identical
    // updates so calling this after a no-op change is safe.
    const pushSearch = useCallback(
        (nextForm: FormState, nextTiles: CanvasTile[]) => {
            const next: CanvasDemoSearch = {
                sourceId: nextForm.sourceId,
                startUtc: nextForm.from
                    ? (wallClockToInstantIso(nextForm.from, timeZone) ?? undefined)
                    : undefined,
                endUtc: nextForm.to
                    ? (wallClockToInstantIso(nextForm.to, timeZone) ?? undefined)
                    : undefined,
                machineIds:
                    nextForm.machineIds && nextForm.machineIds.length > 0
                        ? nextForm.machineIds
                        : undefined,
                productIds:
                    nextForm.productIds && nextForm.productIds.length > 0
                        ? nextForm.productIds
                        : undefined,
                tiles:
                    nextTiles.length > 0
                        ? nextTiles.map((tile) => tile.type)
                        : undefined,
            };
            navigate({ to: "/report/canvas-demo", search: next, replace: true });
        },
        [navigate, timeZone],
    );

    // Wrap the setter so the URL and local state move together.
    const handleTilesChange = useCallback(
        (nextTiles: CanvasTile[]) => {
            setTiles(nextTiles);
            pushSearch(form, nextTiles);
        },
        [form, pushSearch],
    );

    const patchForm = useCallback(
        (patch: Partial<FormState>) => {
            setForm((prev) => {
                const next = { ...prev, ...patch };
                pushSearch(next, tiles);
                return next;
            });
        },
        [pushSearch, tiles],
    );

    // Filters exposed to tiles via context. Rebuilt on every render
    // but reference-equal per shallow-equal input — the provider
    // memoises the wrapper object so tile subtrees don't churn.
    const canvasFilters: CanvasFilters = useMemo(
        () => ({
            sourceId: effectiveSourceId,
            startUtc: form.from
                ? (wallClockToInstantIso(form.from, timeZone) ?? undefined)
                : undefined,
            endUtc: form.to
                ? (wallClockToInstantIso(form.to, timeZone) ?? undefined)
                : undefined,
            machineIds:
                form.machineIds && form.machineIds.length > 0
                    ? form.machineIds
                    : undefined,
            productIds:
                form.productIds && form.productIds.length > 0
                    ? form.productIds
                    : undefined,
        }),
        [
            effectiveSourceId,
            form.from,
            form.to,
            form.machineIds,
            form.productIds,
            timeZone,
        ],
    );

    // The provider expects a `(filters) => void` setter; the demo
    // route drives the filters via the form + URL, so we adopt any
    // programmatic update by echoing it back through `patchForm`.
    const handleFiltersChange = useCallback(
        (next: CanvasFilters) => {
            patchForm({
                sourceId: next.sourceId,
                from: next.startUtc ? instantIsoToWallClock(next.startUtc, timeZone) : null,
                to: next.endUtc ? instantIsoToWallClock(next.endUtc, timeZone) : null,
                machineIds: next.machineIds ?? [],
                productIds: next.productIds ?? [],
            });
        },
        [patchForm, timeZone],
    );

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("canvas.title")}</Title>
                <Text c="dimmed">{t("canvas.subtitle")}</Text>
            </Stack>

            <Card withBorder padding="lg" radius="md">
                <Title order={4} mb="sm">
                    {t("canvas.filters.heading")}
                </Title>

                <Stack gap="md">
                    <Group grow align="flex-end">
                        <Select
                            label={t("canvas.filters.source")}
                            placeholder={t("canvas.filters.sourcePlaceholder")}
                            data={sources.map((s) => ({
                                value: s.id,
                                label: s.available
                                    ? s.displayName
                                    : `${s.displayName} (offline)`,
                            }))}
                            value={effectiveSourceId ?? null}
                            onChange={(value) =>
                                patchForm({
                                    sourceId: value ?? undefined,
                                    machineIds: [],
                                    productIds: [],
                                })
                            }
                            allowDeselect={false}
                            searchable
                        />
                    </Group>

                    <Group grow>
                        <DateTimePicker
                            label={t("canvas.filters.from")}
                            value={form.from}
                            onChange={(value) => patchForm({ from: value })}
                            valueFormat="YYYY-MM-DD HH:mm"
                            clearable
                        />
                        <DateTimePicker
                            label={t("canvas.filters.to")}
                            value={form.to}
                            onChange={(value) => patchForm({ to: value })}
                            valueFormat="YYYY-MM-DD HH:mm"
                            clearable
                        />
                    </Group>

                    <MultiSelectField
                        label={t("canvas.filters.machines")}
                        placeholder={t("canvas.filters.machinesPlaceholder")}
                        data={(machinesQuery.data ?? []).map((m) => ({
                            value: String(m.id),
                            label: `${m.name} (${m.typeName})`,
                        }))}
                        value={(form.machineIds ?? []).map(String)}
                        onChange={(vals) =>
                            patchForm({
                                machineIds: vals
                                    .map(Number)
                                    .filter(Number.isFinite),
                            })
                        }
                        disabled={!effectiveSourceId || machinesQuery.isPending}
                        searchable
                        clearable
                    />

                    <MultiSelectField
                        label={t("canvas.filters.products")}
                        placeholder={t("canvas.filters.productsPlaceholder")}
                        data={(productsQuery.data ?? []).map((p) => ({
                            value: String(p.id),
                            label: p.revision
                                ? `${p.name || `#${p.id}`} — ${p.revision}`
                                : p.name || `#${p.id}`,
                        }))}
                        value={(form.productIds ?? []).map(String)}
                        onChange={(vals) =>
                            patchForm({
                                productIds: vals
                                    .map(Number)
                                    .filter(Number.isFinite),
                            })
                        }
                        disabled={!effectiveSourceId || productsQuery.isPending}
                        searchable
                        clearable
                    />
                </Stack>
            </Card>

            <CanvasFilterProvider
                filters={canvasFilters}
                setFilters={handleFiltersChange}
            >
                <ReportCanvas
                    tiles={tiles}
                    onTilesChange={handleTilesChange}
                    onAddTile={(type: TileType) =>
                        handleTilesChange([
                            ...tiles,
                            { id: newTileId(), type },
                        ])
                    }
                />
            </CanvasFilterProvider>
        </Stack>
    );
}

// -------------------- form state helpers --------------------

type FormState = {
    sourceId?: string;
    /** "YYYY-MM-DD HH:mm" (wall clock in the user's timezone). */
    from: string | null;
    /** "YYYY-MM-DD HH:mm" (wall clock in the user's timezone). */
    to: string | null;
    machineIds: number[];
    productIds: number[];
};

function searchToForm(search: CanvasDemoSearch, timeZone: string): FormState {
    return {
        sourceId: search.sourceId,
        from: search.startUtc ? instantIsoToWallClock(search.startUtc, timeZone) : null,
        to: search.endUtc ? instantIsoToWallClock(search.endUtc, timeZone) : null,
        machineIds: search.machineIds ?? [],
        productIds: search.productIds ?? [],
    };
}
