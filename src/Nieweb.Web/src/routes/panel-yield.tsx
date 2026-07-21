import { Card, Stack, Text, Title } from "@mantine/core";
import { Trans, useTranslation } from "react-i18next";

/**
 * Panel Yield by Line report - placeholder. F4-F7 fill in:
 *   - Filters (source, date range, machines, products) in the URL search
 *   - KPI header cards
 *   - ECharts bar chart per machine
 *   - Mantine data table with sort + column export
 */
export function PanelYieldRoute() {
    const { t } = useTranslation();
    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("panelYield.title")}</Title>
                <Text c="dimmed">{t("panelYield.subtitle")}</Text>
            </Stack>
            <Card withBorder padding="lg" radius="md">
                <Text>
                    <Trans
                        i18nKey="panelYield.placeholderBody"
                        components={{
                            1: <Text component="code" />,
                            3: <Text component="code" />,
                            5: <Text component="code" />,
                        }}
                    />
                </Text>
            </Card>
        </Stack>
    );
}
