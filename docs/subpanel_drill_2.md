# Plan: Pareto subpanel axis (reviewed)

_Owner: Pareto report. Audience: implementer. Status: implementation-ready._

## Review outcome

The original direction is sound: Subpanel belongs in the existing Pareto report,
the grouping key is `CARDS.Card_Number`, and a Subpanel bar represents a cavity
distribution rather than a physical board.

This revision closes the following implementation gaps found against the current
code:

1. **`CardNumbers` must filter both passes.** Filtering only
   `TestedObjectRow` would leave `Overall.OpportunityCount` and opportunity
   shares scoped to all slots. Apply the same set to `CardRow` before adding
   opportunities and to `TestedObjectRow` before counting defects.
2. **Browser Back needs explicit form synchronization.** The report query follows
   the URL today, but the local form is initialized only once. The existing
   click-time `setForm` call does not make Back/Forward restore the axis and
   chips. Add and test URL-to-form synchronization.
3. **PDF has a separate endpoint signature.** `ExportParetoPdfAsync` in
   `ReportEndpoints.Pdf.cs` must receive and forward `cardNumbers`; changing
   `TryBuildParetoRequest` alone is insufficient.
4. **XLSX has a materialized applied-filter sheet.** Add `CardNumbers` there and
   update tests that depend on filter-row order.
5. **Canvas support needs an explicit contract test.** The tile parser accepts
   enum values generically, and the SPA option list derives from `PARETO_AXES`,
   but a test should lock both assumptions.
6. **The DPMO drill expectation needs precision.** Entering Part number and
   Reference designator already forces Count because those axes have no
   denominator. Advancing from Reference designator to Subpanel must not force
   Count again, but the full default drill chain will still arrive at Subpanel
   in Count mode. A directly requested `axis=Subpanel&weight=Dpmo|Ppm`, or a
   user selection on Subpanel, must be honored.
7. **The E2E test should not click ECharts by pixel coordinate.** Cover the click
   transition in a route/component test using the chart callback, and use E2E
   to verify a Subpanel URL/API response. This is deterministic and tests the
   same boundaries without canvas flakiness.
8. **DTO changes affect snapshots and positional fixtures.** Adding
   `ParetoAppliedFilters.CardNumbers` changes every Pareto snapshot and the
   positional fixture in `ParetoChartSvgTests`; these updates must be reviewed,
   not left to a late compile/test sweep.
9. **Subpanel needs an Others-bucket invariant.** Unlike the Defect axis,
   Subpanel overflow must sum the opportunity counts of the hidden slots. It
   must not reuse the Defect-only “overall denominator once” special case.

## Goal

Extend the existing Pareto drill chain by one step. Clicking a Reference
designator bar stays on `/report/pareto`, appends that topology to the URL
filters, and reruns the same report with `axis=Subpanel`.

```
Product · AOI machine · JEDEC / package
  → Defect
  → Part number
  → Reference designator
  → Subpanel (end)
```

Subpanel bars show the defect distribution by cavity/slot
(`CARDS.Card_Number`). They do not identify individual physical boards. Day and
Shift remain non-drillable. Subpanel is also selectable directly from the axis
dropdown.

## Locked semantics

1. **Existing report and route.** Keep `GET /api/reports/pareto` and
   `/report/pareto`. Do not add a report, route, `IAoiSource` method, paged grid,
   source merge, or traceability navigation.
2. **Group by slot, not instance.** Use
   `TestedObjectRow.CardIdOnPanel` / `CardRow.CardIdOnPanel`, mapped by the SQL
   adapter from `CARDS.Card_Number`. Never group on `CARDS.Card_Id`, panel ID,
   barcode, face, or inspection time. Slot 3 on different panels combines into
   one bar `"3"`.
3. **Terminal distribution.** A Subpanel bar is not clickable. It represents all
   contributing instances of that slot in the selected population and cannot
   select one board.
4. **Per-slot opportunities apply.** Sum `NbOfTestsOnComp` and/or
   `NbOfTestsOnPads` from the card pass by `CardIdOnPanel`.
   `OpportunitiesApplicable` is true. Do not add Subpanel to
   `PARETO_OBJECT_LEVEL_AXES`.
5. **Optional narrowing contract.** Add `CardNumbers` as an integer collection,
   serialized as `cardNumbers=1,3`. It is AND-combined with all other filters
   and scopes both numerator and denominator. The current drill chain does not
   generate this filter because Subpanel is terminal; it remains useful for
   direct URLs, saved views, API callers, and future extensions.
6. **Single source.** Keep one `sourceId`; the server remains authoritative for
   counts, opportunities, ranking, and DPMO/PPM.
7. **No zero-defect buckets.** Slots with opportunities but no matching defects
   remain omitted, consistent with Product and Machine.
8. **No schema/source changes.** Do not change SQL projections, `IAoiSource`,
   `CardRow`, `TestedObjectRow`, or default fake-source totals.
9. **Mixed-grain denominator is intentional.** After drilling through a topology,
   part number, or defect bit, the numerator is narrowed but each slot's
   denominator remains the full card-derived test count for that slot. This
   matches existing Product/Machine drill semantics; do not attempt to derive
   topology-level opportunities from the defect-only tested-object stream.

## Data and API contract

### Axis

Append `ParetoAxis.Subpanel = 8` after `Shift = 7`. Do not renumber existing enum
members.

Update stale XML documentation in `ParetoReportDtos.cs` so it does not claim a
fixed number of axes. Add `subpanel` to the `axis` parameter documentation in
`ReportEndpoints.Pareto.cs`. `TryParseEnumAlias` should accept `subpanel`,
`Subpanel`, and case variants without a special parser branch.

Add `"Subpanel"` to the SPA `ParetoAxis` union and `PARETO_AXES`. Keep it out of
`PARETO_OBJECT_LEVEL_AXES`.

### Filter DTOs

Append this defaulted parameter at the end of `ParetoFilter`:

```csharp
IReadOnlyCollection<int>? CardNumbers = null
```

Appending preserves positional-call compatibility. Update the XML documentation
to state that `CardNumbers` is a within-panel slot filter and applies to both
cards and tested objects.

Append to `ParetoAppliedFilters`:

```csharp
IReadOnlyList<int> CardNumbers
```

`EchoAppliedFilters` must copy it to a stable, non-null list. Fix every
positional `ParetoAppliedFilters` construction, including PDF/SVG tests.
Update every existing `Pareto_*.expected.json` snapshot to include
`"cardNumbers": []`, and inspect each snapshot diff.

The SPA `ParetoAppliedFilters` response type gains:

```ts
cardNumbers: number[];
```

### `CardNumbers` filtering invariant

Build one `HashSet<int>?` near the start of `ParetoReport.RunAsync` when the
collection is non-empty and reuse it in both passes:

- **Pass 1 / cards:** skip a card whose `CardIdOnPanel` is not selected before
  adding to `opportunitiesOverall`, any per-axis opportunity dictionary, or
  `skipExcludedCards`.
- **Pass 2 / tested objects:** skip an object whose `CardIdOnPanel` is not
  selected before generic filters, numerator counting, and grouping.

This invariant applies even when the active axis is Product, Machine, Defect,
Day, or Shift. A request with `cardNumbers=1` must describe only slot 1
throughout the result:

- `Overall.TestedObjectCount`
- `Overall.DefectBitCount`
- `Overall.OpportunityCount`
- row counts and rates
- opportunity shares
- skip-excluded count

Apply NOGO and skip rules exactly as today after the slot is admitted. Building
the skip index over the broader card population is acceptable; reported
`SkipExcludedCards` must still count only cards inside the requested slot
filter.

Use the existing integer-list parsing convention. Empty/omitted means no filter.
Do not impose a positivity rule: the adapter and existing tests permit integer
`Card_Number` values including zero. Existing `ParseIntList` ignores malformed
tokens; do not change all list-parser semantics in this slice.

## Report implementation

### Grouping

In `ParetoReport.GroupKeyFor`:

```csharp
ParetoAxis.Subpanel => GroupKey.Int(obj.CardIdOnPanel)
```

In `ResolveName`, return the invariant decimal display key for Subpanel. Both
`GroupKey` and `GroupName` should therefore be `"3"` for slot 3.

### Opportunity denominator

Create `opportunitiesByCardNumber` only when
`filter.Axis == ParetoAxis.Subpanel`.

For every admitted card in pass 1:

```text
opportunitiesByCardNumber[card.CardIdOnPanel] +=
    OpportunityFor(card, filter.Opportunity)
```

Thread the dictionary into `BuildRows`. In `OpportunityForGroup`, look up the
integer group key just as Product looks up `opportunitiesByProduct`.

Add Subpanel to the true arm of `OpportunitiesApplicableForAxis`. Leave
Reference designator, Part number, and JEDEC false.

Consequences to lock in tests:

- Count weighting uses defect count.
- DPMO/PPM weighting uses the per-slot denominator.
- A real zero denominator emits zero under existing report semantics.
- Opportunity share is per-slot opportunities divided by the filtered overall
  opportunities.
- Skip, NOGO, window, machine, product, opportunity flavor, numerator, and all
  object-level filters retain their existing AND semantics.

For `TopN` overflow, Subpanel follows Product/Machine behavior:
`Others.OpportunityCount` is the sum of the hidden slots' denominators. Do not
enter the `filter.Axis == ParetoAxis.Defect` special case that substitutes the
overall denominator.

### Documentation and metadata

Update `ParetoReport` class remarks, filter lists, per-group denominator
comments, `ParetoRow.OpportunitiesApplicable` docs, and
`ReportDescriptor.Description` to include Subpanel and `CardNumbers`.

Do not add `DpmoGroupBy.Subpanel` or teach the generic
`ReportFilterRows.TestedObjectFields` adapter about `FilterField.BoardNumber` in
this slice.

## Endpoint and export plumbing

Add `string? cardNumbers` adjacent to the other narrowing lists and forward it
to `TryBuildParetoRequest` in every standalone Pareto entry point:

1. `RunParetoAsync` in `ReportEndpoints.Pareto.cs`
2. `ExportParetoCsvAsync` in `ReportEndpoints.Pareto.cs`
3. `ExportParetoXlsxAsync` in `ReportEndpoints.Pareto.cs`
4. `ExportParetoPdfAsync` in `ReportEndpoints.Pdf.cs`

Add the parameter to `TryBuildParetoRequest`, parse it with `ParseIntList`, and
pass it as the final named `ParetoFilter` argument. Update XML `<param>` docs on
all public endpoint contracts.

The row renderers are generic and need no Subpanel branch:

- JSON serializes `GroupKey`, `GroupName`, and opportunity fields.
- CSV uses `ParetoPresentation`.
- XLSX row cells use `OpportunitiesApplicable`.
- PDF table and SVG use `GroupName ?? GroupKey`.

The XLSX **Applied Filters** sheet is not generic. Append a `CardNumbers` row
after `JedecNames` and update its row-order assertions.

### Canvas tile

Subpanel should be selectable on Pareto tiles because
`TILE_PARETO_AXES = PARETO_AXES - {Day, Shift}`. The backend
`ParseParetoTileConfig` already parses enum values generically.

Do not add `CardNumbers` to `ParetoFromTileRequest`: tile-level report filters
currently carry only source/window/machine/product plus config JSON, and this
slice only promises Subpanel as a tile axis.

Canvas Pareto bars remain non-drillable, and `FilterContext` does not gain
`cardNumbers` in this slice.

Add tests proving:

- the tile configuration schema exposes Subpanel;
- `ParseParetoTileConfig({"axis":"Subpanel"})` retains Subpanel;
- `/api/reports/pareto/from-tile` can return a Subpanel result.

## SPA drill and URL state

### Search model

Add `cardNumbers?: number[]` to `ParetoSearch` and thread it through:

- `validateParetoSearch` via `toNumberArray`;
- `toApiQuery`;
- `withNumericFilter` / `withoutNumericFilter` key unions;
- `FormState`, `emptyForm`, `searchToForm`, and `formToSearch`;
- `handleBarClick` form synchronization;
- active-filter chips and removal.

Render chips as `Subpanel: N` using `pareto.axis.Subpanel`.

### Drill progression

Add:

```ts
PARETO_DRILL_NEXT_AXIS.ReferenceDesignator = "Subpanel";
```

Add a `ReferenceDesignator` case in `paretoDrillInto`:

```ts
narrowed = withStringFilter(search, "topologies", groupKey);
```

Then advance to Subpanel. Because Subpanel is not object-level, this transition
must not set `weight: "Count"`. Subpanel has no next-axis entry and remains
terminal.

Add Reference designator to `DRILLABLE_AXES`; keep Subpanel, Day, and Shift out.
Update `DrillDownMap` so `(end)` appears after Subpanel.

Update the stale `ParetoRoute` and `handleBarClick` comments that describe the
old manual-switch flow or claim every category advances directly to Defect.

### Weight behavior

Keep the weight control enabled on Subpanel. The object-level N/A banner must
not appear.

Be explicit in tests:

- `validateParetoSearch({ axis: "Subpanel", weight: "Dpmo" })` preserves Dpmo.
- A direct Reference-designator-to-Subpanel transformation with an explicit
  rate weight does not overwrite it.
- The full normal drill chain reaches Subpanel in Count mode because the earlier
  object-level steps already selected Count. This is expected; users can then
  choose DPMO/PPM.

Do not add hidden “previous weight” state in this slice.

### Back/Forward correctness

The URL is the source of truth, but `form` is currently initialized from it only
on mount. Add URL-to-form synchronization when the canonical search state
changes through browser history or external navigation. Ensure the effect does
not run merely because the user edits local form controls; depend on stable URL
state rather than mutable form state.

Test this at route level:

1. open Reference designator state;
2. invoke a real-bar callback and observe Subpanel/topology in URL and form;
3. navigate Back;
4. verify the report axis, axis selector, and chips all restore to Reference
   designator state.

This test is required by the stated browser-Back definition of done.

## i18n

Add `Subpanel` to the three actual Pareto axis maps in `bundle.ts`, `en.ts`, and
`fr.ts`:

- `pareto.axis.Subpanel`
- `pareto.chart.axis.Subpanel`
- `admin.reports.editor.tiles.config.pareto.axis.options.Subpanel`

Use “Subpanel” in English and “Sous-panneau” in French. Update nearby subtitle or
help copy that enumerates available axes. `i18n.test.tsx` checks only top-level
locale parity; nested completeness is enforced by the `bundle.ts` type and the
TypeScript build.

`paretoAxisLabelKey` is currently unused. Delete this dead helper when touching
the axis list rather than extending a switch that provides no runtime or
compile-time protection. If it becomes used before implementation, wire all
axis-label call sites through it and then keep its switch exhaustive.

## Fixtures

Do not modify the default `FakeAoiSource` totals of 15 defects and 200
opportunities.

Extend the local `ParetoReportTests` helpers with optional `panelId` and
`cardIdOnPanel`, both defaulting to their current value. This makes the
distribution grain visible in test setup and avoids repeated `with` expressions.

Use seeded report/API tests for multi-slot cases. Include slot `0` in one case
to prevent an accidental one-based validation or truthiness bug; current data
contracts and tests permit it.

## Test matrix

### Report tests

Add:

- `SubpanelAxis_DifferentSlotsProduceDifferentBars`
- `SubpanelAxis_SameSlotAcrossPanelsCombines`
- `SubpanelAxis_UsesPerSlotOpportunityDenominator`
- `SubpanelAxis_DpmoCanReorderCountRanking`
- `SubpanelAxis_PpmUsesSameRateAsDpmo`
- `CardNumbersFilter_ScopesBothNumeratorAndDenominator`
- `CardNumbersFilter_AndsWithTopologyPartNumberAndDefectBits`
- `CardNumbersFilter_ScopesSkipExcludedCount`
- `SubpanelAxis_SkipExclusionDropsCardFromBothPasses`
- `SubpanelAxis_ZeroDefectSlotsAreOmitted`
- `SubpanelAxis_TopNOthersSumsHiddenSlotOpportunities`
- `SubpanelAxis_ZeroSlotIsAValidGroup`

Assertions should include exact group keys/names, overall opportunities, row
opportunities, opportunity shares, applied weight, and stable ordering.

Update the existing object-level-axis theory only to confirm Subpanel is absent;
do not classify it as object-level.

### Endpoint tests

Add:

- `axis=subpanel` and `axis=Subpanel` return 200;
- `cardNumbers=1,2` is echoed in
  `appliedFilters.cardNumbers`;
- card-number narrowing changes both overall opportunities and defects;
- a drill-shaped request with Subpanel axis plus topology, part number, and
  defect bits returns only matching contributors;
- unknown axis remains 400;
- an empty valid window remains 200 with no rows.

Exercise at least one standalone export with `cardNumbers`:

- CSV/PDF: request succeeds and contains only selected-slot rows;
- XLSX: `Applied Filters` includes `CardNumbers`, and row-order assertions are
  updated.

Compile coverage must prove all four endpoint signatures forward the parameter.

### SPA search tests

Update `pareto.search.test.ts`:

- Subpanel validates and serializes;
- `cardNumbers` parses from CSV/array and emits through `toApiQuery`;
- numeric add/remove helpers support `cardNumbers`;
- Reference designator adds `topologies` and advances to Subpanel;
- Subpanel returns the same reference from `paretoDrillInto`;
- full chain ends at Subpanel;
- Subpanel preserves direct DPMO/PPM requests;
- Day and Shift remain unchanged.

Update comments that still call Reference designator terminal.

### Route/chart tests

Add or extend a route test with ECharts mocked as in `ParetoChart.test.tsx`:

- Reference designator rows are clickable;
- clicking a real row navigates to Subpanel with `topologies`;
- Subpanel rows have no click handler;
- the Subpanel weight control is enabled;
- the object-level opportunity warning is absent;
- a `cardNumbers` chip renders and removes correctly;
- Back/Forward synchronizes URL, results, selector, and chips.

Keep the existing test that Others is never drillable.

### Canvas and i18n tests

- Tile schema option list includes Subpanel but still excludes Day/Shift.
- Tile parser and from-tile endpoint retain Subpanel.
- the dead `paretoAxisLabelKey` helper is removed, or is wired and exhaustive;
- English, French, and `bundle.ts` stay structurally identical.

### E2E

Keep the existing Defect-axis golden smoke and its 15/200 assertions untouched.

Add a focused, deterministic case:

1. log in;
2. open Pareto with a valid `axis=Subpanel` URL;
3. verify the displayed axis and terminal chart state;
4. call the JSON endpoint and assert `axis === "Subpanel"` plus slot-shaped
   rows.

Do not assert a multi-slot distribution against the default fixture, which uses
slot 1. Do not automate an ECharts canvas click by screen coordinates; the route
integration test owns click-to-URL behavior.

Extend the local `ParetoSmokeSearch` axis type so the new Subpanel URL compiles.

## Implementation order

1. Add `ParetoAxis.Subpanel`, append DTO fields, fix positional constructions
   and Pareto snapshots, and update comments/XML docs until the solution
   compiles.
2. Extend test helpers and write failing report tests for grouping, per-slot
   denominator, both-pass filtering, and DPMO ranking.
3. Implement the shared card-number set, pass-1 denominator dictionary,
   pass-2 grouping/filtering, applicability, lookup, and name resolution.
4. Thread `cardNumbers` through JSON, CSV, XLSX, and PDF endpoint signatures;
   update the XLSX applied-filter sheet and endpoint/export tests.
5. Add SPA search types, serialization, helper support, form/chips, drill map,
   terminal behavior, exhaustive switches, and translations.
6. Add URL-to-form Back/Forward synchronization and its route integration test.
7. Verify tile schema/parser/from-tile behavior for Subpanel.
8. Run focused suites, then the full backend and web suites, then the focused
   Playwright case.

## Verification commands

Use the repository's existing command conventions, but the final verification
must cover:

- `Nieweb.Reports.Tests` Pareto tests;
- `Nieweb.Api.Tests` Pareto endpoint, export, tile-config, and PDF/SVG tests;
- web Vitest search, chart, route, tile-config, and i18n tests;
- full `.NET` solution build/test;
- full web typecheck/test;
- focused Pareto Playwright spec.

Treat snapshots as review artifacts: inspect changes before accepting them. Do
not update snapshots merely to make failures disappear.

## Definition of done

- The visible drill map ends in
  `Reference designator → Subpanel (end)`.
- Clicking a Reference designator bar adds its topology, changes the URL to
  `axis=Subpanel`, and reruns the report without leaving the route.
- Subpanel groups by `Card_Number`; the same slot across panels is one bar.
- `CardNumbers`, when supplied, scopes both passes and every overall/row metric.
- Count, DPMO, and PPM behave according to their existing contracts; Subpanel
  never reports opportunity metrics as N/A and never gets coerced solely because
  of its axis.
- Subpanel bars and Others are not clickable. Day and Shift remain
  non-drillable.
- Browser Back/Forward restores report state, selector state, and chips.
- JSON, CSV, XLSX, PDF, and Pareto canvas tiles accept Subpanel through their
  existing paths.
- XLSX echoes `CardNumbers` in Applied Filters.
- No AOI writes, source/schema changes, new API resource, default-fixture
  retuning, or traceability navigation are introduced.

## Guardrails

- Inspect and extend the existing switches and helpers; do not introduce a new
  drill framework for one axis.
- Keep enum values stable and append defaulted positional-record fields.
- Do not key a distribution bar by `Card_Id`, `(PanelId, CardIdOnPanel)`, or
  barcode.
- Do not filter only the tested-object pass when `CardNumbers` is present.
- Do not add Subpanel to `PARETO_OBJECT_LEVEL_AXES`.
- Do not add `DpmoGroupBy.Subpanel` or generic BoardNumber filtering.
- Do not add per-board traceability from a distribution bar.
- Do not make canvas-coordinate E2E assertions.
- Keep commits phase-focused and ship each behavior with the test that locks its
  grain and denominator.
