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
import { apiFetch } from "../api/client";

type SourceDescriptor = {
    id: string;
    displayName: string;
    schemaVersion?: string;
};

export function HomeRoute() {
    const { data, error, isPending } = useQuery({
        queryKey: ["sources"],
        queryFn: () => apiFetch<SourceDescriptor[]>("/api/sources"),
    });

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>Welcome to Nieweb</Title>
                <Text c="dimmed">
                    Phase 1 MVP scaffold. Head to{" "}
                    <Anchor component={Link} to="/report/panel-yield">
                        Panel Yield by Line
                    </Anchor>{" "}
                    to try the first report.
                </Text>
            </Stack>

            <Card withBorder padding="lg" radius="md">
                <Group justify="space-between" mb="sm">
                    <Title order={4}>Configured AOI sources</Title>
                    {isPending && <Loader size="xs" />}
                </Group>

                {error && (
                    <Alert
                        color="red"
                        icon={<IconAlertTriangle size={18} />}
                        title="Could not reach the API"
                        role="alert"
                    >
                        {error instanceof Error ? error.message : String(error)}
                    </Alert>
                )}

                {data && data.length === 0 && (
                    <Text c="dimmed">No sources configured.</Text>
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
                                            schema {s.schemaVersion}
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
