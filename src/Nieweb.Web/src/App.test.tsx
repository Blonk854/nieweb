import { describe, it, expect, afterEach, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import App from "./App";

describe("App", () => {
    afterEach(() => {
        cleanup();
        vi.restoreAllMocks();
    });

    it("renders the Nieweb heading", async () => {
        vi.stubGlobal(
            "fetch",
            vi.fn(() =>
                Promise.resolve(
                    new Response(JSON.stringify([]), {
                        status: 200,
                        headers: { "content-type": "application/json" },
                    }),
                ),
            ),
        );

        render(<App />);

        expect(
            screen.getByRole("heading", { level: 1, name: /nieweb/i }),
        ).toBeInTheDocument();
        // Wait for the async /api/sources call to settle so the state
        // update happens inside React's act(...) window.
        await waitFor(() =>
            expect(screen.getByText(/no sources configured/i)).toBeInTheDocument(),
        );
    });

    it("shows sources returned by /api/sources", async () => {
        vi.stubGlobal(
            "fetch",
            vi.fn(() =>
                Promise.resolve(
                    new Response(
                        JSON.stringify([
                            { id: "postreflow", displayName: "Post-reflow AOI" },
                        ]),
                        {
                            status: 200,
                            headers: { "content-type": "application/json" },
                        },
                    ),
                ),
            ),
        );

        render(<App />);

        await waitFor(() =>
            expect(screen.getByText(/Post-reflow AOI/)).toBeInTheDocument(),
        );
    });

    it("surfaces an error when /api/sources fails", async () => {
        vi.stubGlobal(
            "fetch",
            vi.fn(() =>
                Promise.resolve(
                    new Response("boom", { status: 500 }),
                ),
            ),
        );

        render(<App />);

        await waitFor(() =>
            expect(screen.getByRole("alert")).toHaveTextContent(/HTTP 500/),
        );
    });
});
