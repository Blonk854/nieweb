import { ActionIcon, Card, Group, Menu, Stack, Text, Title, Tooltip } from "@mantine/core";
import {
    IconArrowDown,
    IconArrowUp,
    IconPlus,
    IconX,
} from "@tabler/icons-react";
import { useTranslation } from "react-i18next";
import { TILE_LABEL_KEYS, TILE_TYPES, type TileType } from "./tileTypes";
import { TILE_REGISTRY } from "./tiles/registry";

/**
 * A single tile inside the canvas. The tile type is the discriminator
 * that maps to a component via `TILE_REGISTRY`; the string form
 * flows through the URL (see `routes/canvas-demo.search.ts`).
 */
export type CanvasTile = {
    /**
     * Position-independent identifier used as a React `key`. Stable
     * across reorders so a tile's internal state (e.g. loaded chart
     * chunk) survives moving up/down.
     */
    id: string;
    type: TileType;
};

/**
 * Props for the reusable `<ReportCanvas>` component (F10).
 *
 * The canvas is intentionally *controlled*: the parent owns the
 * tile list and reacts to `onTilesChange` by pushing the new list
 * into the URL. This mirrors the pattern used elsewhere in the
 * SPA (Panel Yield / Pareto) and keeps the canvas bookmarkable.
 *
 * Canvas-level filters are read by every tile through
 * `useCanvasFilters()`; the canvas itself does not render a filter
 * form (that responsibility belongs to the parent route so we can
 * reuse the same form component across a future
 * report-editor / dashboard / preview screen).
 */
export type ReportCanvasProps = {
    tiles: CanvasTile[];
    onTilesChange: (tiles: CanvasTile[]) => void;
    /**
     * Callback fired when the user picks a tile from the palette.
     * Defaults to appending a new tile at the end of the list —
     * override this to insert at a specific slot for future
     * drag-add UX (RC2).
     */
    onAddTile?: (type: TileType) => void;
};

/**
 * Reusable "report canvas" component: a vertical stack of report
 * tiles with a palette to add new tiles, plus per-tile reorder
 * and remove controls.
 *
 * Uses simple Move-up / Move-down / Remove buttons rather than
 * HTML5 drag-drop. That trade-off:
 *
 * - keeps the component dependency-free (no `@dnd-kit`, no
 *   `react-beautiful-dnd`),
 * - stays keyboard-accessible by default,
 * - is trivially unit-testable with `userEvent.click`,
 * - and is a strict superset of what the current phase-2 backlog
 *   asks for (§7.9 F10 says "drag-drop" but §7.6 RC2 is where the
 *   full palette + drop-zone UX is scheduled — F10 provides the
 *   canvas + fanout foundation on which RC2 will build).
 */
export function ReportCanvas(props: ReportCanvasProps) {
    const { tiles, onTilesChange, onAddTile } = props;
    const { t } = useTranslation();

    function handleAdd(type: TileType): void {
        if (onAddTile) {
            onAddTile(type);
            return;
        }
        onTilesChange([...tiles, { id: newTileId(), type }]);
    }

    function handleRemove(index: number): void {
        const next = tiles.slice();
        next.splice(index, 1);
        onTilesChange(next);
    }

    function handleMove(index: number, direction: -1 | 1): void {
        const target = index + direction;
        if (target < 0 || target >= tiles.length) return;
        const next = tiles.slice();
        const [moved] = next.splice(index, 1);
        next.splice(target, 0, moved);
        onTilesChange(next);
    }

    return (
        <Stack gap="md">
            <Group justify="space-between" align="center">
                <Title order={4}>{t("canvas.heading")}</Title>
                <Menu shadow="md" position="bottom-end">
                    <Menu.Target>
                        <Tooltip label={t("canvas.palette.add")}>
                            <ActionIcon
                                variant="light"
                                color="blue"
                                aria-label={t("canvas.palette.add")}
                            >
                                <IconPlus size={18} />
                            </ActionIcon>
                        </Tooltip>
                    </Menu.Target>
                    <Menu.Dropdown>
                        <Menu.Label>{t("canvas.palette.heading")}</Menu.Label>
                        {TILE_TYPES.map((type) => (
                            <Menu.Item
                                key={type}
                                onClick={() => handleAdd(type)}
                            >
                                {t(TILE_LABEL_KEYS[type])}
                            </Menu.Item>
                        ))}
                    </Menu.Dropdown>
                </Menu>
            </Group>

            {tiles.length === 0 ? (
                <Card withBorder padding="lg" radius="md">
                    <Text c="dimmed" ta="center">
                        {t("canvas.emptyPrompt")}
                    </Text>
                </Card>
            ) : (
                tiles.map((tile, index) => (
                    <CanvasTileCard
                        key={tile.id}
                        tile={tile}
                        index={index}
                        total={tiles.length}
                        onMoveUp={() => handleMove(index, -1)}
                        onMoveDown={() => handleMove(index, 1)}
                        onRemove={() => handleRemove(index)}
                    />
                ))
            )}
        </Stack>
    );
}

function CanvasTileCard(props: {
    tile: CanvasTile;
    index: number;
    total: number;
    onMoveUp: () => void;
    onMoveDown: () => void;
    onRemove: () => void;
}) {
    const { t } = useTranslation();
    const { tile, index, total, onMoveUp, onMoveDown, onRemove } = props;
    const TileComponent = TILE_REGISTRY[tile.type];
    const title = t(TILE_LABEL_KEYS[tile.type]);

    return (
        <Card
            withBorder
            padding="md"
            radius="md"
            data-testid={`canvas-tile-${tile.type}`}
        >
            <Group justify="space-between" mb="sm">
                <Title order={5}>{title}</Title>
                <Group gap="xs">
                    <Tooltip label={t("canvas.tile.moveUp")}>
                        <ActionIcon
                            variant="subtle"
                            aria-label={t("canvas.tile.moveUp")}
                            disabled={index === 0}
                            onClick={onMoveUp}
                        >
                            <IconArrowUp size={16} />
                        </ActionIcon>
                    </Tooltip>
                    <Tooltip label={t("canvas.tile.moveDown")}>
                        <ActionIcon
                            variant="subtle"
                            aria-label={t("canvas.tile.moveDown")}
                            disabled={index === total - 1}
                            onClick={onMoveDown}
                        >
                            <IconArrowDown size={16} />
                        </ActionIcon>
                    </Tooltip>
                    <Tooltip label={t("canvas.tile.remove")}>
                        <ActionIcon
                            variant="subtle"
                            color="red"
                            aria-label={t("canvas.tile.remove")}
                            onClick={onRemove}
                        >
                            <IconX size={16} />
                        </ActionIcon>
                    </Tooltip>
                </Group>
            </Group>
            <TileComponent />
        </Card>
    );
}

/**
 * Simple monotonic-ish tile id. Not cryptographically unique but
 * unique-enough for React `key` stability across a single session —
 * the URL-driven regeneration on reload doesn't need cross-session
 * uniqueness because tile identity is not persisted.
 */
export function newTileId(): string {
    return `t-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}
