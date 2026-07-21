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
        filters: {
            heading: string;
            source: string;
            sourcePlaceholder: string;
            from: string;
            to: string;
            machines: string;
            machinesPlaceholder: string;
            products: string;
            productsPlaceholder: string;
            recipes: string;
            recipesPlaceholder: string;
            onlyLastInspection: string;
            onlyLastInspectionHint: string;
            submit: string;
            reset: string;
            exportCsv: string;
            exportXlsx: string;
            emptyPrompt: string;
            missingRequired: string;
        };
        results: {
            heading: string;
            noRows: string;
            source: string;
            window: string;
            overall: string;
            totalPanels: string;
            inspectedPanels: string;
            goodPanels: string;
            faultyPanels: string;
            notInspectedPanels: string;
            fpyPercent: string;
            machineName: string;
        };
        chart: {
            heading: string;
            axisMachine: string;
            axisFpy: string;
            overallFpy: string;
            thresholdGreen: string;
            thresholdAmber: string;
            emptyChart: string;
            ariaSummary: string;
        };
    };
    login: {
        title: string;
        signedInAs: string;
        placeholderBody: string;
    };
    common: {
        loading: string;
    };
    table: {
        columns: string;
        downloadCsv: string;
        pageSize: string;
        noRows: string;
        rowCount: string;
    };
};
