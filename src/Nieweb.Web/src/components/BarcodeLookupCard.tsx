import { useState, type FormEvent } from "react";
import {
    Button,
    Card,
    Group,
    Stack,
    Text,
    TextInput,
    Title,
} from "@mantine/core";
import { IconBarcode, IconSearch } from "@tabler/icons-react";
import { useNavigate } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";

/**
 * TC3 entry point — reusable panel-barcode search form. Rendered on
 * the home page (F16) so signed-in users can jump straight to the
 * cross-DB board trace (TC2). Validates the barcode client-side with
 * the same 1..64 character contract as the server so callers never
 * hit a 400.
 *
 * Kept as a standalone component so future tiles (e.g. an admin
 * report dashboard) can drop the same lookup in without duplicating
 * markup + validation.
 */
export function BarcodeLookupCard() {
    const { t } = useTranslation();
    const navigate = useNavigate();
    const [barcode, setBarcode] = useState("");
    const [error, setError] = useState<string | null>(null);

    function handleSubmit(evt: FormEvent<HTMLFormElement>) {
        evt.preventDefault();
        const trimmed = barcode.trim();
        if (trimmed.length === 0) {
            setError(t("traceability.board.barcodeRequired"));
            return;
        }
        if (trimmed.length > 64) {
            setError(t("traceability.board.barcodeTooLong"));
            return;
        }
        setError(null);
        void navigate({
            to: "/traceability/board",
            search: { barcode: trimmed },
        });
    }

    return (
        <Card
            withBorder
            padding="lg"
            radius="md"
            component="form"
            onSubmit={handleSubmit}
            data-testid="home-barcode-lookup"
        >
            <Stack gap="sm">
                <Group gap="xs">
                    <IconBarcode size={18} />
                    <Title order={4}>{t("traceability.board.homeCardTitle")}</Title>
                </Group>
                <Text c="dimmed" size="sm">
                    {t("traceability.board.homeCardHint")}
                </Text>
                <Group align="flex-end" wrap="nowrap">
                    <TextInput
                        label={t("traceability.board.barcodeLabel")}
                        placeholder={t("traceability.board.barcodePlaceholder")}
                        description={t("traceability.board.barcodeHint")}
                        value={barcode}
                        onChange={(e) => setBarcode(e.currentTarget.value)}
                        error={error ?? undefined}
                        maxLength={64}
                        autoComplete="off"
                        spellCheck={false}
                        style={{ flex: 1 }}
                        data-testid="home-barcode-input"
                    />
                    <Button
                        type="submit"
                        leftSection={<IconSearch size={16} />}
                        data-testid="home-barcode-submit"
                    >
                        {t("traceability.board.submit")}
                    </Button>
                </Group>
            </Stack>
        </Card>
    );
}
