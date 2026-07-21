import "@testing-library/jest-dom/vitest";

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
