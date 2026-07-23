import { useMemo, useState } from "react";
import {
    Alert,
    Button,
    Card,
    Group,
    Radio,
    Select,
    Stack,
    Text,
    Title,
} from "@mantine/core";
import { IconCircleCheck, IconClock, IconInfoCircle } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import { useDateTimeFormatter } from "../i18n/formatters";
import {
    AUTO_TIME_ZONE,
    resolveTimeZone,
    usePreferencesStore,
} from "../state/preferences";

/**
 * Curated shortlist of IANA time zones. `Intl.supportedValuesOf`
 * would give the full 400+ list but the Mantine searchable Select
 * handles that comfortably — we still start from this list because it
 * keeps the dropdown scan-friendly for the SMT floor use cases we
 * care about (US East/West, EU, EMEA, Asia). Anything not in the
 * list is still reachable by typing the IANA name, and the runtime
 * happily accepts it.
 */
const CURATED_ZONES: readonly string[] = [
    "UTC",
    "America/Los_Angeles",
    "America/Denver",
    "America/Chicago",
    "America/New_York",
    "America/Mexico_City",
    "America/Sao_Paulo",
    "Europe/London",
    "Europe/Paris",
    "Europe/Berlin",
    "Europe/Madrid",
    "Europe/Warsaw",
    "Europe/Bucharest",
    "Africa/Cairo",
    "Asia/Jerusalem",
    "Asia/Dubai",
    "Asia/Kolkata",
    "Asia/Bangkok",
    "Asia/Shanghai",
    "Asia/Tokyo",
    "Asia/Seoul",
    "Australia/Sydney",
    "Pacific/Auckland",
];

/**
 * Return the widest available IANA zone list. Modern browsers expose
 * `Intl.supportedValuesOf("timeZone")` (Chromium 99+, Safari 15.4+,
 * Firefox 93+); older environments (jsdom in CI, ancient Edge) fall
 * back to `CURATED_ZONES` so the UI still works.
 */
function listAvailableZones(): readonly string[] {
    const IntlAny = Intl as unknown as {
        supportedValuesOf?: (key: string) => string[];
    };
    if (typeof IntlAny.supportedValuesOf === "function") {
        try {
            const all = IntlAny.supportedValuesOf("timeZone");
            if (Array.isArray(all) && all.length > 0) {
                return all;
            }
        } catch {
            // fall through
        }
    }
    return CURATED_ZONES;
}

export function SettingsTimezoneRoute() {
    const { t } = useTranslation();
    const timeZone = usePreferencesStore((s) => s.timeZone);
    const setTimeZone = usePreferencesStore((s) => s.setTimeZone);

    const mode: "auto" | "manual" =
        timeZone === AUTO_TIME_ZONE ? "auto" : "manual";
    const resolved = resolveTimeZone(timeZone);

    // The Select only cares when we're in "manual" mode. When the
    // user is on "auto" we still hold the resolved value so the
    // preview stays populated.
    const [manualValue, setManualValue] = useState<string>(
        timeZone === AUTO_TIME_ZONE ? resolved : timeZone,
    );
    const [saved, setSaved] = useState(false);

    const zones = useMemo(() => listAvailableZones(), []);
    const zoneOptions = useMemo(
        () => zones.map((z) => ({ value: z, label: z })),
        [zones],
    );

    const preview = useDateTimeFormatter({
        dateStyle: "full",
        timeStyle: "long",
    });
    const nowSample = useMemo(() => new Date(), [timeZone]);

    function handleModeChange(next: string) {
        if (next === "auto") {
            setTimeZone(AUTO_TIME_ZONE);
            setSaved(true);
            return;
        }
        setTimeZone(manualValue);
        setSaved(true);
    }

    function handleZoneChange(next: string | null) {
        if (!next) {
            return;
        }
        setManualValue(next);
        setTimeZone(next);
        setSaved(true);
    }

    function handleReset() {
        setTimeZone(AUTO_TIME_ZONE);
        setSaved(true);
    }

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("settings.timezone.title")}</Title>
                <Text c="dimmed">{t("settings.timezone.subtitle")}</Text>
            </Stack>

            <Card withBorder padding="lg" radius="md">
                <Stack gap="md">
                    <Radio.Group
                        value={mode}
                        onChange={handleModeChange}
                        label={t("settings.timezone.currentLabel")}
                    >
                        <Stack gap="xs" mt="xs">
                            <Radio
                                value="auto"
                                label={t("settings.timezone.autoLabel")}
                                description={t(
                                    "settings.timezone.autoDescription",
                                )}
                                data-testid="tz-mode-auto"
                            />
                            <Radio
                                value="manual"
                                label={t("settings.timezone.selectLabel")}
                                data-testid="tz-mode-manual"
                            />
                        </Stack>
                    </Radio.Group>

                    <Select
                        label={t("settings.timezone.selectLabel")}
                        placeholder={t("settings.timezone.selectPlaceholder")}
                        data={zoneOptions}
                        value={mode === "manual" ? manualValue : null}
                        onChange={handleZoneChange}
                        searchable
                        disabled={mode !== "manual"}
                        nothingFoundMessage={t(
                            "settings.timezone.selectNothingFound",
                        )}
                        leftSection={<IconClock size={16} />}
                        data-testid="tz-select"
                    />

                    <Group gap="sm">
                        <Button
                            variant="subtle"
                            onClick={handleReset}
                            disabled={mode === "auto"}
                            data-testid="tz-reset"
                        >
                            {t("settings.timezone.resetToAuto")}
                        </Button>
                    </Group>
                </Stack>
            </Card>

            <Card withBorder padding="lg" radius="md">
                <Stack gap="xs">
                    <Text fw={600}>{t("settings.timezone.previewLabel")}</Text>
                    <Text data-testid="tz-preview">
                        {preview.format(nowSample)}
                    </Text>
                    <Text c="dimmed" size="sm" data-testid="tz-resolved">
                        {resolved}
                    </Text>
                </Stack>
            </Card>

            {saved && (
                <Alert
                    icon={<IconCircleCheck size={18} />}
                    color="green"
                    role="status"
                    data-testid="tz-saved"
                >
                    {t("settings.timezone.savedNotice")}
                </Alert>
            )}

            <Alert
                icon={<IconInfoCircle size={18} />}
                color="blue"
                variant="light"
            >
                {t("settings.timezone.note")}
            </Alert>
        </Stack>
    );
}
