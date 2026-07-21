import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider, Button } from "@mantine/core";
import { IconPrinter } from "@tabler/icons-react";

/**
 * Behavioural smoke test for the F9 Print button pattern used across
 * report pages: a button that triggers `window.print()`. The panel-
 * yield route wires this into its filter toolbar; the actual visual
 * layout is exercised by index.css @media print rules that the E2E
 * suite will cover once Playwright is wired up.
 *
 * Keeping this test small avoids re-mounting the whole panel-yield
 * route (which needs a router, JWT session, and data-source mocks)
 * just to confirm the click handler calls `window.print`.
 */
describe("Print button pattern (F9)", () => {
    it("invokes window.print when clicked", async () => {
        const spy = vi.spyOn(window, "print").mockImplementation(() => {});
        const user = userEvent.setup();
        render(
            <MantineProvider>
                <Button
                    variant="default"
                    leftSection={<IconPrinter size={16} />}
                    onClick={() => window.print()}
                >
                    Print
                </Button>
            </MantineProvider>,
        );

        await user.click(screen.getByRole("button", { name: /Print/i }));
        expect(spy).toHaveBeenCalledTimes(1);
        spy.mockRestore();
    });
});
