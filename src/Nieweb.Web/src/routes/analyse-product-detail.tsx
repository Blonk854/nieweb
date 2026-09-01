import { Alert, Button, Card, Group, Stack, Text, Title } from "@mantine/core";
import { Link, useParams } from "@tanstack/react-router";
import { IconInfoCircle } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

export function AnalyseProductDetailRoute() {
    const { t } = useTranslation();
    const { productId } = useParams({ from: "/analyse/product/$productId" });

    return (
        <Stack gap="lg">
            <Stack gap={4}>
                <Title order={2}>{t("analyse.productDetailTitle")}</Title>
                <Text c="dimmed">{t("analyse.productDetailSubtitle")}</Text>
            </Stack>

            <Card withBorder padding="md" radius="md">
                <Stack gap="sm">
                    <Group justify="space-between" align="center">
                        <Text fw={600}>{t("analyse.productIdLabel")}: {productId}</Text>
                        <Button component={Link} to="/analyse" variant="light" size="xs">
                            {t("analyse.productBackAction")}
                        </Button>
                    </Group>
                    <Alert icon={<IconInfoCircle size={16} />} color="blue" variant="light">
                        {t("analyse.productDetailComingSoon")}
                    </Alert>
                </Stack>
            </Card>
        </Stack>
    );
}
