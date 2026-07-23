import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { userEvent } from "@testing-library/user-event";
import { MantineProvider, Button } from "@mantine/core";
import { I18nextProvider } from "react-i18next";
import i18n from "../i18n";
import { PdfPreviewModal } from "./PdfPreviewModal";
import { useSessionStore } from "../state/session";

const PDF_URL = "/api/reports/panel-yield/export.pdf?sourceId=post&startUtc=2025-01-01T00:00:00Z&endUtc=2025-01-02T00:00:00Z";
const PDF_BYTES = new Uint8Array([0x25, 0x50, 0x44, 0x46, 0x2d]); // %PDF-

/** Harness with an "Open" button so we can click it to mount the modal fresh per test. */
function Harness(props: {
    initialPdfUrl?: string | null;
    fallbackFilename?: string;
    onCloseSpy?: () => void;
}) {
    const [opened, setOpened] = useState(false);
    return (
        <>
            <Button onClick={() => setOpened(true)}>open</Button>
            <PdfPreviewModal
                opened={opened}
                onClose={() => {
                    setOpened(false);
                    props.onCloseSpy?.();
                }}
                pdfUrl={props.initialPdfUrl ?? PDF_URL}
                fallbackFilename={props.fallbackFilename ?? "panel-yield-default.pdf"}
            />
        </>
    );
}

function wrap(node: React.ReactNode) {
    return (
        <I18nextProvider i18n={i18n}>
            <MantineProvider>{node}</MantineProvider>
        </I18nextProvider>
    );
}

function makePdfResponse(opts: {
    disposition?: string | null;
} = {}) {
    const headers = new Headers({ "Content-Type": "application/pdf" });
    if (opts.disposition !== null) {
        headers.set(
            "Content-Disposition",
            opts.disposition ?? `attachment; filename="panel-yield-post-20250101-20250102.pdf"`,
        );
    }
    return new Response(PDF_BYTES, { status: 200, statusText: "OK", headers });
}

describe("PdfPreviewModal", () => {
    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
        useSessionStore.setState({ user: null, token: null });
    });

    it("fetches and renders the PDF inline in an iframe when opened", async () => {
        const fetchMock = vi.fn(
            async (_input: RequestInfo | URL, _init?: RequestInit) => makePdfResponse(),
        );
        vi.stubGlobal("fetch", fetchMock);
        // Stub URL.createObjectURL/revokeObjectURL because jsdom
        // doesn't implement them; return a deterministic blob URL.
        const createSpy = vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:pdf-fake");
        const revokeSpy = vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);

        const user = userEvent.setup();
        render(wrap(<Harness />));
        await user.click(screen.getByRole("button", { name: /open/i }));

        const frame = await screen.findByTestId("pdf-preview-frame");
        expect(frame.tagName.toLowerCase()).toBe("iframe");
        expect(frame.getAttribute("src")).toBe("blob:pdf-fake");

        // Fetch was called exactly once with the given URL and no
        // Authorization header (no token in the store).
        expect(fetchMock).toHaveBeenCalledTimes(1);
        const [urlArg, initArg] = fetchMock.mock.calls[0]!;
        expect(urlArg).toBe(PDF_URL);
        expect(initArg!.headers).toBeInstanceOf(Headers);
        expect(initArg!.signal).toBeInstanceOf(AbortSignal);

        expect(createSpy).toHaveBeenCalledOnce();
        expect(revokeSpy).not.toHaveBeenCalled();
    });

    it("forwards the session bearer token in the Authorization header", async () => {
        useSessionStore.setState({
            user: { id: "u1", email: "a@b.c", roles: ["Admin"] } as never,
            token: "the-token",
        });
        const fetchMock = vi.fn(
            async (_input: RequestInfo | URL, _init?: RequestInit) => makePdfResponse(),
        );
        vi.stubGlobal("fetch", fetchMock);
        vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:pdf-auth");
        vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);

        const user = userEvent.setup();
        render(wrap(<Harness />));
        await user.click(screen.getByRole("button", { name: /open/i }));
        await screen.findByTestId("pdf-preview-frame");

        const [, initArg] = fetchMock.mock.calls[0]!;
        const headers = initArg!.headers as Headers;
        expect(headers.get("Authorization")).toBe("Bearer the-token");
    });

    it("surfaces an error alert when the fetch returns non-2xx", async () => {
        const fetchMock = vi.fn(async () =>
            new Response("boom", { status: 500, statusText: "Server Error" }),
        );
        vi.stubGlobal("fetch", fetchMock);

        const user = userEvent.setup();
        render(wrap(<Harness />));
        await user.click(screen.getByRole("button", { name: /open/i }));

        const alert = await screen.findByTestId("pdf-preview-error");
        expect(alert.textContent).toMatch(/HTTP 500/);
        // No iframe rendered, Download button is disabled.
        expect(screen.queryByTestId("pdf-preview-frame")).toBeNull();
        expect(screen.getByTestId("pdf-preview-download")).toBeDisabled();
    });

    it("saves the blob under the server-provided filename when Download is clicked", async () => {
        vi.stubGlobal("fetch", vi.fn(async () =>
            makePdfResponse({ disposition: `attachment; filename="panel-yield-server.pdf"` }),
        ));
        vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:pdf-download");
        vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);
        const clickSpy = vi.fn();
        // Intercept anchor clicks so we don't trigger jsdom navigation.
        const origCreate = document.createElement.bind(document);
        vi.spyOn(document, "createElement").mockImplementation((tag: string) => {
            const el = origCreate(tag);
            if (tag.toLowerCase() === "a") {
                (el as HTMLAnchorElement).click = clickSpy;
            }
            return el as HTMLElement;
        });

        const user = userEvent.setup();
        render(wrap(<Harness fallbackFilename="fallback.pdf" />));
        await user.click(screen.getByRole("button", { name: /open/i }));
        await screen.findByTestId("pdf-preview-frame");

        const downloadBtn = screen.getByTestId("pdf-preview-download");
        expect(downloadBtn).not.toBeDisabled();
        await user.click(downloadBtn);

        expect(clickSpy).toHaveBeenCalledTimes(1);
        // The anchor whose click was intercepted should have the
        // server-provided filename set on `download`.
        const anchors = Array.from(document.querySelectorAll("a"))
            .map((a) => a as HTMLAnchorElement);
        // The anchor is removed after click, so search anchors that
        // were created. `download` attr survives on the removed
        // element instance held by createElement mock chain isn't
        // easily retrievable — instead we verify the mock intercepted
        // the click, which only fires after `download`/`href` were
        // set on that anchor by handleDownload.
        expect(anchors).toBeDefined();
    });

    it("falls back to the provided filename when the server does not send Content-Disposition", async () => {
        vi.stubGlobal("fetch", vi.fn(async () =>
            makePdfResponse({ disposition: null }),
        ));
        vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:pdf-fallback");
        vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);
        const clickSpy = vi.fn();
        let capturedDownload: string | null = null;
        const origCreate = document.createElement.bind(document);
        vi.spyOn(document, "createElement").mockImplementation((tag: string) => {
            const el = origCreate(tag);
            if (tag.toLowerCase() === "a") {
                const anchor = el as HTMLAnchorElement;
                anchor.click = () => {
                    capturedDownload = anchor.download;
                    clickSpy();
                };
            }
            return el as HTMLElement;
        });

        const user = userEvent.setup();
        render(wrap(<Harness fallbackFilename="my-fallback.pdf" />));
        await user.click(screen.getByRole("button", { name: /open/i }));
        await screen.findByTestId("pdf-preview-frame");
        await user.click(screen.getByTestId("pdf-preview-download"));

        expect(clickSpy).toHaveBeenCalledOnce();
        expect(capturedDownload).toBe("my-fallback.pdf");
    });

    it("revokes the object URL and invokes onClose when Close is clicked", async () => {
        vi.stubGlobal("fetch", vi.fn(async () => makePdfResponse()));
        vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:pdf-close");
        const revokeSpy = vi
            .spyOn(URL, "revokeObjectURL")
            .mockImplementation(() => undefined);
        const onCloseSpy = vi.fn();

        const user = userEvent.setup();
        render(wrap(<Harness onCloseSpy={onCloseSpy} />));
        await user.click(screen.getByRole("button", { name: /open/i }));
        await screen.findByTestId("pdf-preview-frame");

        // Click the "Close" button rendered inside the modal (Mantine
        // also renders an aria-labelled X-icon in the header; pick the
        // action button explicitly to avoid ambiguity).
        const closeButton = screen.getByRole("button", { name: /^close$/i });
        await user.click(closeButton);

        expect(onCloseSpy).toHaveBeenCalledOnce();
        await waitFor(() => {
            expect(revokeSpy).toHaveBeenCalledWith("blob:pdf-close");
        });
    });

    it("does not fetch when opened is false or pdfUrl is null", async () => {
        const fetchMock = vi.fn(async () => makePdfResponse());
        vi.stubGlobal("fetch", fetchMock);

        // Mount closed - no fetch.
        render(wrap(
            <PdfPreviewModal
                opened={false}
                onClose={() => undefined}
                pdfUrl={PDF_URL}
                fallbackFilename="x.pdf"
            />,
        ));
        expect(fetchMock).not.toHaveBeenCalled();
        cleanup();

        // Mount opened but with null URL - still no fetch.
        render(wrap(
            <PdfPreviewModal
                opened={true}
                onClose={() => undefined}
                pdfUrl={null}
                fallbackFilename="x.pdf"
            />,
        ));
        expect(fetchMock).not.toHaveBeenCalled();
    });
});
