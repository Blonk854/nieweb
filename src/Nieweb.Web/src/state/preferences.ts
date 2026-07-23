import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";

/**
 * Sentinel value that tells the app "use whatever time zone the
 * browser reports via `Intl.DateTimeFormat().resolvedOptions()`". We
 * intentionally store this as a string rather than `null` so the
 * `<Select>` control (which represents "no selection" as `null`) has
 * an explicit option to pick.
 */
export const AUTO_TIME_ZONE = "auto" as const;

export type TimeZonePreference = typeof AUTO_TIME_ZONE | string;

type PreferencesState = {
    /**
     * IANA time-zone name (e.g. `Europe/Paris`, `America/New_York`) or
     * `"auto"` to follow the browser. Every place in the UI that
     * formats a timestamp should feed this through
     * `resolveTimeZone(preference)` to get an IANA string suitable for
     * `Intl.DateTimeFormat({ timeZone })`.
     */
    timeZone: TimeZonePreference;
    setTimeZone: (value: TimeZonePreference) => void;
    reset: () => void;
};

/**
 * Preferences store: per-browser UI knobs that do NOT belong on the
 * server. Persisted to `localStorage` so a page reload keeps the user
 * on the same time zone / theme / date format. Distinct from
 * `useSessionStore` (auth) so signing out never wipes preferences.
 *
 * Phase B introduces this store with `timeZone` only; future
 * preferences (theme, density, chart palette, …) should extend the
 * shape here rather than creating another `localStorage` island.
 */
export const usePreferencesStore = create<PreferencesState>()(
    persist(
        (set) => ({
            timeZone: AUTO_TIME_ZONE,
            setTimeZone: (value) => set({ timeZone: value }),
            reset: () => set({ timeZone: AUTO_TIME_ZONE }),
        }),
        {
            name: "nieweb.preferences.v1",
            storage: createJSONStorage(() => localStorage),
            partialize: (s) => ({ timeZone: s.timeZone }),
        },
    ),
);

/**
 * Resolve the stored preference into a concrete IANA time-zone name
 * usable by `Intl.DateTimeFormat`. When the preference is `"auto"`
 * (the default) we ask the browser what it thinks — `Intl` returns
 * the system-configured zone (e.g. `Europe/Paris`). When the
 * environment cannot answer (extremely rare — a broken jsdom stub),
 * fall back to `UTC` so timestamps never blow up.
 */
export function resolveTimeZone(preference: TimeZonePreference): string {
    if (preference !== AUTO_TIME_ZONE) {
        return preference;
    }
    try {
        const tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
        return tz && tz.length > 0 ? tz : "UTC";
    } catch {
        return "UTC";
    }
}
