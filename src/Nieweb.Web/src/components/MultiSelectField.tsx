import { MultiSelect, TagsInput } from "@mantine/core";
import type { MultiSelectProps, TagsInputProps } from "@mantine/core";

/**
 * Mantine keeps a pills input's `placeholder` visible next to the selected
 * pills, so a filter that reads "All lines" while `Line 3` and `Line 5` are
 * picked contradicts itself. These thin wrappers drop the placeholder as
 * soon as anything is selected, and are used everywhere in place of the raw
 * Mantine components so the behaviour cannot drift per screen.
 */
export function MultiSelectField({ placeholder, value, ...rest }: MultiSelectProps) {
    return (
        <MultiSelect
            {...rest}
            value={value}
            placeholder={hasSelection(value) ? undefined : placeholder}
        />
    );
}

/** {@link MultiSelectField} for free-text pills. */
export function TagsInputField({ placeholder, value, ...rest }: TagsInputProps) {
    return (
        <TagsInput
            {...rest}
            value={value}
            placeholder={hasSelection(value) ? undefined : placeholder}
        />
    );
}

function hasSelection(value: string[] | undefined): boolean {
    return value !== undefined && value.length > 0;
}
