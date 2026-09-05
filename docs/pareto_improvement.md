# Nieweb Pareto Improvement Build Plan

## 1\. Purpose

This document defines the implementation plan for correcting and refining the Nieweb Pareto feature across its domain model, API, standalone UI, canvas tile, exports, charts, localization, and automated tests.

The plan prioritizes two user-trust risks:

1. The canvas tile and its saved-report export can apply different filters and therefore return different results.
2. Object-level axes encode an unavailable opportunity denominator as numeric zero, causing the UI and exports to present a misleading DPMO of zero beside nonzero defects.

The plan also resolves chart-versus-ranking ambiguity, inconsistent defaults, divergent export schemas, misleading opportunity-share behavior, TypeScript contract drift, and missing regression coverage.

## 2\. Baseline and implementation constraints

### 2.1 Reviewed baseline

The review package describes the working tree on branch `backup/phase-c-2026-09-05` at commit `2d0f94a2e755728ebfc119d70aa67a8af08087b1`. The working tree was dirty and contained uncommitted Pareto-related changes. Before implementation, create a clean baseline commit or record a patch so all changes introduced by this plan remain attributable and reviewable.

### 2.2 Existing architecture to preserve

The current architecture should remain intact unless a work package explicitly changes it:

* `ParetoReport.RunAsync` remains the source of Pareto aggregation and ranking.
* Overall opportunities remain based on `CARDS` test counts.
* Defect count remains the count of selected defect bits 1 through 25.
* Count remains the default Pareto ranking mode.
* The crossing category remains included in the vital-few set.
* Dedicated JSON, CSV, Excel, and PDF paths continue to share `ParetoResult`.
* Existing authentication and source-resolution behavior remain unchanged.
* Existing volume-weighted Product A before Product B behavior remains protected.

### 2.3 Non-goals

The first implementation does not need to invent true per-reference-designator, per-part-number, or per-JEDEC opportunity denominators. Those require a separate data-source enhancement, likely using placement or library data. The immediate goal is to represent the current metric honestly.

This plan also does not change the intentional rule that one tested object with multiple selected defect bits contributes multiple defect occurrences.

## 3\. Target behavior

After implementation:

* A canvas Pareto tile and every export of that tile apply identical filters.
* Reference Designator, Part Number, and JEDEC rows clearly show that opportunities and DPMO are unavailable, not zero.
* A true zero denominator remains distinguishable from an unavailable denominator.
* Rate-weighted Pareto charts communicate the same metric used to rank rows.
* Standalone and tile defaults are defined centrally and behave consistently.
* Dedicated and saved-report exports use a documented common semantic schema.
* Defect-axis opportunity share is not presented as though every row owns 100 percent of production opportunities.
* TypeScript types accurately model all server-supported Pareto weights and metric availability.
* Regression tests cover the corrected contracts and all cross-surface parity risks.

## 4\. Delivery strategy

Implement the work in six ordered work packages. Each package should leave the repository buildable and testable.

1. Characterization and safety-net tests
2. Metric-availability contract and presentation
3. Canvas screen/export filter parity
4. Ranking and chart semantic alignment
5. Defaults and export schema consolidation
6. Documentation, cleanup, validation, and release readiness

Do not combine all changes into one large commit. Preserve reviewability by committing each completed work package separately.

\---

# Work Package 1: Characterization and Safety-Net Tests

## Objective

Lock down current intentional behavior and reproduce each confirmed defect before changing production logic.

## Files likely to change

* `tests/Nieweb.Reports.Tests/ParetoReportTests.cs`
* `tests/Nieweb.Api.Tests/Endpoints/DpmoAndParetoEndpointsTests.cs`
* `tests/Nieweb.Api.Tests/Endpoints/ReportExportEndpointTests.cs`
* `tests/Nieweb.Api.Tests/Pdf/ParetoChartSvgTests.cs`
* `src/Nieweb.Web/src/routes/pareto.search.test.ts`
* `src/Nieweb.Web/src/charts/ParetoChart.test.tsx`
* New Pareto tile test near `src/Nieweb.Web/src/components/canvas/tiles/ParetoTile.tsx`
* `src/Nieweb.Web/e2e/pareto.spec.ts`

## Build tasks

### 1.1 Add object-level denominator characterization

Create report-level tests for all three unsupported row-denominator axes:

* `ReferenceDesignator`
* `PartNumber`
* `Jedec`

Seed nonzero card opportunities and at least one defective tested object. Before the contract change, prove that overall opportunities are positive while affected rows have zero opportunities and zero DPMO. This test should initially document the current behavior. It will be updated in Work Package 2 to assert explicit unavailability.

Suggested test names:

```text
ReferenceDesignatorAxis\_RowDenominatorIsUnavailable
PartNumberAxis\_RowDenominatorIsUnavailable
JedecAxis\_RowDenominatorIsUnavailable
```

### 1.2 Add a true-zero card-denominator test

Add a Product-axis fixture in which the group is supported by the denominator model but its card test count is actually zero. This ensures the new contract can distinguish:

* Applicable metric with denominator zero
* Inapplicable metric with no denominator model

Suggested test:

```text
ProductAxis\_ZeroCardTests\_RemainsApplicableWithZeroDenominator
```

### 1.3 Add multi-bit Pareto integration coverage

Seed one tested object with two selected defect bits. Assert:

* `TestedObjectCount` increases by one.
* Overall `DefectBitCount` increases by two.
* A non-Defect axis receives a defect count of two.
* The Defect axis receives one occurrence in each corresponding defect group.
* Cumulative percentages use the two total occurrences.

Suggested test:

```text
OneObjectWithTwoBits\_AddsTwoDefectOccurrences
```

### 1.4 Add deterministic tie coverage

Create equal scores and assert ordinal `GroupKey` ordering. Cover both Count and DPMO ranking if the same comparator is used.

```text
TiedWeightedScore\_SortsByOrdinalGroupKey
```

### 1.5 Add first-category-crosses-threshold coverage

Seed a first category above the configured vital-few threshold. Assert that the first row is vital-few and subsequent rows are not.

```text
VitalFew\_FirstCategoryExceedsThreshold\_IncludesOnlyFirstCategory
```

### 1.6 Reproduce canvas/export filter divergence

Create a tile configuration containing a nonempty generic `filters` array. Assert the current on-screen request omits the filters while saved-report export applies them. This test is expected to demonstrate the defect and will become a parity test in Work Package 3.

### 1.7 Protect existing invariants

Retain and run all existing tests for:

* Count-based volume ranking
* DPMO ranking reversal
* Top N and Other
* Inclusive 80 percent threshold
* Authentication
* Invalid filters
* CSV, Excel, PDF, and SVG generation
* Day and Shift grouping
* Skip and NOGO exclusion

## Acceptance criteria

* Each confirmed issue has an automated reproduction or characterization test.
* Existing Pareto tests continue to pass.
* No production behavior changes in this package.
* Test fixtures clearly distinguish card opportunities, tested objects, and defect occurrences.

\---

# Work Package 2: Metric Availability Contract and Presentation

## Objective

Stop representing an unavailable row denominator and unavailable row DPMO as measured numeric zero.

## Design decision

Use an additive compatibility field first. Preserve existing numeric fields during migration, but require all updated Nieweb clients and exports to use the explicit applicability field.

Recommended server contract:

```csharp
public sealed record ParetoRow(
    string? GroupKey,
    string? GroupName,
    long DefectCount,
    double WeightedScore,
    long OpportunityCount,
    double OpportunitySharePercent,
    double DpmoPpm,
    bool OpportunitiesApplicable,
    double DefectSharePercent,
    double CumulativePercent,
    bool IsVitalFew);
```

A stronger future contract may make `OpportunityCount`, `OpportunitySharePercent`, and `DpmoPpm` nullable. Do not make that breaking change until all consumers are identified or the API is versioned.

## Semantic rules

|Axis|OpportunitiesApplicable|Row denominator meaning|
|-|-:|-|
|AoiMachine|true|Card opportunities for machine|
|Product|true|Card opportunities for product|
|Day|true|Card opportunities for bucket|
|Shift|true|Card opportunities for bucket|
|Defect|true for DPMO|Shared overall denominator|
|ReferenceDesignator|false|Per-group denominator unavailable|
|PartNumber|false|Per-group denominator unavailable|
|Jedec|false|Per-group denominator unavailable|

For Defect axis, DPMO remains applicable as the rate of that defect type across the full sample. Opportunity share must not be treated as a partition metric and will be addressed separately below.

## Files likely to change

* `src/Nieweb.Reports/ParetoReportDtos.cs`
* `src/Nieweb.Reports/ParetoReport.cs`
* `src/Nieweb.Web/src/api/pareto.ts`
* `src/Nieweb.Web/src/routes/pareto.tsx`
* `src/Nieweb.Web/src/charts/ParetoChart.tsx`
* `src/Nieweb.Web/src/i18n/locales/en.ts`
* `src/Nieweb.Web/src/i18n/locales/fr.ts`
* `src/Nieweb.Api/Endpoints/ReportEndpoints.Pareto.cs`
* `src/Nieweb.Api/Endpoints/ReportEndpoints.ReportExport.cs`
* `src/Nieweb.Pdf/ParetoPdfRenderer.cs`
* `src/Nieweb.Pdf/ReportPdfRenderer.cs`
* Corresponding test files

## Build tasks

### 2.1 Add applicability to the domain result

Update `ParetoRow` and the row-building path. Replace implicit axis inference with a named helper such as:

```csharp
private static bool OpportunitiesApplicableForAxis(ParetoAxis axis)
```

`OpportunityForGroup` should no longer be the only source of meaning. A returned `0` must mean a numeric denominator of zero only when applicability is true.

### 2.2 Preserve ranking safety

For DPMO or PPM weight:

* Reject or normalize rate weighting on axes where opportunities are inapplicable.
* Preserve the existing UI behavior that forces Count on object-level drill-downs.
* Add server-side enforcement so non-UI callers cannot request a meaningless rate ranking.

Preferred behavior is HTTP 400 with a clear problem title for unsupported axis/weight combinations. If backward compatibility requires normalization, echo the applied weight and document the normalization. Do not silently sort all rows by zero.

### 2.3 Update JSON and TypeScript contracts

Add `opportunitiesApplicable: boolean` to the TypeScript `ParetoRow` type. At the same time, correct the existing weight type from only `"Count"` to:

```ts
type ParetoWeight = "Count" | "Dpmo" | "Ppm";
```

Add a formatting helper shared by the table and tooltip:

```ts
formatApplicableMetric(value, applicable, formatter)
```

This prevents each UI surface from independently deciding how to represent unavailable metrics.

### 2.4 Update standalone UI

In `ParetoTable`:

* Render an em dash or localized `N/A` when opportunities are inapplicable.
* Do not render DPMO zero in that state.
* Add an accessible tooltip or helper text explaining that the selected grouping axis does not have a per-group opportunity denominator.

In the KPI area, keep overall opportunities and overall DPMO unchanged.

### 2.5 Update chart tooltip and accessibility

In `ParetoChart.tsx`:

* Suppress numeric row opportunities and DPMO when inapplicable.
* Display localized explanatory text instead.
* Include availability in the chart's accessible description.
* Ensure that the table remains a complete accessible representation of the chart data.

### 2.6 Update dedicated exports

CSV:

* Add `OpportunitiesApplicable`.
* When false, emit blank `OpportunityCount`, `OpportunitySharePercent`, and `DpmoPpm` cells, or retain zeros only in legacy columns while adding explicit applicability. Prefer blank cells for the new schema.

Excel:

* Add an applicability column.
* Use blank or text `N/A` cells for inapplicable row metrics.
* Keep numeric cell types for applicable values.

PDF:

* Display `—` or localized `N/A`.
* Add a concise footnote explaining unavailable row denominators.

### 2.7 Update saved-report exports

Do not allow saved-report exports to show a DPMO of zero without also communicating applicability. Apply the same semantic contract even if the compact export intentionally omits opportunity counts.

### 2.8 Correct Defect-axis opportunity share

Because every Defect row uses the shared overall denominator, `OpportunitySharePercent` becomes 100 percent for every row. Treat opportunity share as not applicable for the Defect axis even though DPMO itself remains applicable.

If a separate field is needed, introduce:

```csharp
bool OpportunityShareApplicable
```

Alternatively, stop emitting or displaying opportunity share for Defect rows. Do not overload `OpportunitiesApplicable`, because Defect-row DPMO remains meaningful.

## Acceptance criteria

* Object-level rows no longer display measured zero DPMO when the denominator is unavailable.
* API consumers can distinguish unavailable from true zero.
* Rate weighting is not permitted where row denominators are unavailable.
* Defect-axis DPMO remains calculated using the overall denominator.
* Defect-axis opportunity share is hidden or explicitly marked not applicable.
* JSON, UI, CSV, Excel, and PDF communicate the same availability semantics.
* Overall KPI values remain unchanged.

\---

# Work Package 3: Canvas Screen and Export Filter Parity

## Objective

Ensure that the on-screen Pareto tile and saved-report exports calculate the same result from the same saved tile configuration.

## Preferred design

Use one server-side request contract for tile execution. Avoid duplicating a complex generic filter encoding in a GET query string.

Preferred options, in order:

1. Add a POST execution endpoint accepting the existing Pareto filter DTO, including `FilterRequest`.
2. Add a dedicated tile-preview endpoint accepting the saved tile configuration.
3. If GET must remain, define and test a stable encoded filter parameter.

The standalone GET endpoint may remain for bookmarked and simple queries.

## Files likely to change

* `src/Nieweb.Web/src/components/canvas/tiles/ParetoTile.tsx`
* `src/Nieweb.Web/src/api/pareto.ts`
* `src/Nieweb.Web/src/components/reportConfig/tileConfig.ts`
* `src/Nieweb.Api/Endpoints/ReportEndpoints.cs`
* `src/Nieweb.Api/Endpoints/ReportEndpoints.Pareto.cs`
* `src/Nieweb.Api/Endpoints/ReportEndpoints.ReportExport.cs`
* `src/Nieweb.Api/Endpoints/ReportEndpoints.TileConfig.cs`
* API and UI tests

## Build tasks

### 3.1 Define a canonical tile execution request

Create or reuse a request DTO containing:

* Source and time window
* Axis
* Numerator
* Opportunity
* Weight
* Top N
* Include Other
* Vital-few threshold
* Skip and NOGO settings
* Drill filters
* Generic `FilterRequest`

### 3.2 Share tile-config parsing

Move duplicated client/server defaults and normalization rules behind one clearly documented contract. If code cannot be shared across C# and TypeScript, generate the client type or add contract tests using fixed JSON fixtures.

### 3.3 Update on-screen tile request

`ParetoTile.tsx` must send the same generic filters used by `RunParetoForTileAsync`. The React Query key must include a stable representation of the filters so changing a filter cannot reuse stale data.

### 3.4 Reuse execution logic in saved exports

The saved-report export path should invoke the same request-to-filter builder as the on-screen tile endpoint. Avoid separate interpretation rules for Day/Shift or defaults.

### 3.5 Add parity tests

For a saved tile with generic filters, run both paths and compare:

* Overall tested-object count
* Overall opportunities
* Overall defect count
* Row keys
* Row order
* Defect counts
* DPMO or applicability
* Cumulative percentage
* Other bucket

Add at least one filter that materially excludes data so a dropped filter cannot accidentally pass.

## Acceptance criteria

* The same tile configuration produces equivalent screen and export results.
* React Query invalidates when tile filters change.
* No filters are applied only during export.
* Existing standalone URL-driven Pareto behavior remains available.
* API validation returns a user-readable 400 response for malformed generic filters.

\---

# Work Package 4: Ranking and Chart Semantic Alignment

## Objective

Make chart geometry and labeling accurately communicate the selected ranking mode.

## Product decision

Recommended behavior:

* Count weight: bars plot `DefectCount`; left axis label is `Defects`.
* DPMO or PPM weight: bars plot `WeightedScore`; left axis label is `DPMO` or `PPM`.
* Cumulative line always represents cumulative defect contribution, not cumulative rate.

Because a cumulative-defect line paired with rate bars mixes metrics, add clear legend and tooltip wording. If that combination proves too confusing, disable cumulative contribution for rate-weighted mode or label it explicitly as `Cumulative defect contribution`.

## Files likely to change

* `src/Nieweb.Web/src/charts/ParetoChart.tsx`
* `src/Nieweb.Web/src/charts/ParetoChart.test.tsx`
* `src/Nieweb.Pdf/ParetoChartSvg.cs`
* `tests/Nieweb.Api.Tests/Pdf/ParetoChartSvgTests.cs`
* `src/Nieweb.Pdf/ParetoPdfRenderer.cs`
* English and French localization files

## Build tasks

### 4.1 Add a shared presentation decision

Define a small function that maps weight to:

* Bar value selector
* Axis title
* Value formatter
* Tooltip label
* Export caption

### 4.2 Update SPA chart

Use `weightedScore` for DPMO/PPM modes. Preserve row order from the server. Do not re-sort client-side.

### 4.3 Update PDF SVG

Mirror the SPA rule exactly. The dedicated PDF must not display count-height bars when its title says the Pareto is weighted by DPMO.

### 4.4 Add visual-semantic tests

Test that:

* Count mode uses `defectCount`.
* DPMO mode uses `weightedScore`.
* Axis labels change appropriately.
* Tooltips identify the ranking metric and cumulative metric.
* SVG uses the same values as the SPA for a shared fixture.

## Acceptance criteria

* The first-ranked category corresponds to the chart's selected bar metric.
* SPA and PDF charts use the same semantic rule.
* The cumulative line is labeled as cumulative defect contribution.
* Count-mode appearance and behavior remain backward compatible.

\---

# Work Package 5: Defaults and Export Schema Consolidation

## Objective

Remove silent differences among standalone, tile, dedicated export, saved-report export, and client CSV behavior.

## Files likely to change

* `src/Nieweb.Web/src/routes/pareto.tsx`
* `src/Nieweb.Web/src/components/reportConfig/tileConfig.ts`
* `src/Nieweb.Api/Endpoints/ReportEndpoints.TileConfig.cs`
* `src/Nieweb.Api/Endpoints/ReportEndpoints.Pareto.cs`
* `src/Nieweb.Api/Endpoints/ReportEndpoints.ReportExport.cs`
* `src/Nieweb.Pdf/ReportPdfRenderer.cs`
* `src/Nieweb.Web/src/components/csvExport.ts`
* Tests for defaults and exports

## Build tasks

### 5.1 Define canonical defaults

Create a documented default set:

```text
Axis: Defect
Numerator: Real
Opportunity: Components or All, selected explicitly as a product decision
Weight: Count
Vital-few threshold: 80
Include Other: true
Top N: explicitly defined by surface or explicitly absent
```

If tile compactness requires Top N 10 while standalone has no cap, label that as a deliberate presentation default rather than pretending the configurations are identical.

For opportunity flavor, select one canonical default. Because changing empty saved tile configs may alter historical output, migrate or materialize defaults into existing saved configurations before changing fallback interpretation.

### 5.2 Build a canonical row export model

Create one export projection that all export writers can use. It should include:

* Rank
* Group key and name
* Defect count
* Ranking metric and weighted score
* Opportunities applicability
* Opportunity count when applicable
* DPMO applicability and DPMO when applicable
* Defect share
* Cumulative defect contribution
* Vital-few status
* Other-row status

### 5.3 Standardize metadata

Every dedicated and saved-report export should identify:

* Source
* Time window
* Axis
* Numerator
* Opportunity flavor
* Weight
* Top N
* Other behavior
* Vital-few threshold
* Applied filters

### 5.4 Rationalize client-side CSV

Choose one of these:

* Remove the client-side CSV action in favor of the authenticated dedicated server export, or
* Rename it to `Export visible table` and document that it contains only displayed columns.

Do not present two differently shaped files as if they are the same export.

### 5.5 Add schema parity tests

Tests should compare semantic fields across dedicated and saved-report output. File layout may differ, but values and applicability must match.

## Acceptance criteria

* Defaults are documented and consistently interpreted.
* Existing saved tiles are not silently reinterpreted.
* Export variants communicate the same metric meanings.
* Client-visible export actions have distinct, accurate names.
* Dedicated and saved-report exports pass parity assertions.

\---

# Work Package 6: Documentation, Cleanup, Validation, and Release Readiness

## Objective

Finish the implementation with accurate documentation, maintainable contracts, and complete validation.

## Build tasks

### 6.1 Correct stale comments

Update XML documentation that describes opportunities as tested-object rows. It must state that Pareto opportunities come from card test counts through `NbOfTestsOnComp` and `NbOfTestsOnPads`.

### 6.2 Document supported metric combinations

Document which axes support row-level opportunities and rate weighting. Include API behavior for unsupported combinations.

### 6.3 Investigate obsolete-bit cumulative mismatch

Add a fixture using an obsolete defect bit and determine whether `defectsOverall` includes occurrences omitted from Defect-axis rows. If confirmed, ensure the denominator used for defect share and cumulative percentage matches the set of emitted rows when obsolete bits are excluded.

### 6.4 Verify CSV Shift error handling

Exercise Shift axis without a shift definition through CSV, Excel, PDF, and JSON endpoints. Ensure all surfaces return an equivalent 400 response rather than an unexpected 500.

### 6.5 Add large-count and precision tests

Test:

* Large `long` opportunity totals
* No integer overflow during addition
* Double calculation for DPMO
* Cumulative display ending at an appropriate 100 percent representation
* Other-bucket reconciliation

Use synthetic boundary values that remain valid for the source DTOs.

### 6.6 Add localization tests

Verify English and French text for:

* Unavailable metrics
* DPMO/PPM chart labels
* Cumulative defect contribution
* Export footnotes
* Validation errors where localized by the SPA

### 6.7 Run full validation

Run at minimum:

* .NET build
* .NET report tests
* .NET API integration tests
* UI type checking
* UI unit tests
* Playwright Pareto scenario
* Export tests for CSV, Excel, PDF, and SVG

If the repository has formatting, lint, snapshot, or architecture checks, include them in the same validation run.

### 6.8 Regenerate the AI review package

After implementation and commit:

* Regenerate `AI-REVIEW-PARETO` from a clean working tree.
* Record the final branch and commit.
* Confirm that resolved findings are marked resolved.
* Keep any remaining limitations explicit.

## Acceptance criteria

* Documentation matches implementation.
* No stale contract descriptions remain.
* All supported output paths pass automated tests.
* The implementation is reviewed from a clean, identifiable commit.
* No secrets or generated directories are introduced.

\---

# 5\. Detailed Acceptance Test Matrix

## Domain

* Count weighting ranks by absolute defect occurrence count.
* DPMO weighting ranks only axes with applicable row denominators.
* One object with multiple bits contributes one occurrence per selected bit.
* Overall opportunities continue to use card test counts.
* Object-level axes return explicit unavailability.
* True-zero supported denominators remain applicable.
* Vital-few includes the crossing row.
* Top N and Other reconcile with the full defect total.
* Ties are resolved by ordinal group key.

## API

* JSON exposes metric applicability.
* Unsupported rate weighting returns a documented response.
* Generic tile filters reach the report engine.
* Malformed filters return 400.
* Authentication behavior remains unchanged.
* JSON and export execution use equivalent filters.

## Standalone UI

* Overall KPIs remain visible for object-level axes.
* Row opportunity and DPMO cells show `N/A` or `—` when unavailable.
* A true numeric zero is still shown as zero.
* Tooltips explain availability.
* Weight selection and axis selection cannot form a meaningless combination.

## Canvas tile

* Saved filters affect on-screen data.
* Query keys include filters.
* Screen and export results match.
* Defaults match the documented canonical behavior.

## Charts

* Count mode plots defect count.
* Rate mode plots weighted score.
* Axis and tooltip labels match the plotted metric.
* Cumulative line is identified as cumulative defect contribution.
* SPA and PDF SVG agree.

## Exports

* CSV, Excel, and PDF distinguish unavailable from zero.
* Saved-report and dedicated exports preserve the same semantics.
* Metadata identifies applied configuration.
* Compact exports do not silently omit the meaning of DPMO.

## Accessibility

* The data table contains all chart information.
* Chart accessible text includes axis, weight, and availability state.
* Unavailable values are not announced as zero.
* Any chart drill action has a keyboard-accessible equivalent through the table or controls.

\---

# 6\. Compatibility and Migration Plan

## Phase A: Additive contract

Add applicability fields while retaining existing numeric fields. Update all first-party clients immediately.

## Phase B: Saved configuration normalization

Materialize missing opportunity/default values in existing saved Pareto tile configurations. This prevents fallback changes from altering historical reports.

## Phase C: Export schema transition

Add new columns without immediately removing legacy columns. Mark old ambiguous behavior as deprecated in release notes.

## Phase D: Optional nullable contract

After all known consumers use applicability, consider a versioned API contract where unavailable numeric values are `null`. Do not perform this as an unversioned breaking change.

\---

# 7\. Pull Request Structure

Recommended pull request sequence:

1. `test(pareto): characterize metric availability and parity gaps`
2. `fix(pareto): expose row metric applicability`
3. `fix(pareto): align canvas tile filters with exports`
4. `fix(pareto): align chart values with ranking weight`
5. `refactor(pareto): unify defaults and export projections`
6. `docs(pareto): correct opportunity semantics and close review findings`

Each pull request should include:

* Finding IDs addressed
* Before/after behavior
* API compatibility impact
* Screenshots or export samples when presentation changes
* Tests added
* Explicit statement of remaining known limitations

\---

# 8\. Definition of Done

The Pareto improvement initiative is complete when:

* PAR-001 is resolved by explicit metric availability throughout all first-party surfaces.
* PAR-003 is resolved with screen/export filter parity.
* PAR-002 is resolved through chart and ranking semantic alignment.
* PAR-005 is resolved through documented export semantic parity.
* PAR-014 is resolved through canonical defaults and saved-config migration.
* PAR-006 is resolved by suppressing or marking Defect-axis opportunity share as not applicable.
* PAR-004 and PAR-007 are corrected.
* PAR-009 and PAR-013 are either resolved or conclusively dismissed by tests.
* All new and existing automated tests pass.
* A clean commit and regenerated AI review package describe the final implementation.
* The UI and every export provide an operator with the same mathematical meaning for the same Pareto configuration.

