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
        pareto: string;
        adminUsers: string;
        adminAudit: string;
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
    pareto: {
        title: string;
        subtitle: string;
        axis: {
            Defect: string;
            Product: string;
            AoiMachine: string;
            ReferenceDesignator: string;
            PartNumber: string;
            Jedec: string;
        };
        numerator: {
            Aoi: string;
            Real: string;
            Dummy: string;
        };
        opportunity: {
            All: string;
            Components: string;
            Paste: string;
        };
        filters: {
            heading: string;
            source: string;
            sourcePlaceholder: string;
            axis: string;
            from: string;
            to: string;
            numerator: string;
            opportunity: string;
            topN: string;
            topNHint: string;
            vitalFewThreshold: string;
            vitalFewThresholdHint: string;
            machines: string;
            machinesPlaceholder: string;
            products: string;
            productsPlaceholder: string;
            recipes: string;
            recipesPlaceholder: string;
            defectBitsChipsLabel: string;
            defectBitChip: string;
            removeDefectBit: string;
            submit: string;
            reset: string;
            print: string;
            exportCsv: string;
            exportXlsx: string;
            emptyPrompt: string;
            missingRequired: string;
        };
        results: {
            heading: string;
            errorTitle: string;
            source: string;
            window: string;
            axis: string;
            totalDefects: string;
            totalOpportunities: string;
            overallDpmoPpm: string;
            noRows: string;
            groupName: string;
            defectCount: string;
            opportunityCount: string;
            dpmoPpm: string;
            defectSharePercent: string;
            cumulativePercent: string;
            isVitalFew: string;
        };
        chart: {
            seriesDefects: string;
            seriesCumulative: string;
            yLeftDefects: string;
            yRightCumulative: string;
            vitalFew: string;
            defectCount: string;
            opportunityCount: string;
            dpmoPpm: string;
            defectShare: string;
            cumulative: string;
            emptyChart: string;
            ariaSummary: string;
            axis: {
                Defect: string;
                Product: string;
                AoiMachine: string;
                ReferenceDesignator: string;
                PartNumber: string;
                Jedec: string;
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
            ssoDivider: string;
            ssoButton: string;
            ssoError: string;
            ssoErrorLocalConflict: string;
            ssoUnknownError: string;
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
        audit: {
            title: string;
            subtitle: string;
            reload: string;
            forbidden: string;
            loadError: string;
            emptyState: string;
            filters: {
                heading: string;
                eventType: string;
                eventTypePlaceholder: string;
                targetType: string;
                targetTypePlaceholder: string;
                targetId: string;
                targetIdPlaceholder: string;
                actorUserId: string;
                actorUserIdPlaceholder: string;
                fromUtc: string;
                toUtc: string;
                apply: string;
                reset: string;
                pageSize: string;
            };
            columns: {
                when: string;
                actor: string;
                eventType: string;
                target: string;
                ip: string;
                details: string;
            };
            anonymous: string;
            noIp: string;
            pagination: {
                summary: string;
                previous: string;
                next: string;
                pageOf: string;
            };
        };
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