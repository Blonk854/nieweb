/**
 * Shape shared by every translation bundle. Leaves are plain `string`
 * (never string-literal) so translated values freely differ across
 * locales while the *key* set stays identical - TypeScript will fail
 * a build if a French key is missing or misspelled.
 */
export type TranslationBundle = {
    app: {
        title: string;
        subtitle: string;
        toggleNavigation: string;
        language: string;
    };
    nav: {
        home: string;
        panelYield: string;
        signIn: string;
    };
    home: {
        title: string;
        intro: string;
        sourcesCard: string;
        sourcesEmpty: string;
        sourcesErrorTitle: string;
        schemaLabel: string;
    };
    panelYield: {
        title: string;
        subtitle: string;
        placeholderBody: string;
    };
    login: {
        title: string;
        signedInAs: string;
        placeholderBody: string;
    };
    common: {
        loading: string;
    };
};
