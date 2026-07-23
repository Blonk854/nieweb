import { useEffect, useState } from "react";
import {
    Alert,
    Button,
    Card,
    Group,
    NumberInput,
    Stack,
    Table,
    Text,
    TextInput,
    Title,
} from "@mantine/core";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
    IconAlertCircle,
    IconCheck,
    IconPlus,
    IconRefresh,
    IconTrash,
} from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import {
    listShifts,
    replaceShifts,
    type ShiftBreakpointDto,
} from "../api/adminShifts";
import { ApiError } from "../api/client";
import { useSessionStore } from "../state/session";

/**
 * Admin-only route for the site-wide shift cycle (F13 of docs/phase-2.md
 * §7.9, backed by PL1). The whole cycle is one atomic unit — the admin
 * edits the local table (add / edit / remove rows) and hits "Save" to
 * PUT the full replacement list to `/api/admin/shifts`.
 */

const SHIFTS_QUERY_KEY = ["admin", "shifts"] as const;

type ShiftDraft = {
    hour: number;
    minute: number;
    label: string;
};

function toDrafts(rows: ShiftBreakpointDto[]): ShiftDraft[] {
    return rows.map((r) => ({
        hour: r.hour,
        minute: r.minute,
        label: r.label ?? "",
    }));
}

function extractValidationDetail(body: string): string | undefined {
    try {
        const parsed = JSON.parse(body) as {
            errors?: Record<string, string[]>;
        };
        if (!parsed.errors) return undefined;
        return Object.values(parsed.errors).flat().join("; ");
    } catch {
        return body.length > 0 ? body : undefined;
    }
}

export function AdminShiftsRoute() {
    const { t } = useTranslation();
    const roles = useSessionStore((s) => s.user?.roles ?? []);
    const isAdmin = roles.includes("Admin");
    const queryClient = useQueryClient();

    const [drafts, setDrafts] = useState<ShiftDraft[]>([]);
    const [error, setError] = useState<{ key: string; detail?: string } | null>(
        null,
    );
    const [saved, setSaved] = useState(false);

    const query = useQuery({
        queryKey: SHIFTS_QUERY_KEY,
        queryFn: listShifts,
        enabled: isAdmin,
        refetchOnWindowFocus: false,
    });

    // Hydrate the editable drafts from the server payload once it arrives.
    useEffect(() => {
        if (query.data) {
            setDrafts(toDrafts(query.data));
        }
    }, [query.data]);

    const mutation = useMutation({
        mutationFn: async (next: ShiftDraft[]) => {
            return replaceShifts({
                entries: next.map((d) => ({
                    hour: d.hour,
                    minute: d.minute,
                    label: d.label.trim().length === 0 ? null : d.label.trim(),
                })),
            });
        },
        onSuccess: (rows) => {
            setError(null);
            setSaved(true);
            setDrafts(toDrafts(rows));
            void queryClient.invalidateQueries({ queryKey: SHIFTS_QUERY_KEY });
        },
        onError: (err) => {
            setSaved(false);
            if (err instanceof ApiError && err.status === 400) {
                setError({
                    key: "admin.shifts.save.validationFailed",
                    detail: extractValidationDetail(err.body),
                });
            } else {
                setError({ key: "admin.shifts.save.unexpectedError" });
            }
        },
    });

    if (!isAdmin) {
        return (
            <Stack gap="md">
                <Title order={2}>{t("admin.shifts.title")}</Title>
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("admin.shifts.forbidden")}
                </Alert>
            </Stack>
        );
    }

    const updateDraft = (
        index: number,
        patch: Partial<ShiftDraft>,
    ) => {
        setSaved(false);
        setDrafts((prev) =>
            prev.map((d, i) => (i === index ? { ...d, ...patch } : d)),
        );
    };

    const addRow = () => {
        setSaved(false);
        setDrafts((prev) => [
            ...prev,
            { hour: 0, minute: 0, label: "" },
        ]);
    };

    const removeRow = (index: number) => {
        setSaved(false);
        setDrafts((prev) => prev.filter((_, i) => i !== index));
    };

    const handleSave = () => {
        setError(null);
        mutation.mutate(drafts);
    };

    return (
        <Stack gap="lg">
            <Group justify="space-between" align="flex-end" wrap="wrap">
                <Stack gap={4}>
                    <Title order={2}>{t("admin.shifts.title")}</Title>
                    <Text c="dimmed" size="sm">
                        {t("admin.shifts.subtitle")}
                    </Text>
                </Stack>
                <Group gap="xs">
                    <Button
                        variant="default"
                        leftSection={<IconRefresh size={16} />}
                        onClick={() => query.refetch()}
                        loading={query.isFetching && !query.isLoading}
                    >
                        {t("admin.shifts.reload")}
                    </Button>
                    <Button
                        leftSection={<IconPlus size={16} />}
                        onClick={addRow}
                        data-testid="admin-shifts-add"
                    >
                        {t("admin.shifts.addRow")}
                    </Button>
                    <Button
                        leftSection={<IconCheck size={16} />}
                        onClick={handleSave}
                        loading={mutation.isPending}
                        data-testid="admin-shifts-save"
                    >
                        {t("admin.shifts.save.submit")}
                    </Button>
                </Group>
            </Group>

            {query.isError && (
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                >
                    {t("admin.shifts.loadError")}
                </Alert>
            )}
            {error && (
                <Alert
                    color="red"
                    icon={<IconAlertCircle size={18} />}
                    role="alert"
                    withCloseButton
                    onClose={() => setError(null)}
                >
                    <Text>{t(error.key as never)}</Text>
                    {error.detail && (
                        <Text size="xs" c="dimmed" mt={4}>
                            {error.detail}
                        </Text>
                    )}
                </Alert>
            )}
            {saved && (
                <Alert
                    color="green"
                    icon={<IconCheck size={18} />}
                    role="status"
                    withCloseButton
                    onClose={() => setSaved(false)}
                >
                    {t("admin.shifts.save.success")}
                </Alert>
            )}

            <Card withBorder radius="md" padding="lg">
                {query.isLoading ? (
                    <Text c="dimmed">{t("common.loading")}</Text>
                ) : drafts.length === 0 ? (
                    <Text c="dimmed">{t("admin.shifts.emptyState")}</Text>
                ) : (
                    <Table striped withColumnBorders>
                        <Table.Thead>
                            <Table.Tr>
                                <Table.Th style={{ width: 120 }}>
                                    {t("admin.shifts.columns.hour")}
                                </Table.Th>
                                <Table.Th style={{ width: 120 }}>
                                    {t("admin.shifts.columns.minute")}
                                </Table.Th>
                                <Table.Th>{t("admin.shifts.columns.label")}</Table.Th>
                                <Table.Th style={{ width: 80 }}>
                                    {t("admin.shifts.columns.actions")}
                                </Table.Th>
                            </Table.Tr>
                        </Table.Thead>
                        <Table.Tbody>
                            {drafts.map((d, index) => (
                                <Table.Tr
                                    key={index}
                                    data-testid={`admin-shifts-row-${index}`}
                                >
                                    <Table.Td>
                                        <NumberInput
                                            min={0}
                                            max={23}
                                            value={d.hour}
                                            onChange={(v) =>
                                                updateDraft(index, {
                                                    hour: typeof v === "number" ? v : 0,
                                                })
                                            }
                                            data-testid={`admin-shifts-hour-${index}`}
                                        />
                                    </Table.Td>
                                    <Table.Td>
                                        <NumberInput
                                            min={0}
                                            max={59}
                                            value={d.minute}
                                            onChange={(v) =>
                                                updateDraft(index, {
                                                    minute: typeof v === "number" ? v : 0,
                                                })
                                            }
                                            data-testid={`admin-shifts-minute-${index}`}
                                        />
                                    </Table.Td>
                                    <Table.Td>
                                        <TextInput
                                            value={d.label}
                                            onChange={(e) =>
                                                updateDraft(index, {
                                                    label: e.currentTarget.value,
                                                })
                                            }
                                            placeholder={t("admin.shifts.labelPlaceholder")}
                                            data-testid={`admin-shifts-label-${index}`}
                                        />
                                    </Table.Td>
                                    <Table.Td>
                                        <Button
                                            size="xs"
                                            variant="default"
                                            color="red"
                                            leftSection={<IconTrash size={14} />}
                                            onClick={() => removeRow(index)}
                                            data-testid={`admin-shifts-remove-${index}`}
                                            aria-label={t("admin.shifts.actions.remove")}
                                        >
                                            {t("admin.shifts.actions.remove")}
                                        </Button>
                                    </Table.Td>
                                </Table.Tr>
                            ))}
                        </Table.Tbody>
                    </Table>
                )}
            </Card>
        </Stack>
    );
}
