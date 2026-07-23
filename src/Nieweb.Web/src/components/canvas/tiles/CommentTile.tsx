import { Card, Stack, Text, Title } from "@mantine/core";
import { IconMessage2 } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

/**
 * Comment tile placeholder for `<ReportCanvas>` (docs/phase-2.md
 * §7.6 <c>RC6</c>).
 *
 * The comment tile carries free-text markdown content stored in
 * the tile's <c>ConfigJson</c>. The canvas today does not plumb
 * per-tile config through to <c>&lt;ReportCanvas&gt;</c> — it only
 * renders live data tiles — so this component intentionally shows
 * a static placeholder telling the user that the comment content
 * is composed in the report editor and rendered in the XLSX / PDF
 * exports. When the canvas gains per-tile config props (planned
 * alongside a "run saved report" route in a later backlog item)
 * this component becomes a real markdown renderer.
 */
export function CommentTile() {
    const { t } = useTranslation();
    return (
        <Card withBorder padding="md" radius="md">
            <Stack gap="xs">
                <Title order={5} c="dimmed">
                    <IconMessage2 size={16} style={{ verticalAlign: "middle", marginRight: 4 }} />
                    {t("canvas.tiles.comment.title")}
                </Title>
                <Text size="sm" c="dimmed">
                    {t("canvas.tiles.comment.placeholder")}
                </Text>
            </Stack>
        </Card>
    );
}
