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
        adminUsers: string;
        signIn: string;
        signOut: string;
        changePassword: string;
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
            print: string;
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
        kpi: {
            totalPanels: string;
            overallFpy: string;
            freshness: string;
            unknownFreshness: string;
            noPanels: string;
            band: {
                green: string;
                amber: string;
                red: string;
            };
        };
    };
    login: {
        title: string;
        signInHeading: string;
        signedInAs: string;
        signOut: string;
        form: {
            emailLabel: string;
            emailPlaceholder: string;
            passwordLabel: string;
            passwordPlaceholder: string;
            submit: string;
            signingIn: string;
            emailRequired: string;
            emailInvalid: string;
            passwordRequired: string;
            invalidCredentials: string;
            unexpectedError: string;
        };
    };
    account: {
        changePassword: {
            title: string;
            subtitle: string;
            mustRotateBanner: string;
            currentPasswordLabel: string;
            currentPasswordPlaceholder: string;
            newPasswordLabel: string;
            newPasswordPlaceholder: string;
            confirmPasswordLabel: string;
            confirmPasswordPlaceholder: string;
            submit: string;
            submitting: string;
            cancel: string;
            success: string;
            successBody: string;
            continueHome: string;
            currentPasswordRequired: string;
            newPasswordRequired: string;
            confirmPasswordRequired: string;
            confirmMismatch: string;
            sameAsCurrent: string;
            wrongCurrentPassword: string;
            validationFailed: string;
            unexpectedError: string;
        };
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
    freshness: {
        relative: {
            justNow: string;
            secondsAgo: string;
            minutesAgo: string;
            hoursAgo: string;
            daysAgo: string;
            inFuture: string;
        };
    };
    savedViews: {
        menu: string;
        empty: string;
        save: string;
        saveTitle: string;
        namePlaceholder: string;
        nameRequired: string;
        shared: string;
        sharedHint: string;
        create: string;
        cancel: string;
        delete: string;
        confirmDelete: string;
        confirmDeleteBody: string;
        mine: string;
        sharedByOthers: string;
        loadError: string;
        saveError: string;
        deleteError: string;
        saved: string;
        deleted: string;
    };
    admin: {
        users: {
            title: string;
            subtitle: string;
            reload: string;
            createButton: string;
            emptyState: string;
            loadError: string;
            forbidden: string;
            columns: {
                email: string;
                displayName: string;
                roles: string;
                status: string;
                lastLogin: string;
                actions: string;
            };
            status: {
                active: string;
                disabled: string;
            };
            never: string;
            roles: {
                reader: string;
                author: string;
                admin: string;
            };
            actions: {
                edit: string;
                resetPassword: string;
            };
            create: {
                title: string;
                emailLabel: string;
                emailPlaceholder: string;
                displayNameLabel: string;
                displayNamePlaceholder: string;
                passwordLabel: string;
                passwordPlaceholder: string;
                rolesLabel: string;
                rolesPlaceholder: string;
                submit: string;
                cancel: string;
                success: string;
                emailRequired: string;
                emailInvalid: string;
                displayNameRequired: string;
                passwordRequired: string;
                conflict: string;
                validationFailed: string;
                unexpectedError: string;
            };
            edit: {
                title: string;
                displayNameLabel: string;
                rolesLabel: string;
                isDisabledLabel: string;
                isDisabledHint: string;
                submit: string;
                cancel: string;
                success: string;
                conflictLastAdmin: string;
                conflictSelfDisable: string;
                validationFailed: string;
                unexpectedError: string;
            };
            reset: {
                title: string;
                body: string;
                newPasswordLabel: string;
                submit: string;
                cancel: string;
                success: string;
                validationFailed: string;
                unexpectedError: string;
            };
        };
    };
};