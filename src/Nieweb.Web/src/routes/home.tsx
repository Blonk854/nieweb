import { useQuery } from "@tanstack/react-query";
import {
    Alert,
    Anchor,
    Badge,
    Card,
    Group,
    List,
    Loader,
    Stack,
    Text,
    Title,
} from "@mantine/core";
import { Link } from "@tanstack/react-router";
import { IconAlertTriangle } from "@tabler/icons-react";
import { Trans, useTranslation } from "react-i18next";
import { apiFetch } from "../api/client";

type SourceDescriptor = {
    id: string;
    displayName: string;
    schemaVersion?: string;
};

export function HomeRoute() {
    const { t } = useTranslation();
    const { data, error, isPending } = useQuery({
        queryKey: ["sources"],
        queryFn: () => apiFetch<SourceDescriptor[]>("/api/sources"),
    });

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("home.title")}</Title>
                <Text c="dimmed">
                    <Trans
                        i18nKey="home.intro"
                        components={{
                            1: <Anchor component={Link} to="/report/panel-yield" />,
                        }}
                    />
                </Text>
            </Stack>

            <Card withBorder padding="lg" radius="md">
                <Group justify="space-between" mb="sm">
                    <Title order={4}>{t("home.sourcesCard")}</Title>
                    {isPending && <Loader size="xs" />}
                </Group>

                {error && (
                    <Alert
                        color="red"
                        icon={<IconAlertTriangle size={18} />}
                        title={t("home.sourcesErrorTitle")}
                        role="alert"
                    >
                        {error instanceof Error ? error.message : String(error)}
                    </Alert>
                )}

                {data && data.length === 0 && (
                    <Text c="dimmed">{t("home.sourcesEmpty")}</Text>
                )}

                {data && data.length > 0 && (
                    <List spacing="xs">
                        {data.map((s) => (
                            <List.Item key={s.id}>
                                <Group gap="xs">
                                    <Text fw={500}>{s.displayName}</Text>
                                    <Badge variant="light">{s.id}</Badge>
                                    {s.schemaVersion && (
                                        <Text size="sm" c="dimmed">
                                            {t("home.schemaLabel", {
                                                version: s.schemaVersion,
                                            })}
                                        </Text>
                                    )}
                                </Group>
                            </List.Item>
                        ))}
                    </List>
                )}
            </Card>
        </Stack>
    );
}
