import { useMemo, useState } from "react";
import {
    Alert,
    Badge,
    Button,
    Card,
    Code,
    Group,
    NumberInput,
    Pagination,
    Select,
    Stack,
    Table,
    Text,
    TextInput,
    Title,
} from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { IconAlertCircle, IconFilter, IconRefresh } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    listAuditEvents,
    type AuditEventDto,
    type AuditListParams,
    type AuditListResponse,
} from "../api/adminAudit";
import { useSessionStore } from "../state/session";

/**
 * Admin-only audit-trail viewer. Consumes GET /api/admin/audit and
 * paginates through the append-only AuditEvents table. Filters live
 * in local component state (not the URL) — the audit log is an
 * internal-ops utility, not a shareable report, so we don't pay the
 * TanStack Router validateSearch tax here.
 *
 * Route-level gating is handled by the router's beforeLoad
 * (requireAuthentication); the Admin role check lives inside this
 * component so we can render a localised forbidden panel for
 * signed-in-but-not-admin users.
 */

const PAGE_SIZE_OPTIONS = ["25", "50", "100", "250"] as const;
const DEFAULT_PAGE_SIZE = 50;

type FilterFormState = {
    eventType: string;
    targetType: string;
    targetId: string;
    actorUserId: string;
    fromLocal: string;
    toLocal: string;
};

const EMPTY_FILTERS: FilterFormState = {
    eventType: "",
    targetType: "",
    targetId: "",
    actorUserId: "",
    fromLocal: "",
    toLocal: "",
};

const ADMIN_AUDIT_QUERY_KEY = ["admin", "audit"] as const;

export function AdminAuditRoute() {
    const { t, i18n } = useTranslation();
    const roles = useSessionStore((s) => s.user?.roles ?? []);
    const isAdmin = roles.includes("Admin");

    const [form, setForm] = useState<FilterFormState>(EMPTY_FILTERS);
    const [applied, setApplied] = useState<FilterFormState>(EMPTY_FILTERS);
    const [page, setPage] = useState<number>(1);
    const [pageSize, setPageSize] = useState<number>(DEFAULT_PAGE_SIZE);

    const params: AuditListParams = useMemo(() => {
        const p: AuditListParams = { page, pageSize };
        if (applied.eventType.trim()) p.eventType = applied.eventType.trim();
        if (applied.targetType.trim()) p.targetType = applied.targetType.trim();
        if (applied.targetId.trim()) p.targetId = applied.targetId.trim();
        if (applied.actorUserId.trim()) {
            const parsed = Number.parseInt(applied.actorUserId.trim(), 10);
            if (Number.isFinite(parsed)) p.actorUserId = parsed;
        }
        const fromIso = toUtcIso(applied.fromLocal);
        if (fromIso) p.fromUtc = fromIso;
        const toIso = toUtcIso(applied.toLocal);
        if (toIso) p.toUtc = toIso;
        return p;
    }, [applied, page, pageSize]);

    const query = useQuery<AuditListResponse>({
        queryKey: [...ADMIN_AUDIT_QUERY_KEY, params],
        queryFn: () => listAuditEvents(params),
        enabled: isAdmin,
        refetchOnWindowFocus: false,
        placeholderData: (previous) => previous,
    });

    const dateFormatter = useMemo(
        () =>
            new Intl.DateTimeFormat(i18n.language, {
                dateStyle: "short",
                timeStyle: "medium",
                timeZone: "UTC",
            }),
        [i18n.language],
    );

    if (!isAdmin) {
        return (
            <Stack gap="md">
                <Title order={2}>{t("admin.audit.title")}</Title>
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("admin.audit.forbidden")}
                </Alert>
            </Stack>
        );
    }

    const data = query.data;
    const rows: AuditEventDto[] = data?.items ?? [];
    const total = data?.total ?? 0;
    const pages = total > 0 ? Math.max(1, Math.ceil(total / pageSize)) : 1;
    const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
    const to = Math.min(total, page * pageSize);

    return (
        <Stack gap="lg">
            <Group justify="space-between" align="flex-end" wrap="wrap">
                <Stack gap={4}>
                    <Title order={2}>{t("admin.audit.title")}</Title>
                    <Text c="dimmed" size="sm">
                        {t("admin.audit.subtitle")}
                    </Text>
                </Stack>
                <Button
                    variant="default"
                    leftSection={<IconRefresh size={16} />}
                    onClick={() => query.refetch()}
                    loading={query.isFetching && !query.isLoading}
                >
                    {t("admin.audit.reload")}
                </Button>
            </Group>

            <Card withBorder radius="md" padding="lg">
                <Stack gap="sm">
                    <Text fw={600}>{t("admin.audit.filters.heading")}</Text>
                    <Group grow wrap="wrap" align="flex-end">
                        <TextInput
                            label={t("admin.audit.filters.eventType")}
                            placeholder={t(
                                "admin.audit.filters.eventTypePlaceholder",
                            )}
                            value={form.eventType}
                            onChange={(e) => {
                                const v = e.currentTarget.value;
                                setForm((f) => ({ ...f, eventType: v }));
                            }}
                        />
                        <TextInput
                            label={t("admin.audit.filters.targetType")}
                            placeholder={t(
                                "admin.audit.filters.targetTypePlaceholder",
                            )}
                            value={form.targetType}
                            onChange={(e) => {
                                const v = e.currentTarget.value;
                                setForm((f) => ({ ...f, targetType: v }));
                            }}
                        />
                        <TextInput
                            label={t("admin.audit.filters.targetId")}
                            placeholder={t(
                                "admin.audit.filters.targetIdPlaceholder",
                            )}
                            value={form.targetId}
                            onChange={(e) => {
                                const v = e.currentTarget.value;
                                setForm((f) => ({ ...f, targetId: v }));
                            }}
                        />
                        <NumberInput
                            label={t("admin.audit.filters.actorUserId")}
                            placeholder={t(
                                "admin.audit.filters.actorUserIdPlaceholder",
                            )}
                            value={
                                form.actorUserId === ""
                                    ? ""
                                    : Number(form.actorUserId)
                            }
                            onChange={(v) =>
                                setForm((f) => ({
                                    ...f,
                                    actorUserId: v === "" ? "" : String(v),
                                }))
                            }
                            min={1}
                            allowDecimal={false}
                            allowNegative={false}
                            hideControls
                        />
                    </Group>
                    <Group grow wrap="wrap" align="flex-end">
                        <TextInput
                            type="datetime-local"
                            label={t("admin.audit.filters.fromUtc")}
                            value={form.fromLocal}
                            onChange={(e) => {
                                const v = e.currentTarget.value;
                                setForm((f) => ({ ...f, fromLocal: v }));
                            }}
                        />
                        <TextInput
                            type="datetime-local"
                            label={t("admin.audit.filters.toUtc")}
                            value={form.toLocal}
                            onChange={(e) => {
                                const v = e.currentTarget.value;
                                setForm((f) => ({ ...f, toLocal: v }));
                            }}
                        />
                        <Select
                            label={t("admin.audit.filters.pageSize")}
                            data={PAGE_SIZE_OPTIONS as unknown as string[]}
                            value={String(pageSize)}
                            onChange={(v) => {
                                if (v) {
                                    const parsed = Number.parseInt(v, 10);
                                    if (Number.isFinite(parsed)) {
                                        setPageSize(parsed);
                                        setPage(1);
                                    }
                                }
                            }}
                            allowDeselect={false}
                        />
                    </Group>
                    <Group justify="flex-end" gap="xs">
                        <Button
                            variant="default"
                            onClick={() => {
                                setForm(EMPTY_FILTERS);
                                setApplied(EMPTY_FILTERS);
                                setPage(1);
                            }}
                        >
                            {t("admin.audit.filters.reset")}
                        </Button>
                        <Button
                            leftSection={<IconFilter size={16} />}
                            onClick={() => {
                                setApplied(form);
                                setPage(1);
                            }}
                        >
                            {t("admin.audit.filters.apply")}
                        </Button>
                    </Group>
                </Stack>
            </Card>

            {query.isError && (
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("admin.audit.loadError")}
                </Alert>
            )}

            <Card withBorder radius="md" padding="lg">
                {query.isLoading ? (
                    <Text c="dimmed">{t("common.loading")}</Text>
                ) : rows.length === 0 ? (
                    <Text c="dimmed">{t("admin.audit.emptyState")}</Text>
                ) : (
                    <Stack gap="md">
                        <Table striped highlightOnHover withColumnBorders>
                            <Table.Thead>
                                <Table.Tr>
                                    <Table.Th>
                                        {t("admin.audit.columns.when")}
                                    </Table.Th>
                                    <Table.Th>
                                        {t("admin.audit.columns.actor")}
                                    </Table.Th>
                                    <Table.Th>
                                        {t("admin.audit.columns.eventType")}
                                    </Table.Th>
                                    <Table.Th>
                                        {t("admin.audit.columns.target")}
                                    </Table.Th>
                                    <Table.Th>
                                        {t("admin.audit.columns.ip")}
                                    </Table.Th>
                                    <Table.Th>
                                        {t("admin.audit.columns.details")}
                                    </Table.Th>
                                </Table.Tr>
                            </Table.Thead>
                            <Table.Tbody>
                                {rows.map((row) => (
                                    <AuditRow
                                        key={row.id}
                                        row={row}
                                        dateFormatter={dateFormatter}
                                        anonymousLabel={t(
                                            "admin.audit.anonymous",
                                        )}
                                        noIpLabel={t("admin.audit.noIp")}
                                    />
                                ))}
                            </Table.Tbody>
                        </Table>
                        <Group justify="space-between" wrap="wrap">
                            <Text size="sm" c="dimmed">
                                {t("admin.audit.pagination.summary", {
                                    from,
                                    to,
                                    total,
                                })}
                                {" · "}
                                {t("admin.audit.pagination.pageOf", {
                                    page,
                                    pages,
                                })}
                            </Text>
                            <Pagination
                                total={pages}
                                value={page}
                                onChange={setPage}
                                withEdges
                                siblings={1}
                            />
                        </Group>
                    </Stack>
                )}
            </Card>
        </Stack>
    );
}

function AuditRow(props: {
    row: AuditEventDto;
    dateFormatter: Intl.DateTimeFormat;
    anonymousLabel: string;
    noIpLabel: string;
}) {
    const { row, dateFormatter, anonymousLabel, noIpLabel } = props;
    return (
        <Table.Tr>
            <Table.Td style={{ whiteSpace: "nowrap" }}>
                {formatWhen(row.eventTimeUtc, dateFormatter)}
            </Table.Td>
            <Table.Td>
                <Stack gap={0}>
                    <Text size="sm">
                        {row.actorUserId === null
                            ? anonymousLabel
                            : row.actorDisplayName}
                    </Text>
                    {row.actorUserId !== null && (
                        <Text size="xs" c="dimmed">
                            #{row.actorUserId}
                        </Text>
                    )}
                </Stack>
            </Table.Td>
            <Table.Td>
                <Badge variant="light" size="sm">
                    {row.eventType}
                </Badge>
            </Table.Td>
            <Table.Td>
                <Stack gap={0}>
                    <Text size="sm">{row.targetType}</Text>
                    <Text size="xs" c="dimmed">
                        {row.targetId}
                    </Text>
                </Stack>
            </Table.Td>
            <Table.Td style={{ whiteSpace: "nowrap" }}>
                {row.ipAddress ?? noIpLabel}
            </Table.Td>
            <Table.Td style={{ maxWidth: 320 }}>
                <Code
                    block
                    style={{
                        fontSize: 11,
                        whiteSpace: "pre-wrap",
                        wordBreak: "break-word",
                        maxHeight: 120,
                        overflow: "auto",
                    }}
                >
                    {prettifyJson(row.detailsJson)}
                </Code>
            </Table.Td>
        </Table.Tr>
    );
}

function formatWhen(iso: string, formatter: Intl.DateTimeFormat): string {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return formatter.format(d);
}

function prettifyJson(raw: string): string {
    if (!raw) return "";
    try {
        return JSON.stringify(JSON.parse(raw), null, 2);
    } catch {
        return raw;
    }
}

/**
 * Convert an HTML `datetime-local` value ("YYYY-MM-DDTHH:mm") into
 * an ISO-8601 UTC string suitable for the API. The `datetime-local`
 * control is timezone-naive; we treat the entered value as UTC so
 * "from/to (UTC)" labels match what the user typed. Returns null for
 * empty / invalid input.
 */
function toUtcIso(local: string): string | null {
    if (!local) return null;
    // "YYYY-MM-DDTHH:mm" or "YYYY-MM-DDTHH:mm:ss"; append 'Z' to
    // force UTC parsing regardless of the browser's local zone.
    const withZ = local.length === 16 ? `${local}:00Z` : `${local}Z`;
    const d = new Date(withZ);
    if (Number.isNaN(d.getTime())) return null;
    return d.toISOString();
}
