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
        fpyTrend: string;
        pareto: string;
        dpmo: string;
        dpmoTrend: string;
        fpy: string;
        skipSummary: string;
        canvasDemo: string;
        boardTrace: string;
        adminUsers: string;
        adminAudit: string;
        adminReports: string;
        myReports: string;
        oldSchool: string;
        adminBoardSvgs: string;
        adminParameters: string;
        adminSkipClassification: string;
        adminProductionLines: string;
        adminShifts: string;
        settings: string;
        settingsTimezone: string;
        settingsDatabases: string;
        signIn: string;
        signOut: string;
        changePassword: string;
    };
    myReports: {
        title: string;
        subtitle: string;
        forbidden: string;
        newReport: string;
        empty: string;
        locked: string;
        open: string;
        duplicate: string;
        delete: string;
        cancel: string;
        deleteConfirmTitle: string;
        deleteConfirmBody: string;
        columns: {
            title: string;
            tiles: string;
            updated: string;
        };
        create: {
            title: string;
            templateLabel: string;
            titleLabel: string;
            titlePlaceholder: string;
            descriptionLabel: string;
            submit: string;
            titleRequired: string;
            unexpectedError: string;
        };
    };
    oldSchool: {
        title: string;
        subtitle: string;
        forbidden: string;
        breadcrumbRoot: string;
        loading: string;
        list: {
            newReport: string;
            open: string;
            delete: string;
            duplicate: string;
            empty: string;
            deleteConfirmTitle: string;
            deleteConfirmBody: string;
            cancel: string;
            columns: { title: string; entities: string; updated: string };
            create: {
                title: string;
                templateLabel: string;
                titleLabel: string;
                titlePlaceholder: string;
                submit: string;
                titleRequired: string;
                unexpectedError: string;
            };
        };
        layout: {
            heading: string;
            propertiesHeading: string;
            titleLabel: string;
            groupLabel: string;
            groupNone: string;
            descriptionLabel: string;
            refreshLabel: string;
            refreshHelp: string;
            columnsLabel: string;
            oneColumn: string;
            twoColumns: string;
            contentHeading: string;
            addEntity: string;
            noEntities: string;
            edit: string;
            remove: string;
            moveUp: string;
            moveDown: string;
            view: string;
            back: string;
            save: string;
            saved: string;
            saveError: string;
        };
        newEntity: {
            heading: string;
            subtitle: string;
            comment: string;
            commentDesc: string;
            chart: string;
            chartDesc: string;
            table: string;
            tableDesc: string;
            msa: string;
            processCapability: string;
            comingSoon: string;
            cancel: string;
        };
        entity: {
            heading: string;
            generalHeading: string;
            titleMode: string;
            titleAuto: string;
            titleManual: string;
            titleLabel: string;
            descriptionLabel: string;
            parametersHeading: string;
            commentBody: string;
            filtersHeading: string;
            filtersHelp: string;
            addFilter: string;
            noFilters: string;
            field: string;
            operator: string;
            value: string;
            valueFrom: string;
            valueTo: string;
            valueList: string;
            valueListHelp: string;
            removeFilter: string;
            invalidFilter: string;
            back: string;
            save: string;
        };
        view: {
            heading: string;
            back: string;
            empty: string;
        };
        fields: {
            ReferenceDesignator: string;
            PartNumber: string;
            Package: string;
            Product: string;
            AoiMachine: string;
            Defect: string;
            PanelBarcode: string;
            PanelStatus: string;
        };
        operators: {
            Equal: string;
            Different: string;
            In: string;
            NotIn: string;
            Between: string;
            NotBetween: string;
            Like: string;
            NotLike: string;
            LessThanOrEqual: string;
            GreaterThanOrEqual: string;
        };
    };
    home: {
        title: string;
        intro: string;
        sourcesCard: string;
        sourcesEmpty: string;
        sourcesErrorTitle: string;
        schemaLabel: string;
        pinned: {
            heading: string;
            empty: string;
            errorTitle: string;
            errorBody: string;
            locked: string;
            tileCount_one: string;
            tileCount_other: string;
            unpinAction: string;
        };
    };
    /**
     * Shared failure vocabulary rendered by `ApiErrorAlert` /
     * `describeApiError`. The `*Title` leaves are alert headings keyed off
     * the HTTP status; the rest are messages keyed off the server's
     * `code` problem extension (see `ReportEndpoints.ProblemCodes`).
     */
    errors: {
        networkTitle: string;
        badRequestTitle: string;
        unauthorizedTitle: string;
        forbiddenTitle: string;
        notFoundTitle: string;
        serverTitle: string;
        genericTitle: string;
        network: string;
        unauthorized: string;
        forbidden: string;
        emptyWindow: string;
        invalidWindow: string;
        invalidStart: string;
        invalidEnd: string;
        missingSource: string;
        unknownSource: string;
    };
    fpyTrend: {
        title: string;
        subtitle: string;
        filters: {
            heading: string;
            from: string;
            to: string;
            bucket: string;
            granularity: string;
            flavor: string;
            cleanSkips: string;
            skipStatuses: string;
            skipStatusesPlaceholder: string;
            line: string;
            linePlaceholder: string;
            lineOption: string;
            submit: string;
            reset: string;
            print: string;
            runDisabledHint: string;
            printDisabledHint: string;
            exportDisabledHint: string;
            previewDisabledHint: string;
            exportCsv: string;
            exportXlsx: string;
            exportPdf: string;
            excludeNogo: string;
            excludeNogoHint: string;
        };
        bucket: { week: string; day: string };
        granularity: { board: string; panel: string };
        flavor: { diagnostic: string; aoi: string };
        results: {
            empty: string;
            lineCount: string;
            overallFpy: string;
            inspected: string;
        };
        chart: {
            fpy: string;
            inspected: string;
            faulty: string;
            ariaSummary: string;
        };
    };
    dpmoTrend: {
        title: string;
        subtitle: string;
        filters: {
            heading: string;
            from: string;
            to: string;
            bucket: string;
            opportunity: string;
            numerator: string;
            cleanSkips: string;
            skipStatuses: string;
            skipStatusesPlaceholder: string;
            line: string;
            linePlaceholder: string;
            lineOption: string;
            submit: string;
            reset: string;
            print: string;
            runDisabledHint: string;
            printDisabledHint: string;
            exportDisabledHint: string;
            previewDisabledHint: string;
            exportCsv: string;
            exportXlsx: string;
            exportPdf: string;
            excludeNogo: string;
            excludeNogoHint: string;
        };
        bucket: { week: string; day: string };
        opportunity: { all: string; components: string };
        numerator: { real: string; aoi: string; dummy: string };
        results: {
            empty: string;
            lineCount: string;
            overallDpmo: string;
            opportunities: string;
        };
        chart: {
            dpmo: string;
            defects: string;
            opportunities: string;
            ariaSummary: string;
        };
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
            onlyLastInspection: string;
            onlyLastInspectionHint: string;
            submit: string;
            reset: string;
            exportCsv: string;
            exportXlsx: string;
            exportPdf: string;
            emptyPrompt: string;
            missingRequired: string;
            runDisabledHint: string;
            printDisabledHint: string;
            exportDisabledHint: string;
            previewDisabledHint: string;
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
            Day: string;
            Shift: string;
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
        weight: {
            Count: string;
            Dpmo: string;
            Ppm: string;
        };
        skipExclusion: {
            Raw: string;
            Clean: string;
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
            weight: string;
            weightHint: string;
            topN: string;
            topNHint: string;
            vitalFewThreshold: string;
            vitalFewThresholdHint: string;
            machines: string;
            machinesPlaceholder: string;
            products: string;
            productsPlaceholder: string;
            skipExclusion: string;
            skipExclusionHint: string;
            skipStatuses: string;
            skipStatusesHint: string;
            skipStatusesPlaceholder: string;
            excludeNogo: string;
            excludeNogoHint: string;
            defectBitsChipsLabel: string;
            activeFiltersLabel: string;
            removeFilter: string;
            defectBitChip: string;
            removeDefectBit: string;
            submit: string;
            reset: string;
            print: string;
            runDisabledHint: string;
            printDisabledHint: string;
            exportDisabledHint: string;
            previewDisabledHint: string;
            exportCsv: string;
            exportXlsx: string;
            exportPdf: string;
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
            opportunitySharePercent: string;
            cumulativePercent: string;
            isVitalFew: string;
            notApplicable: string;
            opportunitiesUnavailable: string;
        };
        chart: {
            seriesDefects: string;
            seriesCumulative: string;
            yLeftDefects: string;
            yLeftDpmo: string;
            yLeftPpm: string;
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
                Day: string;
                Shift: string;
            };
        };
        drillMap: {
            link: string;
            intro: string;
            endLabel: string;
            notDrillable: string;
        };
    };
    skipSummary: {
        title: string;
        subtitle: string;
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
            onlyLastInspection: string;
            onlyLastInspectionHint: string;
            submit: string;
            reset: string;
            emptyPrompt: string;
            missingRequired: string;
        };
        results: {
            heading: string;
            errorTitle: string;
            source: string;
            window: string;
            totalCards: string;
            totalComponents: string;
            skippedCards: string;
            skippedCardPercent: string;
            class: string;
            cardCount: string;
            componentCount: string;
            cardPercent: string;
        };
        classLabel: {
            None: string;
            ManualSkip: string;
            MachineFlagged: string;
            HeuristicMissing: string;
        };
    };
    dpmo: {
        title: string;
        subtitle: string;
        filters: {
            heading: string;
            source: string;
            sourcePlaceholder: string;
            groupBy: string;
            from: string;
            to: string;
            numerator: string;
            opportunity: string;
            skipExclusion: string;
            skipExclusionHint: string;
            skipStatuses: string;
            skipStatusesHint: string;
            skipStatusesPlaceholder: string;
            excludeNogo: string;
            excludeNogoHint: string;
            includeObsoleteBits: string;
            includeObsoleteBitsHint: string;
            machines: string;
            machinesPlaceholder: string;
            products: string;
            productsPlaceholder: string;
            submit: string;
            reset: string;
            print: string;
            exportCsv: string;
            exportXlsx: string;
            exportPdf: string;
            emptyPrompt: string;
            missingRequired: string;
        };
        groupBy: {
            AoiMachine: string;
            Defect: string;
            Product: string;
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
        skipExclusion: {
            Raw: string;
            Clean: string;
        };
        results: {
            heading: string;
            errorTitle: string;
            source: string;
            window: string;
            overallPpm: string;
            skipExcludedCards: string;
            group: string;
            unassigned: string;
            dpmoPpm: string;
            defects: string;
            opportunities: string;
            testedObjects: string;
        };
    };
    fpy: {
        title: string;
        subtitle: string;
        filters: {
            heading: string;
            source: string;
            sourcePlaceholder: string;
            groupBy: string;
            from: string;
            to: string;
            granularity: string;
            skipExclusion: string;
            skipExclusionHint: string;
            skipStatuses: string;
            skipStatusesHint: string;
            skipStatusesPlaceholder: string;
            excludeNogo: string;
            excludeNogoHint: string;
            onlyLastInspection: string;
            onlyLastInspectionHint: string;
            machines: string;
            machinesPlaceholder: string;
            products: string;
            productsPlaceholder: string;
            submit: string;
            reset: string;
            print: string;
            exportCsv: string;
            exportXlsx: string;
            exportPdf: string;
            emptyPrompt: string;
            missingRequired: string;
        };
        granularity: {
            Panel: string;
            Board: string;
        };
        groupBy: {
            AoiMachine: string;
            Product: string;
        };
        skipExclusion: {
            Raw: string;
            Clean: string;
        };
        results: {
            heading: string;
            errorTitle: string;
            source: string;
            window: string;
            overallAoi: string;
            skipExcludedRows: string;
            group: string;
            unassigned: string;
            fpyAoi: string;
            fpyDiagnostic: string;
            fpyAfterRepair: string;
            inspected: string;
            faulty: string;
        };
    };
    filters: {
        builder: {
            emptyState: string;
            addClause: string;
            removeClause: string;
            field: string;
            operator: string;
            value: string;
            valueList: string;
            valueListPlaceholder: string;
            valueMin: string;
            valueMax: string;
            valueBoolean: string;
            valuePlaceholder: string;
            fields: {
                BoardNumber: string;
                PnpMachine: string;
                PnpSubElement1: string;
                PnpSubElement2: string;
                PnpSubElement3: string;
                PnpSubElement4: string;
                PartNumber: string;
                InspectedObject: string;
                Product: string;
                Package: string;
                RepairStatus: string;
                RepairComment: string;
                ReferenceDesignator: string;
                Defect: string;
                PanelBarcode: string;
                BoardIdCode: string;
                AoiMachine: string;
                PanelStatus: string;
                BoardStatus: string;
            };
            operators: {
                Equal: string;
                Different: string;
                In: string;
                NotIn: string;
                Between: string;
                NotBetween: string;
                Like: string;
                NotLike: string;
                LessThanOrEqual: string;
                GreaterThanOrEqual: string;
            };
            errors: {
                summaryTitle: string;
                summaryBody: string;
                operatorNotAllowed: string;
                operatorKindMismatch: string;
                aritySingle: string;
                arityRange: string;
                arityList: string;
                valueRequired: string;
                stringEmpty: string;
                integerInvalid: string;
                decimalInvalid: string;
                dateInvalid: string;
                booleanInvalid: string;
            };
        };
    };
    charts: {
        timeDecomposition: {
            label: string;
            unavailable: string;
            buckets: {
                Hour1: string;
                Hour3: string;
                Hour6: string;
                Hour12: string;
                Shift: string;
                Day: string;
                Week: string;
                Month: string;
            };
        };
    };
    canvas: {
        title: string;
        subtitle: string;
        heading: string;
        emptyPrompt: string;
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
        };
        palette: {
            heading: string;
            add: string;
        };
        tile: {
            moveUp: string;
            moveDown: string;
            remove: string;
        };
        tiles: {
            loading: string;
            errorTitle: string;
            panelYield: {
                title: string;
                emptyPrompt: string;
                noRows: string;
                chartHeading: string;
            };
            pareto: {
                title: string;
                emptyPrompt: string;
                noRows: string;
                chartHeading: string;
                totalDefects: string;
                totalOpportunities: string;
                overallDpmoPpm: string;
            };
            comment: {
                title: string;
                placeholder: string;
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
    settings: {
        timezone: {
            title: string;
            subtitle: string;
            currentLabel: string;
            autoLabel: string;
            autoDescription: string;
            selectLabel: string;
            selectPlaceholder: string;
            selectNothingFound: string;
            savedNotice: string;
            resetToAuto: string;
            previewLabel: string;
            note: string;
        };
        databases: {
            title: string;
            subtitle: string;
            forbidden: string;
            reload: string;
            loadError: string;
            addButton: string;
            emptyState: string;
            restartBanner: {
                pending: string;
                pendingReason: string;
                restartButton: string;
                restartingTitle: string;
                restartingBody: string;
                restartedTitle: string;
                restartedBody: string;
                restartFailedTitle: string;
                restartFailedBody: string;
            };
            columns: {
                key: string;
                displayName: string;
                kind: string;
                server: string;
                database: string;
                enabled: string;
                lastTested: string;
                actions: string;
            };
            enabled: string;
            disabled: string;
            never: string;
            testPass: string;
            testFail: string;
            actions: {
                edit: string;
                delete: string;
                test: string;
            };
            kinds: {
                SqlServer: string;
                Fake: string;
            };
            upsert: {
                createTitle: string;
                editTitle: string;
                keyLabel: string;
                keyPlaceholder: string;
                keyHint: string;
                keyRequired: string;
                keyImmutable: string;
                displayNameLabel: string;
                displayNamePlaceholder: string;
                displayNameRequired: string;
                kindLabel: string;
                kindPlaceholder: string;
                kindRequired: string;
                serverLabel: string;
                serverPlaceholder: string;
                serverRequiredForSql: string;
                databaseLabel: string;
                databasePlaceholder: string;
                databaseRequiredForSql: string;
                userLabel: string;
                userPlaceholder: string;
                userRequiredForSql: string;
                passwordLabel: string;
                passwordPlaceholder: string;
                passwordHintCreate: string;
                passwordHintEdit: string;
                connectTimeoutLabel: string;
                queryTimeoutLabel: string;
                trustServerCertificateLabel: string;
                encryptLabel: string;
                isEnabledLabel: string;
                isEnabledHint: string;
                testButton: string;
                testing: string;
                testSuccess: string;
                testFailure: string;
                submitCreate: string;
                submitEdit: string;
                cancel: string;
                conflict: string;
                validationFailed: string;
                unexpectedError: string;
            };
            deleteModal: {
                title: string;
                body: string;
                submit: string;
                cancel: string;
                unexpectedError: string;
            };
        };
    };
    common: {
        loading: string;
        pdfPreview: {
            title: string;
            loading: string;
            errorTitle: string;
            noSupport: string;
            download: string;
            close: string;
            openAction: string;
        };
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
        unsavedChanges: string;
        save: string;
        saveDisabledHint: string;
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
    traceability: {
        board: {
            title: string;
            subtitle: string;
            homeCardTitle: string;
            homeCardHint: string;
            barcodeLabel: string;
            barcodePlaceholder: string;
            barcodeHint: string;
            submit: string;
            emptyPrompt: string;
            barcodeRequired: string;
            barcodeTooLong: string;
            barcodeLabelResult: string;
            loading: string;
            errorTitle: string;
            notFoundTitle: string;
            notFoundBody: string;
            stageErrorTitle: string;
            stageNotFound: string;
            stageFound: string;
            panelDateLabel: string;
            panelStatusLabel: string;
            subpanelsHeading: string;
            subpanelsColCardId: string;
            subpanelsColStatus: string;
            subpanelsColObjectCount: string;
            subpanelsColErrorCount: string;
            subpanelsEmpty: string;
            productLabel: string;
            machineLabel: string;
            reviewOperatorLabel: string;
            reviewOperatorUnknown: string;
            reviewedYes: string;
            reviewedNo: string;
            reviewedLabel: string;
            /** Board trace side toggle (Face_Number 1 / 2). */
            sideLabel: string;
            side1st: string;
            side2nd: string;
            passes: {
                more_one: string;
                more_other: string;
                more: string;
                latest: string;
                viewing: string;
                selectionWarningTitle: string;
            };
            /**
             * Decodes `PANELS.Panel_Status` (Vision3D CR4 §5.1). Enum:
             * -2 = Still faulty after review (Sigmalink KO_OPERATOR),
             * -1 = Faulty after inspection, 0 = Not inspected,
             *  1 = Good after inspection,
             *  2 = Good — all defects were dummy (Sigmalink OK_OPERATOR),
             *  3 = Good after review / repaired.
             */
            panelStatus: {
                koOperator: string;
                ko: string;
                notInspected: string;
                ok: string;
                okOperator: string;
                okRepaired: string;
                /** Derived label when Panel_Status = 0 AND Anomaly_BR/AR bit 9 (256) is set. */
                skipped: string;
                unknown: string;
            };
            /**
             * Decodes `CARDS.Card_Status` (Vision3D CR4 §5.2). Same
             * enum as panels.
             */
            cardStatus: {
                koOperator: string;
                ko: string;
                notInspected: string;
                ok: string;
                okOperator: string;
                okRepaired: string;
                /** Derived label when Card_Status = 0 AND Anomaly_BR/AR bit 9 (256) is set. */
                skipped: string;
                unknown: string;
            };
            /** TC5 Phase D — drilldown section shell. */
            drilldown: {
                title: string;
                open: string;
                close: string;
                activeStageLabel: string;
                activeStagePost: string;
                activeStagePre: string;
                unavailableForStage: string;
                missingProductName: string;
            };
            /**
             * TC5 Phase D — enriched failed-objects table (per-stage,
             * one row per failing tested object). Columns per spec.
             */
            failures: {
                loading: string;
                errorTitle: string;
                empty: string;
                rowCount: string;
                stagePost: string;
                stagePre: string;
                colBoardId: string;
                colRefDes: string;
                colFace: string;
                colErrorType: string;
                colPartNumber: string;
                colDevX: string;
                colDevY: string;
                colDevTheta: string;
                colRepairResult: string;
                colRepairDate: string;
                colRepairComment: string;
                colRepairOperator: string;
                colOperatorComment: string;
                /**
                 * `TESTED_OBJECT.Repair_State_Result` decoding.
                 * Enum: -2 = Not inspected, -1 = Not detected,
                 *        0 = Pending, 1 = Repaired, 2 = False call,
                 *        3 = Confirmed faulty.
                 */
                repairState: {
                    notInspected: string;
                    notDetected: string;
                    pending: string;
                    repaired: string;
                    falseCall: string;
                    confirmed: string;
                    unknown: string;
                };
            };
        };
    };
    /**
     * TC5 Phase D — defect-bit catalogue leaf keys used by
     * `formatDefectBits`. Each key mirrors an entry in
     * `Nieweb.Reports.Common.Defects.DefectBitDecoder`.
     */
    defect: {
        bits: {
            objectMissing: string;
            polarityError: string;
            solderJointDefect: string;
            solderBridgeDefect: string;
            ocvError: string;
            modelNotFound: string;
            deltaXOutOfRange: string;
            deltaYOutOfRange: string;
            deltaThetaOutOfRange: string;
            deltaThicknessOutOfRange: string;
            pasteSurfaceAreaOutOfRange: string;
            elementSkipped: string;
            connectorBadPinColumnSpacing: string;
            connectorBadPinRowSpacing: string;
            connectorPinMissing: string;
            connectorBadPinAlignment: string;
            volumeOutOfRange: string;
            badAppearance: string;
            potentialDefectImportedFromSpi: string;
            tiltError: string;
            sideOverhang: string;
            lengthOverhang: string;
            foreignMaterialDetected: string;
            componentPresentButShouldNotBe: string;
            liftedLead: string;
        };
    };
    boardViewer: {
        heading: string;
        emptyPrompt: string;
        loading: string;
        stageLabel: string;
        stagePre: string;
        stagePost: string;
        notCachedTitle: string;
        notCachedBody: string;
        retry: string;
        badRequest: string;
        errorTitle: string;
        zoomReset: string;
        panZoomHint: string;
        crosshairToggle: string;
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
        reports: {
            title: string;
            subtitle: string;
            forbidden: string;
            loadError: string;
            reload: string;
            groups: {
                heading: string;
                createButton: string;
                emptyState: string;
                columns: {
                    name: string;
                    displayOrder: string;
                    reportCount: string;
                    actions: string;
                };
                create: {
                    title: string;
                    nameLabel: string;
                    namePlaceholder: string;
                    displayOrderLabel: string;
                    submit: string;
                    cancel: string;
                    success: string;
                    nameRequired: string;
                    conflict: string;
                    unexpectedError: string;
                };
                edit: {
                    title: string;
                    submit: string;
                    success: string;
                };
                delete: {
                    confirmTitle: string;
                    confirmBody: string;
                    submit: string;
                    cancel: string;
                    success: string;
                    unexpectedError: string;
                };
                unassigned: string;
            };
            list: {
                heading: string;
                createButton: string;
                emptyState: string;
                columns: {
                    title: string;
                    group: string;
                    owner: string;
                    tiles: string;
                    lastModified: string;
                    actions: string;
                };
                actions: {
                    edit: string;
                    delete: string;
                    duplicate: string;
                    pin: string;
                    unpin: string;
                };
                create: {
                    title: string;
                    titleLabel: string;
                    titlePlaceholder: string;
                    descriptionLabel: string;
                    descriptionPlaceholder: string;
                    groupLabel: string;
                    groupPlaceholder: string;
                    ownerLabel: string;
                    ownerPlaceholder: string;
                    submit: string;
                    cancel: string;
                    success: string;
                    titleRequired: string;
                    ownerRequired: string;
                    unexpectedError: string;
                    template: {
                        label: string;
                        blank: { name: string; desc: string };
                        yieldOverview: { name: string; desc: string };
                        topDefects: { name: string; desc: string };
                        yieldAndDefects: { name: string; desc: string };
                        defectsByMachine: { name: string; desc: string };
                    };
                };
                delete: {
                    confirmTitle: string;
                    confirmBody: string;
                    submit: string;
                    cancel: string;
                    success: string;
                    unexpectedError: string;
                };
                duplicate: {
                    title: string;
                    titleField: string;
                    owner: string;
                    submit: string;
                    cancel: string;
                    ownerRequired: string;
                    unexpectedError: string;
                };
            };
            editor: {
                backLink: string;
                loadError: string;
                notFound: string;
                header: {
                    heading: string;
                    titleLabel: string;
                    descriptionLabel: string;
                    groupLabel: string;
                    groupPlaceholder: string;
                    refreshLabel: string;
                    refreshHint: string;
                    displayOrderLabel: string;
                    isLockedLabel: string;
                    isPinnedHomeLabel: string;
                    defaultSourceLabel: string;
                    defaultSourceHint: string;
                    defaultSourcePlaceholder: string;
                    defaultWindowLabel: string;
                    defaultWindowHint: string;
                    defaultWindowPlaceholder: string;
                    windowPreset: {
                        today: string;
                        yesterday: string;
                        last7d: string;
                        last30d: string;
                    };
                    submit: string;
                    saving: string;
                    saved: string;
                    unexpectedError: string;
                };
                lock: {
                    heading: string;
                    statusLocked: string;
                    statusUnlocked: string;
                    hintLocked: string;
                    hintUnlocked: string;
                    lockButton: string;
                    unlockButton: string;
                    duplicateButton: string;
                    lockTitle: string;
                    unlockTitle: string;
                    duplicateTitle: string;
                    lockBody: string;
                    unlockBody: string;
                    passwordLabel: string;
                    passwordRequired: string;
                    wrongPassword: string;
                    lockSubmit: string;
                    unlockSubmit: string;
                    duplicateSubmit: string;
                    duplicateTitleField: string;
                    duplicateOwner: string;
                    ownerRequired: string;
                    cancel: string;
                    unexpectedError: string;
                };
                tiles: {
                    heading: string;
                    subtitle: string;
                    emptyState: string;
                    add: string;
                    addMenuHeading: string;
                    unknownType: string;
                    moveUp: string;
                    moveDown: string;
                    remove: string;
                    tileTypeLabel: string;
                    titleLabel: string;
                    titlePlaceholder: string;
                    configLabel: string;
                    configHint: string;
                    configInvalid: string;
                    commentLabel: string;
                    commentHint: string;
                    commentPlaceholder: string;
                    advancedLabel: string;
                    config: {
                        panelYield: {
                            onlyLastInspection: {
                                label: string;
                                help: string;
                            };
                        };
                        pareto: {
                            axis: {
                                label: string;
                                help: string;
                                options: {
                                    Defect: string;
                                    Product: string;
                                    AoiMachine: string;
                                    ReferenceDesignator: string;
                                    PartNumber: string;
                                    Jedec: string;
                                    Day: string;
                                    Shift: string;
                                };
                            };
                            numerator: {
                                label: string;
                                help: string;
                                options: {
                                    Aoi: string;
                                    Real: string;
                                    Dummy: string;
                                };
                            };
                            opportunity: {
                                label: string;
                                help: string;
                                options: {
                                    All: string;
                                    Components: string;
                                    Paste: string;
                                };
                            };
                            weight: {
                                label: string;
                                help: string;
                                options: {
                                    Count: string;
                                    Dpmo: string;
                                    Ppm: string;
                                };
                            };
                            topN: {
                                label: string;
                                help: string;
                                placeholder: string;
                            };
                        };
                    };
                    save: string;
                    saved: string;
                    saveError: string;
                };
                export: {
                    heading: string;
                    description: string;
                    sourceLabel: string;
                    sourcePlaceholder: string;
                    startLabel: string;
                    endLabel: string;
                    downloadXlsx: string;
                    downloadPdf: string;
                    downloadCsv: string;
                    errorPrefix: string;
                };
            };
        };
        boardSvgs: {
            title: string;
            subtitle: string;
            forbidden: string;
            reload: string;
            loadError: string;
            syncNow: string;
            syncRunning: string;
            syncSuccess: string;
            syncError: string;
            status: {
                heading: string;
                cacheDirectory: string;
                cacheDirectoryMissing: string;
                intervalSeconds: string;
                syncEnabled: string;
                syncDisabled: string;
                cachedFiles: string;
                cachedFilesEmpty: string;
                missingProducts: string;
                missingProductsEmpty: string;
                knownProducts: string;
                columns: {
                    product: string;
                    file: string;
                    size: string;
                    lastWrite: string;
                };
            };
            sources: {
                heading: string;
                addButton: string;
                emptyState: string;
                columns: {
                    machineName: string;
                    uncPath: string;
                    enabled: string;
                    lastSynced: string;
                    lastError: string;
                    actions: string;
                };
                enabled: string;
                disabled: string;
                never: string;
                actions: {
                    edit: string;
                    delete: string;
                };
                create: {
                    title: string;
                    machineNameLabel: string;
                    machineNamePlaceholder: string;
                    uncPathLabel: string;
                    uncPathPlaceholder: string;
                    isEnabledLabel: string;
                    isEnabledHint: string;
                    submit: string;
                    cancel: string;
                    success: string;
                    machineNameRequired: string;
                    uncPathRequired: string;
                    conflict: string;
                    validationFailed: string;
                    unexpectedError: string;
                };
                edit: {
                    title: string;
                    submit: string;
                    success: string;
                    conflict: string;
                    validationFailed: string;
                    unexpectedError: string;
                };
                delete: {
                    confirmTitle: string;
                    confirmBody: string;
                    submit: string;
                    cancel: string;
                    success: string;
                    unexpectedError: string;
                };
            };
            syncResult: {
                title: string;
                startedAt: string;
                completedAt: string;
                sourcesHeading: string;
                productsHeading: string;
                close: string;
                copied: string;
                alreadyCached: string;
                error: string;
                reachable: string;
                unreachable: string;
                filesEnumerated: string;
                columns: {
                    machineName: string;
                    reachable: string;
                    files: string;
                    error: string;
                    product: string;
                    outcome: string;
                    machineNameProduct: string;
                    bytes: string;
                };
                empty: string;
            };
        };
        parameters: {
            title: string;
            subtitle: string;
            forbidden: string;
            reload: string;
            createButton: string;
            loadError: string;
            emptyState: string;
            system: string;
            custom: string;
            columns: {
                key: string;
                valueType: string;
                value: string;
                description: string;
                system: string;
                lastModified: string;
                actions: string;
            };
            actions: {
                edit: string;
                delete: string;
            };
            valueTypes: {
                decimal: string;
                int: string;
                bool: string;
                string: string;
            };
            upsert: {
                createTitle: string;
                editTitle: string;
                keyLabel: string;
                keyPlaceholder: string;
                keyRequired: string;
                valueTypeLabel: string;
                valueLabel: string;
                valueRequired: string;
                descriptionLabel: string;
                descriptionPlaceholder: string;
                submit: string;
                cancel: string;
                validationFailed: string;
                unexpectedError: string;
            };
            delete: {
                title: string;
                confirm: string;
                submit: string;
                cancel: string;
                systemProtected: string;
                unexpectedError: string;
            };
        };
        productionLines: {
            title: string;
            subtitle: string;
            forbidden: string;
            reload: string;
            createButton: string;
            loadError: string;
            emptyState: string;
            columns: {
                name: string;
                displayOrder: string;
                machineCount: string;
                actions: string;
            };
            actions: {
                edit: string;
                delete: string;
                expand: string;
                collapse: string;
            };
            line: {
                nameLabel: string;
                namePlaceholder: string;
                nameRequired: string;
                displayOrderLabel: string;
                submit: string;
                cancel: string;
                create: {
                    title: string;
                    conflict: string;
                    validationFailed: string;
                    unexpectedError: string;
                };
                edit: {
                    title: string;
                    conflict: string;
                    validationFailed: string;
                    unexpectedError: string;
                };
                delete: {
                    title: string;
                    confirm: string;
                    submit: string;
                    conflict: string;
                    validationFailed: string;
                    unexpectedError: string;
                };
            };
            machine: {
                heading: string;
                addButton: string;
                empty: string;
                loadError: string;
                columns: {
                    source: string;
                    machineId: string;
                    name: string;
                    category: string;
                    actions: string;
                };
                actions: {
                    remove: string;
                };
                add: {
                    title: string;
                    sourceLabel: string;
                    sourceRequired: string;
                    machinePickLabel: string;
                    machinePickPlaceholder: string;
                    machineIdLabel: string;
                    machineIdPlaceholder: string;
                    machineIdRequired: string;
                    nameLabel: string;
                    nameRequired: string;
                    categoryLabel: string;
                    categoryPlaceholder: string;
                    displayOrderLabel: string;
                    submit: string;
                    cancel: string;
                    conflict: string;
                    validationFailed: string;
                    unexpectedError: string;
                };
                remove: {
                    conflict: string;
                    validationFailed: string;
                    unexpectedError: string;
                };
            };
        };
        shifts: {
            title: string;
            subtitle: string;
            forbidden: string;
            reload: string;
            addRow: string;
            loadError: string;
            emptyState: string;
            labelPlaceholder: string;
            columns: {
                hour: string;
                minute: string;
                label: string;
                actions: string;
            };
            actions: {
                remove: string;
            };
            save: {
                submit: string;
                success: string;
                validationFailed: string;
                unexpectedError: string;
            };
        };
        skipClassification: {
            title: string;
            subtitle: string;
            forbidden: string;
            reload: string;
            loadError: string;
            thresholds: {
                heading: string;
                hint: string;
                missingRatio: string;
                missingRatioHint: string;
                minComponentFloor: string;
                minComponentFloorHint: string;
                absoluteMissingFloor: string;
                absoluteMissingFloorHint: string;
            };
            buttons: {
                heading: string;
                hint: string;
                add: string;
                remove: string;
                empty: string;
                label: string;
                meaning: string;
            };
            meaning: {
                Normal: string;
                ManualSkip: string;
                FalseCall: string;
                ConfirmedRealMissing: string;
            };
            save: {
                submit: string;
                success: string;
                validationFailed: string;
                unexpectedError: string;
            };
        };
    };
};