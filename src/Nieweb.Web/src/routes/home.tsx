import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
    ActionIcon,
    Alert,
    Anchor,
    Badge,
    Card,
    Group,
    List,
    Loader,
    SimpleGrid,
    Stack,
    Text,
    Title,
    Tooltip,
} from "@mantine/core";
import { Link } from "@tanstack/react-router";
import { IconAlertTriangle, IconLock, IconPin, IconPinnedOff } from "@tabler/icons-react";
import { Trans, useTranslation } from "react-i18next";
import { fetchSources } from "../api/sources";
import { listHomeReports, type HomeReportDto } from "../api/homeReports";
import { unpinAdminReport } from "../api/adminReports";
import { relativeFromNow } from "../components/freshness";
import { ApiErrorAlert } from "../components/ApiErrorAlert";
import { BarcodeLookupCard } from "../components/BarcodeLookupCard";
import { useSessionStore } from "../state/session";

export function HomeRoute() {
    const { t } = useTranslation();
    const user = useSessionStore((s) => s.user);
    const isAdmin = user?.roles.includes("Admin") ?? false;
    const queryClient = useQueryClient();
    const { data, error, isPending } = useQuery({
        queryKey: ["sources"],
        queryFn: fetchSources,
    });
    const pinnedQuery = useQuery({
        queryKey: ["home", "pinned-reports"],
        queryFn: listHomeReports,
    });

    // F14: admin-only unpin action from the pinned tile. Invalidates
    // both the shared home grid and the admin reports list so the
    // pin badge disappears everywhere without a full reload.
    const unpinMutation = useMutation({
        mutationFn: (id: number) => unpinAdminReport(id),
        onSuccess: async () => {
            await queryClient.invalidateQueries({ queryKey: ["home", "pinned-reports"] });
            await queryClient.invalidateQueries({ queryKey: ["admin", "reports"] });
        },
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

            <PinnedReportsCard
                data={pinnedQuery.data}
                isPending={pinnedQuery.isPending}
                isError={pinnedQuery.isError}
                isAdmin={isAdmin}
                onUnpin={(id) => unpinMutation.mutate(id)}
                unpinPendingId={
                    unpinMutation.isPending ? (unpinMutation.variables ?? null) : null
                }
            />

            <BarcodeLookupCard />

            <Card withBorder padding="lg" radius="md">
                <Group justify="space-between" mb="sm">
                    <Title order={4}>{t("home.sourcesCard")}</Title>
                    {isPending && <Loader size="xs" />}
                </Group>

                {error && <ApiErrorAlert error={error} />}

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

function PinnedReportsCard(props: {
    data: HomeReportDto[] | undefined;
    isPending: boolean;
    isError: boolean;
    isAdmin: boolean;
    onUnpin: (id: number) => void;
    unpinPendingId: number | null;
}) {
    const { t } = useTranslation();
    return (
        <Card withBorder padding="lg" radius="md">
            <Group justify="space-between" mb="sm">
                <Group gap="xs">
                    <IconPin size={18} />
                    <Title order={4}>{t("home.pinned.heading")}</Title>
                </Group>
                {props.isPending && <Loader size="xs" />}
            </Group>

            {props.isError && (
                <Alert
                    color="red"
                    icon={<IconAlertTriangle size={18} />}
                    title={t("home.pinned.errorTitle")}
                    role="alert"
                >
                    {t("home.pinned.errorBody")}
                </Alert>
            )}

            {props.data && props.data.length === 0 && (
                <Text c="dimmed">{t("home.pinned.empty")}</Text>
            )}

            {props.data && props.data.length > 0 && (
                <SimpleGrid cols={{ base: 1, sm: 2, md: 3 }} spacing="md">
                    {props.data.map((r) => (
                        <PinnedReportCard
                            key={r.id}
                            report={r}
                            isAdmin={props.isAdmin}
                            onUnpin={props.onUnpin}
                            unpinPending={props.unpinPendingId === r.id}
                        />
                    ))}
                </SimpleGrid>
            )}
        </Card>
    );
}

function PinnedReportCard(props: {
    report: HomeReportDto;
    isAdmin: boolean;
    onUnpin: (id: number) => void;
    unpinPending: boolean;
}) {
    const { t } = useTranslation();
    const r = props.report;
    const rel = relativeFromNow(new Date(r.lastModifiedUtc));
    return (
        <Card
            withBorder
            padding="md"
            radius="sm"
            data-testid={`home-pinned-report-${r.id}`}
        >
            <Stack gap="xs">
                <Group gap="xs" wrap="nowrap" justify="space-between">
                    <Group gap="xs" wrap="nowrap" style={{ minWidth: 0, flex: 1 }}>
                        <Anchor
                            component={Link}
                            to={`/admin/reports/${r.id}`}
                            fw={600}
                            lineClamp={1}
                            underline="hover"
                            style={{ minWidth: 0 }}
                        >
                            {r.title}
                        </Anchor>
                        {r.isLocked && (
                            <Badge
                                size="xs"
                                color="gray"
                                variant="light"
                                leftSection={<IconLock size={10} />}
                            >
                                {t("home.pinned.locked")}
                            </Badge>
                        )}
                    </Group>
                    {props.isAdmin && (
                        <Tooltip label={t("home.pinned.unpinAction")}>
                            <ActionIcon
                                variant="subtle"
                                color="gray"
                                size="sm"
                                aria-label={t("home.pinned.unpinAction")}
                                onClick={() => props.onUnpin(r.id)}
                                loading={props.unpinPending}
                                data-testid={`home-pinned-unpin-${r.id}`}
                            >
                                <IconPinnedOff size={14} />
                            </ActionIcon>
                        </Tooltip>
                    )}
                </Group>
                {r.groupName && (
                    <Text size="xs" c="dimmed">
                        {r.groupName}
                    </Text>
                )}
                {r.description && (
                    <Text size="sm" c="dimmed" lineClamp={2}>
                        {r.description}
                    </Text>
                )}
                <Group gap="xs">
                    <Badge size="xs" variant="light">
                        {t("home.pinned.tileCount", { count: r.entityCount })}
                    </Badge>
                    <Text size="xs" c="dimmed">
                        {t(rel.key, rel.params)}
                    </Text>
                </Group>
            </Stack>
        </Card>
    );
}
