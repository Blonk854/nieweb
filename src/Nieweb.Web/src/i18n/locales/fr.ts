import type { TranslationBundle } from "../bundle";

/**
 * French translation bundle. Must expose the same keys as en.ts -
 * TypeScript enforces the shape via TranslationBundle.
 */
export const fr: TranslationBundle = {
    app: {
        title: "Nieweb",
        subtitle: "MVP phase 1",
        toggleNavigation: "Basculer la navigation",
        language: "Langue",
    },
    nav: {
        home: "Accueil",
        panelYield: "Rendement panneau par ligne",
        signIn: "Connexion",
    },
    home: {
        title: "Bienvenue sur Nieweb",
        intro: "Prototype MVP phase 1. Accédez au <1>Rendement panneau par ligne</1> pour essayer le premier rapport.",
        sourcesCard: "Sources AOI configurées",
        sourcesEmpty: "Aucune source configurée.",
        sourcesErrorTitle: "Impossible de joindre l'API",
        schemaLabel: "schéma {{version}}",
    },
    panelYield: {
        title: "Rendement panneau par ligne",
        subtitle:
            "Rendement au premier passage sur toutes les lignes AOI, ventilé par source et fenêtre temporelle.",
        placeholderBody:
            "L'interface du rapport arrivera dans les prochains lots (F4-F7). L'API est déjà disponible sur <1>GET /api/reports/panel-yield</1> avec des exports CSV et XLSX sous <3>/export.csv</3> et <5>/export.xlsx</5>.",
    },
    login: {
        title: "Connexion",
        signedInAs: "Connecté en tant que <1>{{displayName}}</1> ({{email}}).",
        placeholderBody:
            "Le formulaire de connexion arrivera ici. Pour l'instant, un jeton peut être obtenu via <1>POST /api/auth/token</1> puis injecté dans le store Zustand depuis la console du navigateur (moyen de développement temporaire).",
    },
    common: {
        loading: "Chargement…",
    },
};
