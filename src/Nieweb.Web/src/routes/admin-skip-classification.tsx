import { useEffect, useState } from "react";
import {
    Alert,
    Button,
    Card,
    Group,
    NumberInput,
    Select,
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
    getSkipClassificationConfig,
    saveSkipClassificationConfig,
    REPAIR_BUTTON_MEANINGS,
    type RepairButtonMeaning,
    type SkipClassificationConfigDto,
} from "../api/skipClassification";
import { ApiError } from "../api/client";
import { useSessionStore } from "../state/session";

/**
 * Admin-only route for the skip-classification config: the empty-board
 * heuristic thresholds and the repair-button label -> meaning map. The
 * config is one atomic unit (mirrors the shift-cycle screen) — the admin
 * edits the local form and hits Save to PUT the whole thing to
 * `/api/admin/skip-classification`. These values feed the DPMO / FPY /
 * Skip Summary reports' skip toggle and status filter.
 */

const SKIP_CONFIG_QUERY_KEY = ["admin", "skipClassification"] as const;

type MeaningDraft = { label: string; meaning: RepairButtonMeaning };

type Draft = {
    missingRatioThreshold: number | string;
    minComponentFloor: number | string;
    absoluteMissingFloor: number | string;
    meanings: MeaningDraft[];
};

function toDraft(config: SkipClassificationConfigDto): Draft {
    return {
        missingRatioThreshold: config.missingRatioThreshold,
        minComponentFloor: config.minComponentFloor,
        absoluteMissingFloor: config.absoluteMissingFloor,
        meanings: config.repairButtonMeanings.map((m) => ({
            label: m.label,
            meaning: m.meaning,
        })),
    };
}

function extractValidationDetail(body: string): string | undefined {
    try {
        const parsed = JSON.parse(body) as { errors?: Record<string, string[]> };
        if (!parsed.errors) return undefined;
        return Object.values(parsed.errors).flat().join("; ");
    } catch {
        return body.length > 0 ? body : undefined;
    }
}

export function AdminSkipClassificationRoute() {
    const { t } = useTranslation();
    const roles = useSessionStore((s) => s.user?.roles ?? []);
    const isAdmin = roles.includes("Admin");
    const queryClient = useQueryClient();

    const [draft, setDraft] = useState<Draft | null>(null);
    const [error, setError] = useState<{ key: string; detail?: string } | null>(null);
    const [saved, setSaved] = useState(false);

    const query = useQuery({
        queryKey: SKIP_CONFIG_QUERY_KEY,
        queryFn: getSkipClassificationConfig,
        enabled: isAdmin,
        refetchOnWindowFocus: false,
    });

    useEffect(() => {
        if (query.data) {
            setDraft(toDraft(query.data));
        }
    }, [query.data]);

    const mutation = useMutation({
        mutationFn: async (next: Draft) => {
            const body: SkipClassificationConfigDto = {
                missingRatioThreshold: Number(next.missingRatioThreshold),
                minComponentFloor: Number(next.minComponentFloor),
                absoluteMissingFloor: Number(next.absoluteMissingFloor),
                repairButtonMeanings: next.meanings
                    .filter((m) => m.label.trim().length > 0)
                    .map((m) => ({ label: m.label.trim(), meaning: m.meaning })),
            };
            return saveSkipClassificationConfig(body);
        },
        onSuccess: (config) => {
            setError(null);
            setSaved(true);
            setDraft(toDraft(config));
            void queryClient.invalidateQueries({ queryKey: SKIP_CONFIG_QUERY_KEY });
        },
        onError: (err) => {
            setSaved(false);
            if (err instanceof ApiError && err.status === 400) {
                setError({
                    key: "admin.skipClassification.save.validationFailed",
                    detail: extractValidationDetail(err.body),
                });
            } else {
                setError({ key: "admin.skipClassification.save.unexpectedError" });
            }
        },
    });

    if (!isAdmin) {
        return (
            <Stack gap="md">
                <Title order={2}>{t("admin.skipClassification.title")}</Title>
                <Alert color="red" icon={<IconAlertCircle size={18} />} role="alert">
                    {t("admin.skipClassification.forbidden")}
                </Alert>
            </Stack>
        );
    }

    const patch = (p: Partial<Draft>) => {
        setSaved(false);
        setDraft((prev) => (prev ? { ...prev, ...p } : prev));
    };

    const updateMeaning = (index: number, p: Partial<MeaningDraft>) => {
        setSaved(false);
        setDraft((prev) =>
            prev
                ? {
                      ...prev,
                      meanings: prev.meanings.map((m, i) => (i === index ? { ...m, ...p } : m)),
                  }
                : prev,
        );
    };

    const addMeaning = () => {
        setSaved(false);
        setDraft((prev) =>
            prev ? { ...prev, meanings: [...prev.meanings, { label: "", meaning: "ManualSkip" }] } : prev,
        );
    };

    const removeMeaning = (index: number) => {
        setSaved(false);
        setDraft((prev) =>
            prev ? { ...prev, meanings: prev.meanings.filter((_, i) => i !== index) } : prev,
        );
    };

    const handleSave = () => {
        if (!draft) return;
        setError(null);
        mutation.mutate(draft);
    };

    return (
        <Stack gap="lg">
            <Group justify="space-between" align="flex-end" wrap="wrap">
                <Stack gap={4}>
                    <Title order={2}>{t("admin.skipClassification.title")}</Title>
                    <Text c="dimmed" size="sm">
                        {t("admin.skipClassification.subtitle")}
                    </Text>
                </Stack>
                <Group gap="xs">
                    <Button
                        variant="default"
                        leftSection={<IconRefresh size={16} />}
                        onClick={() => query.refetch()}
                        loading={query.isFetching && !query.isLoading}
                    >
                        {t("admin.skipClassification.reload")}
                    </Button>
                    <Button
                        leftSection={<IconCheck size={16} />}
                        onClick={handleSave}
                        loading={mutation.isPending}
                        disabled={!draft}
                        data-testid="admin-skip-save"
                    >
                        {t("admin.skipClassification.save.submit")}
                    </Button>
                </Group>
            </Group>

            {error && (
                <Alert color="red" icon={<IconAlertCircle size={18} />} role="alert">
                    {t(error.key as never)}
                    {error.detail ? `: ${error.detail}` : ""}
                </Alert>
            )}

            {saved && !error && (
                <Alert color="green" icon={<IconCheck size={18} />}>
                    {t("admin.skipClassification.save.success")}
                </Alert>
            )}

            {query.isError && !query.data && (
                <Alert color="red" icon={<IconAlertCircle size={18} />} role="alert">
                    {t("admin.skipClassification.loadError")}
                </Alert>
            )}

            {draft && (
                <>
                    <Card withBorder padding="lg" radius="md">
                        <Title order={4} mb="sm">
                            {t("admin.skipClassification.thresholds.heading")}
                        </Title>
                        <Text c="dimmed" size="sm" mb="md">
                            {t("admin.skipClassification.thresholds.hint")}
                        </Text>
                        <Group grow align="flex-start">
                            <NumberInput
                                label={t("admin.skipClassification.thresholds.missingRatio")}
                                description={t("admin.skipClassification.thresholds.missingRatioHint")}
                                min={0}
                                max={1}
                                step={0.05}
                                decimalScale={2}
                                value={draft.missingRatioThreshold}
                                onChange={(v) => patch({ missingRatioThreshold: v })}
                                data-testid="admin-skip-ratio"
                            />
                            <NumberInput
                                label={t("admin.skipClassification.thresholds.minComponentFloor")}
                                description={t("admin.skipClassification.thresholds.minComponentFloorHint")}
                                min={1}
                                step={1}
                                allowDecimal={false}
                                value={draft.minComponentFloor}
                                onChange={(v) => patch({ minComponentFloor: v })}
                            />
                            <NumberInput
                                label={t("admin.skipClassification.thresholds.absoluteMissingFloor")}
                                description={t("admin.skipClassification.thresholds.absoluteMissingFloorHint")}
                                min={1}
                                step={1}
                                allowDecimal={false}
                                value={draft.absoluteMissingFloor}
                                onChange={(v) => patch({ absoluteMissingFloor: v })}
                            />
                        </Group>
                    </Card>

                    <Card withBorder padding="lg" radius="md">
                        <Group justify="space-between" mb="sm">
                            <Title order={4}>
                                {t("admin.skipClassification.buttons.heading")}
                            </Title>
                            <Button
                                variant="light"
                                size="xs"
                                leftSection={<IconPlus size={14} />}
                                onClick={addMeaning}
                                data-testid="admin-skip-add-button"
                            >
                                {t("admin.skipClassification.buttons.add")}
                            </Button>
                        </Group>
                        <Text c="dimmed" size="sm" mb="md">
                            {t("admin.skipClassification.buttons.hint")}
                        </Text>

                        {draft.meanings.length === 0 ? (
                            <Text c="dimmed" size="sm">
                                {t("admin.skipClassification.buttons.empty")}
                            </Text>
                        ) : (
                            <Table>
                                <Table.Thead>
                                    <Table.Tr>
                                        <Table.Th>{t("admin.skipClassification.buttons.label")}</Table.Th>
                                        <Table.Th>{t("admin.skipClassification.buttons.meaning")}</Table.Th>
                                        <Table.Th />
                                    </Table.Tr>
                                </Table.Thead>
                                <Table.Tbody>
                                    {draft.meanings.map((m, i) => (
                                        <Table.Tr key={i}>
                                            <Table.Td>
                                                <TextInput
                                                    aria-label={t("admin.skipClassification.buttons.label")}
                                                    placeholder="X-OUT"
                                                    value={m.label}
                                                    onChange={(e) =>
                                                        updateMeaning(i, { label: e.currentTarget.value })
                                                    }
                                                />
                                            </Table.Td>
                                            <Table.Td>
                                                <Select
                                                    aria-label={t("admin.skipClassification.buttons.meaning")}
                                                    data={REPAIR_BUTTON_MEANINGS.map((v) => ({
                                                        value: v,
                                                        label: t(`admin.skipClassification.meaning.${v}`),
                                                    }))}
                                                    value={m.meaning}
                                                    onChange={(v) =>
                                                        updateMeaning(i, {
                                                            meaning: (v ?? "Normal") as RepairButtonMeaning,
                                                        })
                                                    }
                                                    allowDeselect={false}
                                                    comboboxProps={{ withinPortal: true }}
                                                />
                                            </Table.Td>
                                            <Table.Td>
                                                <Button
                                                    variant="subtle"
                                                    color="red"
                                                    size="xs"
                                                    leftSection={<IconTrash size={14} />}
                                                    onClick={() => removeMeaning(i)}
                                                >
                                                    {t("admin.skipClassification.buttons.remove")}
                                                </Button>
                                            </Table.Td>
                                        </Table.Tr>
                                    ))}
                                </Table.Tbody>
                            </Table>
                        )}
                    </Card>
                </>
            )}
        </Stack>
    );
}
