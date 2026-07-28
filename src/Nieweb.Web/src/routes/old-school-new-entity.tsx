import {
    Alert,
    Button,
    Card,
    SimpleGrid,
    Stack,
    Text,
} from "@mantine/core";
import { useMutation } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { IconAlertCircle } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import { addMyReportEntity } from "../api/authorReports";
import { VwbFrame } from "../components/oldSchool/VwbFrame";
import { useSessionStore } from "../state/session";

type EntityKind = {
    tileType: string;
    titleKey: string;
    descKey: string;
    /** Default config for the new tile. */
    configJson: string;
};

const ACTIVE_KINDS: readonly EntityKind[] = [
    { tileType: "comment", titleKey: "oldSchool.newEntity.comment", descKey: "oldSchool.newEntity.commentDesc", configJson: "{}" },
    { tileType: "pareto", titleKey: "oldSchool.newEntity.chart", descKey: "oldSchool.newEntity.chartDesc", configJson: "{}" },
    { tileType: "panelYield", titleKey: "oldSchool.newEntity.table", descKey: "oldSchool.newEntity.tableDesc", configJson: "{}" },
];

const DISABLED_KINDS: readonly string[] = [
    "oldSchool.newEntity.msa",
    "oldSchool.newEntity.processCapability",
];

/**
 * `/old-school/reports/$id/new-entity` — the Vieweb "New entity"
 * picker. Comment / Chart / Table map to Nieweb tiles; MSA and Process
 * capability are shown disabled ("coming soon"), preserving the classic
 * layout while only enabling what our logic supports.
 */
export function OldSchoolNewEntityRoute() {
    const { t } = useTranslation();
    const navigate = useNavigate();
    const { id: idParam } = useParams({ strict: false }) as { id: string };
    const id = Number(idParam);
    const user = useSessionStore((s) => s.user);
    const canAuthor =
        (user?.roles.includes("Author") || user?.roles.includes("Admin")) ?? false;

    const addMutation = useMutation({
        mutationFn: (kind: EntityKind) =>
            addMyReportEntity(id, {
                tileType: kind.tileType,
                title: null,
                displayOrder: -1,
                configJson: kind.configJson,
            }),
        onSuccess: (entity) =>
            void navigate({
                to: "/old-school/reports/$id/entity/$entityId",
                params: { id: String(id), entityId: String(entity.id) },
            }),
    });

    if (!canAuthor) {
        return (
            <VwbFrame title={t("oldSchool.title")}>
                <Alert role="alert" icon={<IconAlertCircle size={16} />} color="red" variant="light">
                    {t("oldSchool.forbidden")}
                </Alert>
            </VwbFrame>
        );
    }

    return (
        <VwbFrame
            title={t("oldSchool.newEntity.heading")}
            crumbs={[
                { label: t("oldSchool.breadcrumbRoot"), to: "/old-school/reports" },
                { label: t("oldSchool.layout.heading"), to: `/old-school/reports/${id}` },
                { label: t("oldSchool.newEntity.heading") },
            ]}
            toolbar={
                <Button
                    size="xs"
                    variant="default"
                    component={Link}
                    to={`/old-school/reports/${id}`}
                >
                    {t("oldSchool.newEntity.cancel")}
                </Button>
            }
        >
            <Stack gap="sm">
                <Text size="xs" c="dimmed">
                    {t("oldSchool.newEntity.subtitle")}
                </Text>
                <SimpleGrid cols={{ base: 1, sm: 2, md: 3 }} spacing="sm">
                    {ACTIVE_KINDS.map((kind) => (
                        <Card
                            key={kind.tileType}
                            withBorder
                            padding="md"
                            data-testid={`new-entity-${kind.tileType}`}
                            style={{ cursor: "pointer" }}
                            onClick={() => addMutation.mutate(kind)}
                        >
                            <Text fw={600}>{t(kind.titleKey as "oldSchool.newEntity.chart")}</Text>
                            <Text size="xs" c="dimmed">
                                {t(kind.descKey as "oldSchool.newEntity.chartDesc")}
                            </Text>
                        </Card>
                    ))}
                    {DISABLED_KINDS.map((titleKey) => (
                        <Card
                            key={titleKey}
                            withBorder
                            padding="md"
                            style={{ opacity: 0.55 }}
                            data-testid={`new-entity-disabled-${titleKey}`}
                        >
                            <Text fw={600}>{t(titleKey as "oldSchool.newEntity.msa")}</Text>
                            <Text size="xs" c="dimmed">
                                {t("oldSchool.newEntity.comingSoon")}
                            </Text>
                        </Card>
                    ))}
                </SimpleGrid>
            </Stack>
        </VwbFrame>
    );
}
