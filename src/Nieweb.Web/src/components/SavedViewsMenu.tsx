import { useState } from "react";
import {
    ActionIcon,
    Button,
    Checkbox,
    Group,
    Loader,
    Menu,
    Modal,
    Stack,
    Text,
    TextInput,
    Tooltip,
} from "@mantine/core";
import {
    IconBookmark,
    IconBookmarkPlus,
    IconTrash,
} from "@tabler/icons-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import {
    createSavedView,
    deleteSavedView,
    fetchSavedViews,
    type SavedView,
} from "../api/savedViews";

/**
 * Reusable "Saved views" affordance for a report page. Renders as a
 * Menu button that:
 *  - lists every view the current user can see for this report
 *    (own views + shared views from other users, coming pre-sorted
 *    from the server);
 *  - lets the user open a modal to save the current filter as a new
 *    named view (optionally shared with everyone);
 *  - lets the *owner* of a row delete it.
 *
 * The component is intentionally report-agnostic. It receives the
 * current filter shape as an opaque JSON-serialisable object plus a
 * callback that applies a filter back to the report. That way the same
 * component can be reused for future reports (defect pareto, MSA, etc.)
 * without any changes.
 */
export type SavedViewsMenuProps<TFilter> = {
    reportKey: string;
    /** The report's current in-URL filter. Serialised as JSON when saving. */
    currentFilter: TFilter;
    /** Applied when the user clicks a saved view. */
    onApply: (filter: TFilter) => void;
    /**
     * Optional guard - when false, the "Save current view" entry is
     * disabled (typically because the current filter is empty / a
     * report hasn't been run yet).
     */
    canSave?: boolean;
    /** Optional dirty-state hint. If true, the menu surfaces a clear note
     * that the current filter differs from the saved URL / last-applied view. */
    isDirty?: boolean;
};

export function SavedViewsMenu<TFilter>(props: SavedViewsMenuProps<TFilter>) {
    const { reportKey, currentFilter, onApply, canSave = true, isDirty = false } = props;
    const { t } = useTranslation();
    const queryClient = useQueryClient();

    const [modalOpen, setModalOpen] = useState(false);
    const [name, setName] = useState("");
    const [nameError, setNameError] = useState<string | null>(null);
    const [isShared, setIsShared] = useState(false);
    const [pendingDelete, setPendingDelete] = useState<SavedView | null>(null);

    const queryKey = ["savedViews", reportKey] as const;

    const listQuery = useQuery({
        queryKey,
        queryFn: () => fetchSavedViews(reportKey),
    });

    const createMutation = useMutation({
        mutationFn: createSavedView,
        onSuccess: async () => {
            await queryClient.invalidateQueries({ queryKey });
            setModalOpen(false);
            setName("");
            setIsShared(false);
        },
    });

    const deleteMutation = useMutation({
        mutationFn: (id: number) => deleteSavedView(id),
        onSuccess: async () => {
            await queryClient.invalidateQueries({ queryKey });
            setPendingDelete(null);
        },
    });

    function openSaveModal() {
        setName("");
        setNameError(null);
        setIsShared(false);
        setModalOpen(true);
    }

    function handleCreate() {
        const trimmed = name.trim();
        if (trimmed.length === 0) {
            setNameError(t("savedViews.nameRequired"));
            return;
        }
        setNameError(null);
        createMutation.mutate({
            reportKey,
            name: trimmed,
            filterJson: JSON.stringify(currentFilter),
            isShared,
        });
    }

    function handleApply(view: SavedView) {
        try {
            const parsed = JSON.parse(view.filterJson) as TFilter;
            onApply(parsed);
        } catch {
            // Corrupt payload - swallow rather than break the menu.
        }
    }

    const rows = listQuery.data ?? [];
    const myViews = rows.filter((v) => v.isOwner);
    const sharedViews = rows.filter((v) => !v.isOwner);

    return (
        <>
            <Menu
                shadow="md"
                width={320}
                position="bottom-start"
                closeOnItemClick={false}
            >
                <Menu.Target>
                    <Button
                        variant="default"
                        leftSection={<IconBookmark size={16} />}
                        aria-label={t("savedViews.menu")}
                    >
                        {t("savedViews.menu")}
                    </Button>
                </Menu.Target>
                <Menu.Dropdown>
                    {isDirty && (
                        <Menu.Label c="yellow">
                            {t("savedViews.unsavedChanges")}
                        </Menu.Label>
                    )}

                    {!canSave ? (
                        <Tooltip label={t("savedViews.saveDisabledHint")}>
                            <span>
                                <Menu.Item
                                    leftSection={<IconBookmarkPlus size={16} />}
                                    onClick={openSaveModal}
                                    disabled
                                >
                                    {t("savedViews.save")}
                                </Menu.Item>
                            </span>
                        </Tooltip>
                    ) : (
                        <Menu.Item
                            leftSection={<IconBookmarkPlus size={16} />}
                            onClick={openSaveModal}
                        >
                            {t("savedViews.save")}
                        </Menu.Item>
                    )}

                    {listQuery.isPending && (
                        <Menu.Item disabled>
                            <Group gap="xs">
                                <Loader size="xs" />
                                <Text size="sm">{t("common.loading")}</Text>
                            </Group>
                        </Menu.Item>
                    )}
                    {listQuery.error && (
                        <Menu.Item disabled c="red">
                            {t("savedViews.loadError")}
                        </Menu.Item>
                    )}

                    {!listQuery.isPending && !listQuery.error && rows.length === 0 && (
                        <Menu.Item disabled>{t("savedViews.empty")}</Menu.Item>
                    )}

                    {myViews.length > 0 && (
                        <>
                            <Menu.Divider />
                            <Menu.Label>{t("savedViews.mine")}</Menu.Label>
                            {myViews.map((v) => (
                                <SavedViewRow
                                    key={v.id}
                                    view={v}
                                    onApply={handleApply}
                                    onDelete={setPendingDelete}
                                />
                            ))}
                        </>
                    )}

                    {sharedViews.length > 0 && (
                        <>
                            <Menu.Divider />
                            <Menu.Label>{t("savedViews.sharedByOthers")}</Menu.Label>
                            {sharedViews.map((v) => (
                                <SavedViewRow
                                    key={v.id}
                                    view={v}
                                    onApply={handleApply}
                                    onDelete={setPendingDelete}
                                />
                            ))}
                        </>
                    )}
                </Menu.Dropdown>
            </Menu>

            <Modal
                opened={modalOpen}
                onClose={() => setModalOpen(false)}
                title={t("savedViews.saveTitle")}
                centered
            >
                <Stack>
                    <TextInput
                        label={t("savedViews.saveTitle")}
                        placeholder={t("savedViews.namePlaceholder")}
                        value={name}
                        onChange={(e) => setName(e.currentTarget.value)}
                        error={nameError}
                        autoFocus
                        maxLength={100}
                        data-autofocus
                    />
                    <Checkbox
                        label={t("savedViews.shared")}
                        description={t("savedViews.sharedHint")}
                        checked={isShared}
                        onChange={(e) => setIsShared(e.currentTarget.checked)}
                    />
                    {createMutation.error && (
                        <Text c="red" size="sm">
                            {t("savedViews.saveError")}
                        </Text>
                    )}
                    <Group justify="flex-end">
                        <Button
                            variant="subtle"
                            onClick={() => setModalOpen(false)}
                            type="button"
                        >
                            {t("savedViews.cancel")}
                        </Button>
                        <Button
                            onClick={handleCreate}
                            loading={createMutation.isPending}
                        >
                            {t("savedViews.create")}
                        </Button>
                    </Group>
                </Stack>
            </Modal>

            <Modal
                opened={pendingDelete !== null}
                onClose={() => setPendingDelete(null)}
                title={t("savedViews.confirmDelete")}
                centered
            >
                <Stack>
                    <Text>
                        {t("savedViews.confirmDeleteBody", {
                            name: pendingDelete?.name ?? "",
                        })}
                    </Text>
                    {deleteMutation.error && (
                        <Text c="red" size="sm">
                            {t("savedViews.deleteError")}
                        </Text>
                    )}
                    <Group justify="flex-end">
                        <Button
                            variant="subtle"
                            onClick={() => setPendingDelete(null)}
                            type="button"
                        >
                            {t("savedViews.cancel")}
                        </Button>
                        <Button
                            color="red"
                            onClick={() =>
                                pendingDelete &&
                                deleteMutation.mutate(pendingDelete.id)
                            }
                            loading={deleteMutation.isPending}
                        >
                            {t("savedViews.delete")}
                        </Button>
                    </Group>
                </Stack>
            </Modal>
        </>
    );
}

function SavedViewRow(props: {
    view: SavedView;
    onApply: (view: SavedView) => void;
    onDelete: (view: SavedView) => void;
}) {
    const { t } = useTranslation();
    const { view, onApply, onDelete } = props;

    return (
        <Menu.Item
            onClick={() => onApply(view)}
            rightSection={
                view.isOwner ? (
                    <Tooltip label={t("savedViews.delete")}>
                        <ActionIcon
                            size="sm"
                            variant="subtle"
                            color="red"
                            aria-label={`${t("savedViews.delete")}: ${view.name}`}
                            onClick={(e) => {
                                e.stopPropagation();
                                onDelete(view);
                            }}
                        >
                            <IconTrash size={14} />
                        </ActionIcon>
                    </Tooltip>
                ) : null
            }
        >
            <Text truncate>{view.name}</Text>
        </Menu.Item>
    );
}
