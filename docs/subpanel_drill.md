# Plan: Pareto subpanel axis

_Owner: Pareto report. Audience: implementer. Status: implementation-ready._

## Goal

Extend the existing Pareto drill chain by one step. Clicking a
Reference designator bar stays on `/report/pareto`, adds that topology
to the URL filters, and re-runs the same report with `axis=Subpanel`.
Bars are the **cavity distribution** (`CARDS.Card_Number`), not a list
of physical boards.

```
Product · AOI machine · JEDEC / package  →  Defect  →  Part number  →  Reference designator  →  Subpanel (end)
```

Day and Shift remain non-drillable. Subpanel is selectable from the
axis dropdown so an engineer can start there.

## Locked decisions

These are not open. Do not reopen them in the implementation.

1. **This is a Pareto axis, not a new report.** No new route, no
   `GET /api/reports/subpanels`, no `IAoiSource` method, no paged
   grid, no multi-source merge, no jump to traceability.
2. **Group by slot, not instance.** Bucket key is
   `TestedObjectRow.CardIdOnPanel` / `CardRow.CardIdOnPanel`, which the
   SQL adapter already maps from `CARDS.Card_Number`. Do not project
   or group on `CARDS.Card_Id` (polymorphic `bigint`/`int`, unique per
   instance, used only in JOINs). Same slot on different panels **must
   combine** into one bar — that is the distribution.
3. **A Subpanel bar cannot open a board.** Bar `3` is every cavity-3
   in the window. Traceability from a contributing instance is a later
   slice with a different grain. Out of scope.
4. **Opportunities apply.** Unlike RefDes / part number / JEDEC, a
   slot has a card-derived denominator. Sum `NbOfTestsOnComp` /
   `NbOfTestsOnPads` from the existing card pass, keyed by
   `CardIdOnPanel`. `OpportunitiesApplicable = true`. Do **not** add
   Subpanel to `PARETO_OBJECT_LEVEL_AXES`. Requested DPMO/PPM weight
   is honoured, not snapped to Count.
5. **Narrowing collection is `CardNumbers`.** Integers, AND-combined
   with every existing Pareto filter, same pattern as `MachineIds` /
   `ProductIds`. URL `cardNumbers=1,3`. Display name is “Subpanel”.
   Do not add `Card_Id`, barcode, face, or inspection time to
   `ParetoRow`.
6. **Single source, existing endpoint.** Pareto remains
   `GET /api/reports/pareto` with one `sourceId`. The server stays
   authoritative for counts and DPMO.
7. **Empty is empty.** Zero matching defects → existing empty state,
   not an error. Slots with opportunities but no defects are omitted,
   matching Product / Machine today.

## Non-goals (this slice)

- Paged list of contributing physical cards.
- Navigation from a bar into `/traceability/board`.
- Teaching `FilterField.BoardNumber` to the Pareto TESTED_OBJECT
  generic-filter adapter (`ReportFilterRows.TestedObjectFields`).
  Drill uses the dedicated `CardNumbers` collection, as RefDes uses
  `Topologies`.
- Changing default `FakeAoiSource` fixture totals (15 defects / 200
  opportunities). Multi-up data lives in seeded report tests only.
- DPMO-table `DpmoGroupBy.Subpanel`. Do not expand sibling reports.

## Contract

### Axis

Add `ParetoAxis.Subpanel = 8` after `Shift = 7`. Endpoint alias
`subpanel` falls out of `TryParseEnumAlias` (member name and kebab
form). Document it in the `GET /api/reports/pareto` `axis` remark
alongside `reference-designator`.

SPA union and `PARETO_AXES` gain `"Subpanel"`. Tile config inherits
that list; `TILE_PARETO_AXES` already drops only Day/Shift, so
Subpanel appears on canvas tiles automatically.

### Filter

Append to `ParetoFilter` (defaulted, last — do not insert in the
middle of the positional record):

```csharp
IReadOnlyCollection<int>? CardNumbers = null
```

Append to `ParetoAppliedFilters`:

```csharp
IReadOnlyList<int> CardNumbers
```

Always materialise a non-null list in `EchoAppliedFilters` (empty
when unset), matching the other collections.

Pass 2 already short-circuits on `Topologies` / `PartNumbers` /
`JedecNames`. Add the same ordinal set for `CardNumbers` against
`obj.CardIdOnPanel`. Invalid / non-integer group keys on drill are a
no-op, matching Product / Machine.

### Grouping and denominator

`GroupKeyFor(ParetoAxis.Subpanel, obj, …)` →
`GroupKey.Int(obj.CardIdOnPanel)`.

`ResolveName` for Subpanel → the slot number as a decimal string
(same as `ToDisplayKey()`). No name lookup.

Pass 1: when `filter.Axis == ParetoAxis.Subpanel`, accumulate
`opportunitiesByCardNumber[card.CardIdOnPanel] += OpportunityFor(card, …)`
next to the existing machine / product / time-bucket dictionaries.

`OpportunitiesApplicableForAxis`: include `ParetoAxis.Subpanel` in
the **true** arm with AoiMachine / Product / Day / Shift / Defect.
Leave RefDes / PartNumber / Jedec in the false arm.

`OpportunityForGroup`: Subpanel looks up `opportunitiesByCardNumber`
the same way Product looks up `opportunitiesByProduct`.

Skip exclusion and NOGO already drop cards before the opportunity
add, and drop matching tested objects in pass 2. Slot 3 skipped on
one panel must not contribute; slot 3 on other panels still must.

### Drill (SPA)

Today `PARETO_DRILL_NEXT_AXIS` has no `ReferenceDesignator` entry, so
`paretoDrillInto` returns the same search and `DRILLABLE_AXES` omits
it.

Change:

```
PARETO_DRILL_NEXT_AXIS.ReferenceDesignator = "Subpanel"
```

Add a `case "ReferenceDesignator"` in `paretoDrillInto` that calls
`withStringFilter(search, "topologies", groupKey)` then advances.
Because the next axis is **not** object-level, do not force Count.

`DRILLABLE_AXES` gains `"ReferenceDesignator"`. Subpanel stays out
(new terminal). Day / Shift stay out.

`handleBarClick` already copies `topologies` into form state. It must
also copy `cardNumbers` so chips stay in sync if a later chip-remove
path uses form state. Add `cardNumbers` to `ParetoSearch`,
`validateParetoSearch`, `toApiQuery`, `withNumericFilter` /
`withoutNumericFilter` (extend those helpers' key union, or add a
dedicated pair — prefer extending the existing numeric helpers), form
converters, and breadcrumb chips next to product / machine.

`DrillDownMap` appends Subpanel as the new `(end)` and moves the end
label off Reference designator.

`paretoAxisLabelKey` is an exhaustive switch — add `"Subpanel"`. Same
for `i18n/bundle.ts` + `en.ts` + `fr.ts` at every Pareto axis map:

- `pareto.axis.Subpanel`
- `pareto.chart.axis.Subpanel`
- `canvas.tiles.pareto.axis.options.Subpanel`

French: “Sous-panneau” (already used for board-number labels).

Weight control stays **enabled** on Subpanel. The object-level N/A
banner (`pareto.table.opportunitiesUnavailable`) must not appear.

### Endpoint / export

Thread `cardNumbers` through `RunParetoAsync`, the CSV/XLSX/PDF
export handlers, and `TryBuildParetoRequest` as a CSV int list via
the existing `ParseIntList`. No new validation beyond “integers”
(unlike `defectBits` 1..25). Empty / omitted → no filter.

JSON, CSV, XLSX, and PDF already render `GroupKey` / `GroupName` /
opportunity columns generically. Subpanel rides those paths once the
axis exists. Confirm PDF/SVG axis captions do not special-case a
closed set of names.

`ParetoReport.ReportDescriptor.Description` currently lists axes;
add subpanel.

SPA `ParetoAppliedFilters` in `api/pareto.ts` must include
`cardNumbers: number[]`.

## Fixtures

Do not change the default fake source’s 15-defect / 200-opportunity
golden numbers. Extend the **test helpers** in
`ParetoReportTests` (`Obj` / `Card`) with an optional
`cardIdOnPanel` (default `1` so existing tests stay green).

Seeded cases the report tests must cover:

- One panel, slots 1 and 2, defects on both → two bars.
- Two panels, both slot 1 defective → **one** bar `"1"` with summed
  defects (distribution, not instance identity).
- Inherited `Topologies` / `PartNumbers` / `DefectBits` AND with
  `CardNumbers`.
- Skip-excluded slot dropped from both numerator and denominator.
- Unequal opportunity counts per slot so DPMO rank can differ from
  Count rank.
- Window / machine / product / NOGO behaviour unchanged vs parent
  filters.

## Tests (write failing first)

### Report (`ParetoReportTests`)

- `SubpanelAxis_GroupsByCardNumber_SameSlotAcrossPanelsCombines`
- `SubpanelAxis_OpportunitiesApplicable_PerSlotDenominator`
- `SubpanelAxis_RequestedDpmo_IsHonouredAndCanReorderVsCount`
- `SubpanelAxis_CardNumbersFilter_AndsWithTopologyAndDefectBits`
- `SubpanelAxis_SkipExclusion_DropsSlotFromBothPasses`
- Existing `ObjectLevelAxes_OpportunitiesNotApplicable` theory must
  **not** gain Subpanel.

### Endpoint (`DpmoAndParetoEndpointsTests`)

- `axis=subpanel` (kebab) and `axis=Subpanel` both 200.
- `cardNumbers=1,2` echoed on `appliedFilters.cardNumbers`.
- Drill-shaped request: `axis=subpanel&topologies=R12&partNumbers=PN-A&defectBits=1`
  returns only that slot’s contributors.
- Unknown axis still 400. Empty valid window still 200 with zero rows.

### SPA (`pareto.search.test.ts`)

- `paretoDrillInto` on Reference designator adds the topology, sets
  `axis: "Subpanel"`, and does **not** snap weight to Count.
- Subpanel is terminal (`paretoDrillInto` same reference).
- Full chain: Product → Defect → PartNumber → ReferenceDesignator →
  Subpanel. Update the test that currently asserts RefDes is terminal.
- `validateParetoSearch({ axis: "Subpanel", weight: "Dpmo" }).weight`
  stays `"Dpmo"`.
- `toApiQuery` emits `cardNumbers`.

### UI / i18n

- `DrillDownMap` copy: chain ends at Subpanel; Day/Shift still called
  out as not drillable.
- `paretoAxisLabelKey` compiles (exhaustive). Locale files and
  `bundle.ts` stay in lockstep — the existing i18n test will catch a
  missing key if you add the type first.

### E2E

Keep the Defect-axis golden smoke untouched. Add a focused case:
login → open Pareto → walk the last two drill steps (or open with
`axis=ReferenceDesignator` and click a bar) → URL contains
`axis=Subpanel` and `topologies=` → JSON `axis === "Subpanel"`.
Do not assert multi-slot distribution here; the default fixture is
slot 1.

## Implementation order

1. Append axis + `CardNumbers` on the DTOs. Fix compile of
   `EchoAppliedFilters` / SPA types. No behaviour yet.
2. Seeded fixtures + failing report tests.
3. `GroupKeyFor`, pass-1 slot denominator, pass-2 `CardNumbers` set,
   `OpportunitiesApplicableForAxis`, `OpportunityForGroup`,
   `ResolveName`.
4. Endpoint `cardNumbers` plumbing + endpoint tests.
5. SPA search model, drill map, chips, axis switch, i18n.
6. Chart / weight-disabled / N/A-banner paths (they key off
   `PARETO_OBJECT_LEVEL_AXES` — Subpanel must not be in that set).
7. Focused suites, then full solution + web tests.

## Definition of done

- Drill map reads: Product · AOI machine · JEDEC → Defect → Part
  number → Reference designator → Subpanel (end).
- Clicking a RefDes bar produces a Subpanel Pareto of `Card_Number`
  under the inherited window, source, numerator, opportunity, skip,
  NOGO, and every prior drill filter.
- Same slot on different panels is one bar.
- DPMO/PPM is a real per-slot rate, never N/A, never coerced to 0
  because the axis was miscategorised as object-level.
- Subpanel bars are not clickable. Browser Back restores the RefDes
  view with its filters.
- CSV / XLSX / PDF / canvas tile accept `axis=Subpanel` without a
  second code path.
- No AOI writes. No new API resource. Default fake fixture totals
  unchanged.

## Guardrails

- Inspect `ParetoReport.GroupKeyFor` / `OpportunitiesApplicableForAxis`
  and `paretoDrillInto` before adding abstractions. Copy those
  patterns; do not invent a drill framework.
- Do not put Subpanel in `PARETO_OBJECT_LEVEL_AXES`.
- Do not key bars on `Card_Id`, `(PanelId, CardId)`, or barcode.
- Do not open traceability from this axis.
- Do not “fix” generic `FilterField.BoardNumber` on Pareto in this
  slice.
- Do not retune default fake-source totals to make a multi-slot e2e
  prettier.
- Keep commits phase-focused; ship tests with the behaviour they
  lock.
