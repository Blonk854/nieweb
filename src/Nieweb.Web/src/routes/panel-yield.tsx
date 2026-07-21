import { Card, Stack, Text, Title } from "@mantine/core";

/**
 * Panel Yield by Line report - placeholder. F4-F7 fill in:
 *   - Filters (source, date range, machines, products) in the URL search
 *   - KPI header cards
 *   - ECharts bar chart per machine
 *   - Mantine data table with sort + column export
 */
export function PanelYieldRoute() {
    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>Panel Yield by Line</Title>
                <Text c="dimmed">
                    First-panel-yield across every AOI line, split by source
                    and date window.
                </Text>
            </Stack>
            <Card withBorder padding="lg" radius="md">
                <Text>
                    The report UI ships in later backlog items (F4-F7). The
                    API is already live at{" "}
                    <Text component="code">GET /api/reports/panel-yield</Text>{" "}
                    with CSV and XLSX exports under{" "}
                    <Text component="code">/export.csv</Text> and{" "}
                    <Text component="code">/export.xlsx</Text>.
                </Text>
            </Card>
        </Stack>
    );
}
