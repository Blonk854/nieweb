import type { TranslationBundle } from "../bundle";

/**
 * English (source-of-truth) translation bundle.
 *
 * When adding a new key: extend TranslationBundle in ../bundle.ts, add
 * the English wording here, then add the French wording in fr.ts on
 * the same line. TypeScript will fail the build if any locale is
 * missing a key.
 */
export const en: TranslationBundle = {
    app: {
        title: "Nieweb",
        subtitle: "Phase 1 MVP",
        toggleNavigation: "Toggle navigation",
        language: "Language",
    },
    nav: {
        home: "Home",
        panelYield: "Panel Yield by Line",
        signIn: "Sign in",
    },
    home: {
        title: "Welcome to Nieweb",
        intro: "Phase 1 MVP scaffold. Head to <1>Panel Yield by Line</1> to try the first report.",
        sourcesCard: "Configured AOI sources",
        sourcesEmpty: "No sources configured.",
        sourcesErrorTitle: "Could not reach the API",
        schemaLabel: "schema {{version}}",
    },
    panelYield: {
        title: "Panel Yield by Line",
        subtitle:
            "First-panel-yield across every AOI line, split by source and date window.",
        placeholderBody:
            "The report UI ships in later backlog items (F4-F7). The API is already live at <1>GET /api/reports/panel-yield</1> with CSV and XLSX exports under <3>/export.csv</3> and <5>/export.xlsx</5>.",
        filters: {
            heading: "Filters",
            source: "Source",
            sourcePlaceholder: "Pick an AOI source",
            from: "From (UTC)",
            to: "To (UTC, exclusive)",
            machines: "Machines",
            machinesPlaceholder: "All machines",
            products: "Products",
            productsPlaceholder: "All products",
            recipes: "Recipes",
            recipesPlaceholder: "All recipes",
            onlyLastInspection: "Only last inspection",
            onlyLastInspectionHint:
                "Post-reflow sources only. When on, each panel's last inspection is counted.",
            submit: "Run report",
            reset: "Reset",
            exportCsv: "Export CSV",
            exportXlsx: "Export XLSX",
            emptyPrompt: "Pick a source and date window, then run the report.",
            missingRequired: "Source, From, and To are required.",
        },
        results: {
            heading: "Results",
            noRows: "No machines produced panels in this window.",
            source: "Source",
            window: "Window",
            overall: "Overall",
            totalPanels: "Total panels",
            inspectedPanels: "Inspected",
            goodPanels: "Good",
            faultyPanels: "Faulty",
            notInspectedPanels: "Not inspected",
            fpyPercent: "FPY (%)",
            machineName: "Machine",
        },
        chart: {
            heading: "FPY by machine",
            axisMachine: "Machine",
            axisFpy: "FPY (%)",
            overallFpy: "Overall",
            thresholdGreen: "Green threshold",
            thresholdAmber: "Amber threshold",
            emptyChart: "No data to chart.",
            ariaSummary: "Bar chart of first-pass yield for {{count}} machines.",
        },
    },
    login: {
        title: "Sign in",
        signedInAs: "Signed in as <1>{{displayName}}</1> ({{email}}).",
        placeholderBody:
            "Sign-in form goes here. For now, a token can be obtained via <1>POST /api/auth/token</1> and pushed into the Zustand session store from the browser console (temporary dev affordance).",
    },
    common: {
        loading: "Loading…",
    },
};
