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
        canvasDemo: string;
        boardTrace: string;
        adminUsers: string;
        adminAudit: string;
        adminReports: string;
        adminBoardSvgs: string;
        adminParameters: string;
        adminProductionLines: string;
        adminShifts: string;
        settings: string;
        settingsTimezone: string;
        settingsDatabases: string;
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
            defectBitsChipsLabel: string;
            defectBitChip: string;
            removeDefectBit: string;
            submit: string;
            reset: string;
            print: string;
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
                Day: string;
                Shift: string;
            };
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
    };
};