import type { TranslationBundle } from "./bundle";

/**
 * Type-augment i18next so `t("home.title")` autocompletes keys and
 * rejects typos. Values are typed as `string` (from TranslationBundle)
 * so react-i18next's interpolation checker does not lock every key
 * into a specific `{{var}}` set.
 */
declare module "i18next" {
    interface CustomTypeOptions {
        defaultNS: "translation";
        resources: {
            translation: TranslationBundle;
        };
        returnNull: false;
    }
}
