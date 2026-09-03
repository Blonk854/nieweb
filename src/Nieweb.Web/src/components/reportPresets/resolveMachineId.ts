import type { MachineOption } from "../../api/sources";

/**
 * Resolve a machine display name (e.g. `L6PSTAOI`) to its AOI machine id.
 * Comparison is case-insensitive and trims whitespace.
 */
export function resolveMachineId(
    machines: readonly MachineOption[],
    displayName: string,
): number | null {
    const needle = displayName.trim().toLowerCase();
    if (needle.length === 0) return null;
    const hit = machines.find((m) => m.name.trim().toLowerCase() === needle);
    return hit?.id ?? null;
}
