import "@testing-library/jest-dom/vitest";
import { initI18n } from "./i18n";

// jsdom does not implement window.matchMedia. Mantine's ColorScheme
// provider, useMediaQuery, and other hooks call it during mount, so we
// stub it with a minimal MediaQueryList shim. Tests that need to
// control media-query behaviour can still override this per-test.
if (typeof window !== "undefined" && !window.matchMedia) {
    window.matchMedia = (query: string): MediaQueryList => ({
        matches: false,
        media: query,
        onchange: null,
        addListener: () => {},
        removeListener: () => {},
        addEventListener: () => {},
        removeEventListener: () => {},
        dispatchEvent: () => false,
    });
}

// jsdom does not implement ResizeObserver. Mantine Select / MultiSelect
// (Combobox) observe the dropdown target for size changes; stub with a
// no-op so those components mount in tests.
if (typeof globalThis.ResizeObserver === "undefined") {
    class ResizeObserverStub {
        observe(): void {}
        unobserve(): void {}
        disconnect(): void {}
    }
    (globalThis as { ResizeObserver: typeof ResizeObserver }).ResizeObserver =
        ResizeObserverStub as unknown as typeof ResizeObserver;
}

// Initialise i18next once for the whole test process. Individual tests
// that need a specific language can call i18n.changeLanguage(...) in
// beforeEach / afterEach.
initI18n();

