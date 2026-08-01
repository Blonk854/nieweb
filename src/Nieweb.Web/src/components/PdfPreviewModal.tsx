import { useEffect, useRef, useState } from "react";
import {
    Alert,
    Box,
    Button,
    Center,
    Group,
    Loader,
    Modal,
    Stack,
    Text,
} from "@mantine/core";
import { IconAlertCircle, IconDownload, IconX } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";
import { ApiError } from "../api/client";
import { describeApiError } from "../api/problem";
import { useSessionStore } from "../state/session";

/**
 * F15 - PDF preview modal.
 *
 * Fetches the PDF at `pdfUrl` with the current session's bearer token
 * (so the browser's plain-anchor limitation - see
 * <c>reportExport.ts</c> - does not bite us) and shows it inline via
 * an object-URL blob wrapped in an <c>&lt;iframe&gt;</c>. Also
 * provides a Download button that saves the same blob to disk under
 * <c>fallbackFilename</c> (or the server-provided filename when a
 * <c>Content-Disposition</c> header is present).
 *
 * When the modal is closed we revoke the object URL and abort any
 * still-in-flight fetch so we never leak memory or trigger stray
 * state updates on unmounted components.
 */
export type PdfPreviewModalProps = {
    opened: boolean;
    onClose: () => void;
    /** URL served by the API as `application/pdf`. Only fetched while `opened` is true. */
    pdfUrl: string | null;
    /** Filename used when the server does not send a `Content-Disposition`. */
    fallbackFilename: string;
    /** Overrides the default `t("common.pdfPreview.title")`. */
    title?: string;
};

type LoadState =
    | { kind: "idle" }
    | { kind: "loading" }
    | { kind: "ready"; blobUrl: string; downloadName: string }
    | { kind: "error"; message: string };

export function PdfPreviewModal(props: PdfPreviewModalProps) {
    const { t } = useTranslation();
    const [state, setState] = useState<LoadState>({ kind: "idle" });
    const abortRef = useRef<AbortController | null>(null);
    const blobUrlRef = useRef<string | null>(null);

    // Fetch (or reset) the PDF whenever the modal opens with a URL.
    useEffect(() => {
        if (!props.opened || !props.pdfUrl) {
            cleanup();
            setState({ kind: "idle" });
            return;
        }

        const controller = new AbortController();
        abortRef.current = controller;
        setState({ kind: "loading" });

        const token = useSessionStore.getState().token;
        const headers = new Headers();
        if (token) headers.set("Authorization", `Bearer ${token}`);

        void (async () => {
            try {
                const response = await fetch(props.pdfUrl!, {
                    headers,
                    signal: controller.signal,
                });
                if (!response.ok) {
                    const body = await response.text().catch(() => "");
                    throw new ApiError(response.status, response.statusText, body);
                }
                const disposition = response.headers.get("Content-Disposition");
                const downloadName =
                    extractFilename(disposition) ?? props.fallbackFilename;
                const blob = await response.blob();
                if (controller.signal.aborted) return;
                const blobUrl = URL.createObjectURL(blob);
                blobUrlRef.current = blobUrl;
                setState({ kind: "ready", blobUrl, downloadName });
            }
            catch (err) {
                if (controller.signal.aborted) return;
                setState({
                    kind: "error",
                    message: describeApiError(err, t).message,
                });
            }
        })();

        return () => {
            controller.abort();
        };
    }, [props.opened, props.pdfUrl, props.fallbackFilename, t]);

    // Belt-and-braces: revoke the object URL on unmount.
    useEffect(() => {
        return () => cleanup();
    }, []);

    function cleanup() {
        abortRef.current?.abort();
        abortRef.current = null;
        if (blobUrlRef.current) {
            URL.revokeObjectURL(blobUrlRef.current);
            blobUrlRef.current = null;
        }
    }

    function handleDownload() {
        if (state.kind !== "ready") return;
        const anchor = document.createElement("a");
        anchor.href = state.blobUrl;
        anchor.download = state.downloadName;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
    }

    return (
        <Modal
            opened={props.opened}
            onClose={props.onClose}
            title={props.title ?? t("common.pdfPreview.title")}
            size="90%"
            centered
            data-testid="pdf-preview-modal"
        >
            <Stack gap="sm">
                {state.kind === "loading" && (
                    <Center py="xl" data-testid="pdf-preview-loading">
                        <Group gap="sm">
                            <Loader size="sm" />
                            <Text>{t("common.pdfPreview.loading")}</Text>
                        </Group>
                    </Center>
                )}
                {state.kind === "error" && (
                    <Alert
                        role="alert"
                        icon={<IconAlertCircle size={16} />}
                        color="red"
                        variant="light"
                        title={t("common.pdfPreview.errorTitle")}
                        data-testid="pdf-preview-error"
                    >
                        {state.message}
                    </Alert>
                )}
                {state.kind === "ready" && (
                    <Box
                        component="iframe"
                        src={state.blobUrl}
                        title={props.title ?? t("common.pdfPreview.title")}
                        w="100%"
                        h={640}
                        style={{ border: "none" }}
                        data-testid="pdf-preview-frame"
                    >
                        {t("common.pdfPreview.noSupport")}
                    </Box>
                )}
                <Group justify="flex-end">
                    <Button
                        variant="subtle"
                        leftSection={<IconX size={14} />}
                        onClick={props.onClose}
                    >
                        {t("common.pdfPreview.close")}
                    </Button>
                    <Button
                        leftSection={<IconDownload size={14} />}
                        onClick={handleDownload}
                        disabled={state.kind !== "ready"}
                        data-testid="pdf-preview-download"
                    >
                        {t("common.pdfPreview.download")}
                    </Button>
                </Group>
            </Stack>
        </Modal>
    );
}

// ---------------------------------------------------------------
// Content-Disposition helpers (copied verbatim from reportExport.ts
// so we don't create a cross-import cycle; the two locations must
// stay in sync).
// ---------------------------------------------------------------

function extractFilename(header: string | null): string | null {
    if (!header) return null;
    const utf8 = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(header);
    if (utf8) {
        try { return decodeURIComponent(utf8[1].trim()); }
        catch { /* fall through */ }
    }
    const quoted = /filename\s*=\s*"([^"]+)"/i.exec(header);
    if (quoted) return quoted[1];
    const bare = /filename\s*=\s*([^;]+)/i.exec(header);
    if (bare) return bare[1].trim();
    return null;
}
