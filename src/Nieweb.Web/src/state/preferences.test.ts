import { describe, it, expect, beforeEach } from "vitest";
import {
    AUTO_TIME_ZONE,
    resolveTimeZone,
    usePreferencesStore,
} from "./preferences";

describe("preferences store", () => {
    beforeEach(() => {
        localStorage.clear();
        usePreferencesStore.setState({ timeZone: AUTO_TIME_ZONE });
    });

    it("defaults timeZone to the auto sentinel", () => {
        expect(usePreferencesStore.getState().timeZone).toBe(AUTO_TIME_ZONE);
    });

    it("setTimeZone updates the store and persists to localStorage", () => {
        usePreferencesStore.getState().setTimeZone("Europe/Paris");
        expect(usePreferencesStore.getState().timeZone).toBe("Europe/Paris");
        // The persist middleware writes under our namespaced key.
        const raw = localStorage.getItem("nieweb.preferences.v1");
        expect(raw).not.toBeNull();
        expect(raw!).toContain("Europe/Paris");
    });

    it("reset returns the store to the auto sentinel", () => {
        usePreferencesStore.getState().setTimeZone("Asia/Tokyo");
        expect(usePreferencesStore.getState().timeZone).toBe("Asia/Tokyo");
        usePreferencesStore.getState().reset();
        expect(usePreferencesStore.getState().timeZone).toBe(AUTO_TIME_ZONE);
    });
});

describe("resolveTimeZone", () => {
    it("returns the stored IANA name when the preference is not auto", () => {
        expect(resolveTimeZone("Europe/Paris")).toBe("Europe/Paris");
        expect(resolveTimeZone("America/New_York")).toBe("America/New_York");
    });

    it("returns a non-empty IANA name when the preference is auto", () => {
        // In jsdom the resolved zone is the host's zone (or "UTC" in
        // most CI containers). We do not assert on the exact value —
        // only that we get *something* usable by Intl.DateTimeFormat.
        const resolved = resolveTimeZone(AUTO_TIME_ZONE);
        expect(typeof resolved).toBe("string");
        expect(resolved.length).toBeGreaterThan(0);
        // Round-trip through DateTimeFormat to prove it is accepted
        // as a valid IANA zone — throws RangeError otherwise.
        expect(() =>
            new Intl.DateTimeFormat("en", { timeZone: resolved }).format(
                new Date(),
            ),
        ).not.toThrow();
    });
});
