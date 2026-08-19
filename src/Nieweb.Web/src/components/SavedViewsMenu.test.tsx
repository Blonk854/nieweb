import { beforeEach, describe, expect, it, vi, type MockInstance } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { I18nextProvider } from "react-i18next";
import { SavedViewsMenu } from "./SavedViewsMenu";
import * as api from "../api/savedViews";
import type { SavedView } from "../api/savedViews";
import i18n from "../i18n";

// -----------------------------------------------------------------
// Test harness
// -----------------------------------------------------------------

type WrapperProps = { children: React.ReactNode };

function makeWrapper() {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: 0 } },
    });
    return function Wrapper({ children }: WrapperProps) {
        return (
            <I18nextProvider i18n={i18n}>
                <MantineProvider>
                    <QueryClientProvider client={client}>
                        {children}
                    </QueryClientProvider>
                </MantineProvider>
            </I18nextProvider>
        );
    };
}

function view(overrides: Partial<SavedView> = {}): SavedView {
    return {
        id: 1,
        name: "Default",
        reportKey: "panel-yield",
        filterJson: "{}",
        isShared: false,
        isOwner: true,
        createdUtc: "2026-01-01T00:00:00Z",
        lastModifiedUtc: "2026-01-01T00:00:00Z",
        ...overrides,
    };
}

async function openMenu(user: ReturnType<typeof userEvent.setup>): Promise<void> {
    await user.click(screen.getByRole("button", { name: /Saved views/i }));
    await screen.findByRole("menu", { hidden: true });
}

function menuItems(): HTMLElement[] {
    return screen.getAllByRole("menuitem", { hidden: true });
}

function findItemByText(text: RegExp): HTMLElement | undefined {
    return menuItems().find((el) => text.test(el.textContent ?? ""));
}

let fetchSpy: MockInstance<typeof api.fetchSavedViews>;
let createSpy: MockInstance<typeof api.createSavedView>;
let deleteSpy: MockInstance<typeof api.deleteSavedView>;

beforeEach(() => {
    vi.restoreAllMocks();
    fetchSpy = vi.spyOn(api, "fetchSavedViews");
    createSpy = vi.spyOn(api, "createSavedView");
    deleteSpy = vi.spyOn(api, "deleteSavedView");
});

// -----------------------------------------------------------------

describe("SavedViewsMenu", () => {
    it("shows an empty message when the user has no saved views yet", async () => {
        fetchSpy.mockResolvedValue([]);
        const user = userEvent.setup();
        render(
            <SavedViewsMenu
                reportKey="panel-yield"
                currentFilter={{}}
                onApply={() => {}}
            />,
            { wrapper: makeWrapper() },
        );

        await openMenu(user);
        expect(await screen.findByText(/No saved views yet/i)).toBeInTheDocument();
        expect(fetchSpy).toHaveBeenCalledWith("panel-yield");
    });

    it("renders a dirty-state callout when the current filter has unsaved edits", async () => {
        fetchSpy.mockResolvedValue([]);
        const user = userEvent.setup();
        render(
            <SavedViewsMenu
                reportKey="panel-yield"
                currentFilter={{ sourceId: "postreflow" }}
                onApply={() => {}}
                isDirty
            />,
            { wrapper: makeWrapper() },
        );

        await openMenu(user);
        expect(await screen.findByText(/Unsaved changes/i)).toBeInTheDocument();
        expect(screen.getByText(/Save current view/i)).toBeInTheDocument();
    });

    it("shows a hover hint when the user cannot save yet", async () => {
        fetchSpy.mockResolvedValue([]);
        const user = userEvent.setup();
        render(
            <SavedViewsMenu
                reportKey="panel-yield"
                currentFilter={{}}
                onApply={() => {}}
                canSave={false}
            />,
            { wrapper: makeWrapper() },
        );

        await openMenu(user);
        const saveItem = findItemByText(/Save current view/i);
        expect(saveItem).toBeDefined();
        await user.hover(saveItem!);
        expect(await screen.findByText(/Select a source and date range before saving/i)).toBeInTheDocument();
    });

    it("lists own views under 'Mine' and applies the parsed filter on click", async () => {
        fetchSpy.mockResolvedValue([
            view({ id: 10, name: "Yield yesterday", filterJson: '{"sourceId":"postreflow"}' }),
        ]);
        const applied: unknown[] = [];
        const user = userEvent.setup();
        render(
            <SavedViewsMenu
                reportKey="panel-yield"
                currentFilter={{}}
                onApply={(f) => applied.push(f)}
            />,
            { wrapper: makeWrapper() },
        );

        await openMenu(user);
        await screen.findByText("Yield yesterday");
        const item = findItemByText(/Yield yesterday/);
        expect(item).toBeDefined();
        await user.click(item!);

        expect(applied).toEqual([{ sourceId: "postreflow" }]);
    });

    it("segregates 'Mine' and 'Shared by others' groups by isOwner", async () => {
        fetchSpy.mockResolvedValue([
            view({ id: 1, name: "Alpha", isOwner: true }),
            view({ id: 2, name: "Beta", isOwner: false, isShared: true }),
        ]);
        const user = userEvent.setup();
        render(
            <SavedViewsMenu
                reportKey="panel-yield"
                currentFilter={{}}
                onApply={() => {}}
            />,
            { wrapper: makeWrapper() },
        );

        await openMenu(user);
        await screen.findByText("Alpha");
        expect(screen.getByText("Mine")).toBeInTheDocument();
        expect(screen.getByText("Shared by others")).toBeInTheDocument();
        expect(findItemByText(/Alpha/)).toBeDefined();
        expect(findItemByText(/Beta/)).toBeDefined();
    });

    it("shows the delete affordance only for owner rows", async () => {
        fetchSpy.mockResolvedValue([
            view({ id: 1, name: "Alpha", isOwner: true }),
            view({ id: 2, name: "Beta", isOwner: false, isShared: true }),
        ]);
        const user = userEvent.setup();
        render(
            <SavedViewsMenu
                reportKey="panel-yield"
                currentFilter={{}}
                onApply={() => {}}
            />,
            { wrapper: makeWrapper() },
        );

        await openMenu(user);
        await screen.findByText("Alpha");

        expect(
            screen.getByRole("button", { name: /Delete: Alpha/i, hidden: true }),
        ).toBeInTheDocument();
        expect(
            screen.queryByRole("button", { name: /Delete: Beta/i, hidden: true }),
        ).not.toBeInTheDocument();
    });

    it("creates a new view when the modal is submitted", async () => {
        fetchSpy.mockResolvedValue([]);
        createSpy.mockResolvedValue(
            view({ id: 42, name: "Line 1 day shift" }),
        );

        const user = userEvent.setup();
        render(
            <SavedViewsMenu
                reportKey="panel-yield"
                currentFilter={{ sourceId: "postreflow" }}
                onApply={() => {}}
            />,
            { wrapper: makeWrapper() },
        );

        await openMenu(user);
        const saveItem = findItemByText(/Save current view/);
        expect(saveItem).toBeDefined();
        await user.click(saveItem!);

        const dialog = await screen.findByRole("dialog", { name: /Save this view/i });
        const nameInput = within(dialog).getByPlaceholderText(/Line 1, day shift/i);
        await user.click(nameInput);
        await user.type(nameInput, "Line 1 day shift");
        await user.click(within(dialog).getByRole("button", { name: /^Save$/i }));

        expect(createSpy).toHaveBeenCalledTimes(1);
        expect(createSpy.mock.calls[0][0]).toEqual({
            reportKey: "panel-yield",
            name: "Line 1 day shift",
            filterJson: '{"sourceId":"postreflow"}',
            isShared: false,
        });
    });

    it("blocks submitting an empty name and does not call the API", async () => {
        fetchSpy.mockResolvedValue([]);
        const user = userEvent.setup();
        render(
            <SavedViewsMenu
                reportKey="panel-yield"
                currentFilter={{}}
                onApply={() => {}}
            />,
            { wrapper: makeWrapper() },
        );

        await openMenu(user);
        const saveItem = findItemByText(/Save current view/);
        expect(saveItem).toBeDefined();
        await user.click(saveItem!);
        const dialog = await screen.findByRole("dialog", { name: /Save this view/i });
        await user.click(within(dialog).getByRole("button", { name: /^Save$/i }));

        expect(await within(dialog).findByText(/Please enter a name/i)).toBeInTheDocument();
        expect(createSpy).not.toHaveBeenCalled();
    });

    it("deletes a view after confirming the delete modal", async () => {
        fetchSpy.mockResolvedValue([view({ id: 7, name: "Nuke me", isOwner: true })]);
        deleteSpy.mockResolvedValue(undefined);

        const user = userEvent.setup();
        render(
            <SavedViewsMenu
                reportKey="panel-yield"
                currentFilter={{}}
                onApply={() => {}}
            />,
            { wrapper: makeWrapper() },
        );

        await openMenu(user);
        await screen.findByText("Nuke me");
        await user.click(
            screen.getByRole("button", { name: /Delete: Nuke me/i, hidden: true }),
        );

        const dialog = await screen.findByRole("dialog", { name: /Delete saved view/i });
        await user.click(within(dialog).getByRole("button", { name: /^Delete$/i }));

        expect(deleteSpy).toHaveBeenCalledTimes(1);
        expect(deleteSpy.mock.calls[0][0]).toBe(7);
    });

    it("disables 'Save current view' when canSave is false", async () => {
        fetchSpy.mockResolvedValue([]);
        const user = userEvent.setup();
        render(
            <SavedViewsMenu
                reportKey="panel-yield"
                currentFilter={{}}
                onApply={() => {}}
                canSave={false}
            />,
            { wrapper: makeWrapper() },
        );

        await openMenu(user);
        const saveItem = findItemByText(/Save current view/);
        expect(saveItem).toBeDefined();
        expect(saveItem).toHaveAttribute("data-disabled");
    });
});
