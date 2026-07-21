import i18n from "i18next";
import LanguageDetector from "i18next-browser-languagedetector";
import { initReactI18next } from "react-i18next";
import { en } from "./locales/en";
import { fr } from "./locales/fr";

/**
 * Supported UI locales. English is the source-of-truth (keys defined
 * in en.ts); French is required by Phase-1 MVP. Adding a locale means
 * a new entry here + a matching bundle in ./locales/.
 */
export const SUPPORTED_LANGUAGES = ["en", "fr"] as const;
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];

const STORAGE_KEY = "nieweb.lang.v1";

export function isSupportedLanguage(v: string): v is SupportedLanguage {
    return (SUPPORTED_LANGUAGES as readonly string[]).includes(v);
}

/**
 * Initialise i18next exactly once. Safe to call from both the app entry
 * (main.tsx) and from tests - a second call returns the same instance
 * without re-adding resources.
 */
export function initI18n(): typeof i18n {
    if (i18n.isInitialized) {
        return i18n;
    }
    void i18n
        .use(LanguageDetector)
        .use(initReactI18next)
        .init({
            resources: {
                en: { translation: en },
                fr: { translation: fr },
            },
            fallbackLng: "en",
            supportedLngs: [...SUPPORTED_LANGUAGES],
            nonExplicitSupportedLngs: true, // fr-CA -> fr
            interpolation: { escapeValue: false }, // React already escapes
            detection: {
                order: ["localStorage", "navigator", "htmlTag"],
                lookupLocalStorage: STORAGE_KEY,
                caches: ["localStorage"],
            },
            returnNull: false,
        });
    return i18n;
}

export { STORAGE_KEY as LANGUAGE_STORAGE_KEY };
export default i18n;
