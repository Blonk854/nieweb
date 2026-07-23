import { useMemo } from "react";
import { useTranslation } from "react-i18next";

import { usePreferencesStore, resolveTimeZone } from "../state/preferences";

/**
 * Hook that returns a memoised `Intl.DateTimeFormat` tuned for the
 * current i18n language *and* the user's stored time-zone preference.
 * Callers pass standard `Intl.DateTimeFormatOptions` (dateStyle,
 * timeStyle, hour12, …) — `locale` and `timeZone` are supplied
 * automatically.
 *
 * All admin timestamp columns (Users, Audit trail, Board SVGs, MSA
 * parameters, …) use this so a single Settings → Timezone change
 * propagates everywhere. Before Phase B those callers hard-coded
 * `timeZone: "UTC"`; the switch is transparent to any consumer that
 * did not previously override `timeZone` itself.
 *
 * If a caller *does* pass an explicit `timeZone`, it wins — this hook
 * is for the common case, not a straitjacket.
 */
export function useDateTimeFormatter(
    options: Intl.DateTimeFormatOptions,
): Intl.DateTimeFormat {
    const { i18n } = useTranslation();
    const timeZonePreference = usePreferencesStore((s) => s.timeZone);
    return useMemo(() => {
        const resolved = resolveTimeZone(timeZonePreference);
        return new Intl.DateTimeFormat(i18n.language, {
            timeZone: resolved,
            ...options,
        });
        // JSON-stringify the options object so callers can pass a
        // fresh literal on every render without invalidating the
        // memo. i18n.language and timeZonePreference stay in the
        // dependency array as scalar values.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [i18n.language, timeZonePreference, JSON.stringify(options)]);
}
