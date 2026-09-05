# Nieweb Pareto Improvement Plan (agreed scope)

## 1. Purpose

This is the implementation plan for the Pareto work we are actually going to do. It is a narrowed successor to `docs/pareto_improvement.md`.

The first original plan was a full-surface cleanup. This plan keeps the operator-trust fixes and drops the extra contract, transport, and process work that is not required to stop the report from lying.

Two user-trust bugs are in scope:

1. **Screen vs export.** A canvas Pareto tile can show one population while the saved-report export of that tile applies extra generic filters and shows another.
2. **Zero vs unavailable.** Reference designator, part number, and JEDEC rows encode a missing per-group opportunity denominator as numeric zero, so the UI and exports show DPMO of 0 next to nonzero defects.

Also in scope: chart bars must plot the metric that ranked the rows; TypeScript must admit DPMO/PPM weights; Defect-axis opportunity share must not be presented as a partition of production.

Two adjacent correctness issues found while verifying this plan against the tree are also in scope because they sit inside the same trust surface:

3. **Defect-axis Others denominator.** With `TopN` on the Defect axis (the tile default is Defect / Top N 10) the Others row sums `totalOpportunities` once per collapsed bit, so its opportunity share exceeds 100% and its DPMO is understated by the number of collapsed bits.
4. **Tile filters fail open.** `ParseTileFilters` silently drops malformed clauses and nulls the whole filter on validation failure, so a typo in a saved tile filter *widens* the exported population instead of failing.

## 2. Relationship to `pareto_improvement.md`

Treat that document as background review notes. This document is the build spec.

| Original item | This plan |
|---|---|
| Explicit row metric availability (`opportunitiesApplicable`) | **Do** |
| Hide misleading Defect-axis opportunity share | **Do** (UI/export rule keyed on axis; no second API flag) |
| Canvas screen/export filter parity | **Do** |
| Chart bars follow ranking weight | **Do** |
| Characterization tests before behavior change | **Do** |
| Additive JSON field, keep existing numeric fields | **Do** |
| Server 400 for DPMO on object-level axes | **Do not.** Normalize to Count and echo the applied weight |
| `OpportunityShareApplicable` on every row | **Do not** |
| Cumulative line on rate-weighted charts | **Do not.** Hide the cumulative series in DPMO/PPM mode |
| Full ParetoFilter POST + GET encoding of `FilterRequest` | **Do not** as the first transport. Reuse the export tile filter builder (made strict, see 6.4) |
| Canonical default unification / saved-tile opportunity migration | **Defer** (standalone defaults to All; tiles default to Components) |
| Dual legacy CSV zero columns | **Do not** |
| Nullable `OpportunityCount` / `DpmoPpm` | **Defer** until first-party clients are on applicability |
| Regenerate `AI-REVIEW-PARETO` as definition of done | **Defer** |
| Invent per-ref-des / per-part / per-JEDEC denominators | **Out of scope** |
| *(new)* Defect-axis Others row uses the overall denominator once | **Do** (found during plan verification; no existing test covers TopN on the Defect axis) |
| *(new)* Strict (fail-closed) tile filter parsing | **Do** (prerequisite for the WP3 "malformed filters → 400" acceptance) |
| *(new)* Echo `VitalFewThresholdPercent` on `ParetoResult` | **Do** (additive; the tile chart needs it once the server parses `configJson`) |

## 3. Baseline and constraints

### 3.1 Baseline

Work from the current `backup/phase-c-2026-09-05` tree after the Subpanel Pareto axis was removed. Do not reintroduce `ParetoAxis.Subpanel`.

As of this revision the Subpanel removal is **still uncommitted** in the working tree (12 modified files, including `ParetoReport.cs`, `ParetoReportDtos.cs`, `pareto.tsx`, `pareto.search.ts`, the i18n bundles, and both Pareto test suites). Commit it as its own baseline commit before the WP1 PR, otherwise the WP1 PR carries unrelated deletions and the three work-package commits stop being attributable.

### 3.2 Architecture to preserve

- `ParetoReport.RunAsync` remains the only aggregator.
- Overall opportunities remain card test counts (`NbOfTestsOnComp` / `NbOfTestsOnPads`), not tested-object row counts.
- Defect count remains selected bits 1–25. One tested object with two selected bits still contributes two occurrences.
- Count remains the default ranking mode.
- The vital-few set still includes the crossing category.
- Dedicated JSON / CSV / Excel / PDF continue to share `ParetoResult`.
- Auth, source resolution, skip/NOGO, Day/Shift, Top N / Other, and volume-weighted Product ranking stay as they are.

### 3.3 Non-goals (this initiative)

- True per-reference-designator, per-part-number, or per-JEDEC opportunity denominators.
- Changing empty saved tiles from `opportunity: All` vs `Components` fallback.
- A versioned nullable API.
- A general-purpose POST of `ParetoFilter` for the standalone bookmarkable GET.
- Client-side CSV vs server CSV naming cleanup beyond what WP2/WP3 already touch.
- Obsolete-bit cumulative investigation, Shift-without-schedule 500 hunt, and large-`long` overflow tests, unless a WP1 characterization test fails and forces the issue.

---

## 4. Target behavior

After implementation:

- The same saved Pareto tile configuration produces the same overall counts, row keys, order, defect counts, and applicability on screen and in saved-report export.
- Ref des / part / JEDEC rows show opportunities and DPMO as unavailable (`N/A` / `—` / blank), not as measured zero.
- A Product/machine/day/shift group whose cards really have zero tests still shows numeric 0 with applicability true.
- Overall KPI opportunities and overall DPMO stay visible and unchanged in meaning.
- Requesting DPMO/PPM on an object-level axis ranks by Count and echoes `weight: Count`.
- Count charts plot `defectCount` with a cumulative defect line. DPMO/PPM charts plot `weightedScore` and **omit** the cumulative line, the vital-few colouring, and the cumulative / vital-few columns in first-party table, tooltip, and PDF table (see 6.3).
- All first-party presentation (chart mode, axis titles, table columns, weight selector state) keys off the **server-echoed** `result.weight`, never the requested weight.
- Defect-axis DPMO remains overall-denominator rate, **including the Others row**. Defect-axis opportunity share is not shown.
- `ParetoResult.weight` in TypeScript is `"Count" | "Dpmo" | "Ppm"`. `ParetoResult` also echoes `vitalFewThresholdPercent`.
- A tile `configJson` whose `filters` array contains a malformed or invalid clause fails with a readable error on both the canvas POST and the saved-report export; it never silently runs wider.
- First-party JSON, table, chart tooltip, CSV, Excel, and PDF agree on availability.

---

## 5. Current defects (verified in tree)

### 5.1 Tile drops generic filters (PAR-003)

`ParetoTile.tsx` builds a `ParetoSearch` from canvas source/window/machine/product plus analytic knobs from `parseParetoTileConfig`, then calls GET `/api/reports/pareto` via `runParetoReport` / `toApiQuery`.

It never sends `cfg.filters`. The React Query key also omits `cfg.filters` and `cfg.vitalFewThreshold`.

Saved-report export **does** apply those filters: `RunParetoForTileAsync` sets `Filters: ParseTileFilters(configJson)`.

`toApiQuery` has no `filters` key. `RunParetoAsync` currently hard-codes `Filters: null`. So even if the tile started stuffing clauses into the GET today, the standalone endpoint would ignore them.

### 5.2 Object-level zeros (PAR-001)

`OpportunityForGroup` returns 0 for every axis that is not machine / product / day / shift. `ScoreFor` then yields DPMO 0 when `OpportunityCount == 0`. The SPA types `weight: "Count"` only. The UI has no applicability concept, so 0 renders as 0.

The SPA already forces Count when **drilling into** an object-level axis (`PARETO_OBJECT_LEVEL_AXES`). Direct axis selection, tiles, and non-UI callers can still request DPMO on those axes and get a key-ordered “Pareto”.

### 5.3 Chart ignores ranking weight (PAR-002)

`ParetoChart.tsx` always sets bar `value: row.defectCount` and scales the left axis from max defect count. PDF SVG follows the same count-height rule. Under DPMO weight the server sort is by `weightedScore`, so the first-ranked row is not necessarily the tallest bar.

### 5.4 Defect-axis opportunity share (PAR-006)

Defect rows all use `totalOpportunities` as the row denominator, so `OpportunitySharePercent` is 100 (or undefined-looking) on every row. That is not “share of production owned by this defect type.”

### 5.5 Defect-axis Others denominator (new, found in plan verification)

`BuildRows` Step 1 gives every Defect-axis `Unranked` row `OpportunityCount = totalOpportunities`. Step 5 then sums `u.OpportunityCount` over the overflow, so with N collapsed bits the Others row has `OpportunityCount = N × totalOpportunities`, `OpportunitySharePercent = 100 × N`, and `DpmoPpm` understated by N. Under `Weight = Dpmo` on the Defect axis the Others `WeightedScore` is wrong for the same reason.

The tile default (`axis = Defect`, `topN = 10`) hits this whenever more than ten bits are active, so the saved-report XLSX / CSV Pareto sheet prints it today. No existing test runs `TopN` on the Defect axis (`TopN_CollapsesOverflowIntoOthersBucket` and `Pareto_TopN_CollapsesOverflowIntoOthers` are both Product axis).

### 5.6 Tile filters fail open (new, found in plan verification)

`ReportEndpoints.TileConfig.ParseTileFilters`:

- skips (`continue`) any clause whose `field` / `operator` is missing, not a string, or not a known enum member — so a two-clause filter with one typo runs as a one-clause filter;
- returns `null` (no filter at all) when no clause survives **or** when `FilterValidator.Validate(request).IsValid` is false.

It never returns an error. The plan previously stated the opposite (“already fail closed … returning a user-readable 400”); that was wrong and would have made the WP3 acceptance criterion unreachable by reusing the parser as-is. The SPA parser `parseFilterRequest` in `api/filters.ts` is equally lenient, which matters less once both screen and export execute server-side.

---

## 6. Design decisions (locked)

### 6.1 Applicability is one boolean

```csharp
bool OpportunitiesApplicable
```

on `ParetoRow`. Keep `OpportunityCount`, `OpportunitySharePercent`, and `DpmoPpm` as numbers for compatibility. First-party UI and exports **must not** present those numbers when applicability is false.

| Axis | `OpportunitiesApplicable` | Row DPMO meaning | Opportunity share in UI/export |
|---|---|---|---|
| AoiMachine | true | Card opps for that machine | Show |
| Product | true | Card opps for that product | Show |
| Day / Shift | true | Card opps for that bucket | Show |
| Defect | true | Overall card opps (rate of this bit in the sample) | **Hide** |
| ReferenceDesignator | false | Unavailable | Hide |
| PartNumber | false | Unavailable | Hide |
| Jedec | false | Unavailable | Hide |

Do **not** add `OpportunityShareApplicable`. Defect share hiding is `axis == Defect` in presentation code (and PDF/CSV writers). Overall KPI DPMO is unchanged.

Helper on the server:

```csharp
private static bool OpportunitiesApplicableForAxis(ParetoAxis axis)
```

`OpportunityForGroup` returning 0 means a real zero only when that helper is true.

**Placement.** `OpportunitiesApplicable` goes **last** in the positional record, after `IsVitalFew`. System.Text.Json serialises positional records in declaration order, so appending keeps existing JSON property order and lets the CSV / Excel column be appended rather than inserted (positional consumers keep working). Snapshots and the literal CSV header assertion in `DpmoAndParetoEndpointsTests` are then a pure append.

**Others row.** The synthetic Others row is constructed separately in `BuildRows` Step 5 and must carry the same flag as the visible rows (from the axis). Do not leave it defaulting to `false`.

**Defect-axis Others denominator (5.5).** On the Defect axis, Others uses `totalOpportunities` once (not the overflow sum). `OpportunitySharePercent` for Others is then 100 (and hidden like every Defect-axis share, per PAR-006); `DpmoPpm` and `WeightedScore` use the overall denominator like every other Defect row. Non-Defect axes keep summing per-group opportunities.

### 6.2 Unsupported rate weight: normalize and echo

When `Weight` is Dpmo or Ppm and `OpportunitiesApplicableForAxis(Axis)` is false:

1. Rank as Count (`ScoreFor` uses defect count).
2. Set `ParetoResult.Weight` to the **applied** weight (`Count`), not the requested weight.
3. Do not return 400 in this initiative.

SPA: when the user picks an object-level axis, snap weight to Count (already true on drill; also do it on the Axis select and on tile config if weight+axis are illegal). Server remains the source of truth for non-UI callers.

**Presentation keys off the echo, not the request.** `ParetoChart` currently has no `weight` prop, and `pareto.tsx` passes `axis={axis}` from the URL rather than from `data`. A bookmarked `?weight=Dpmo&axis=PartNumber` would otherwise show “DPMO” selected while the server ranked by Count. Therefore:

- `ParetoChart` gains a required `weight: ParetoWeight` prop; both `pareto.tsx` and `ParetoTile` pass `result.weight`.
- Axis titles, tooltip labels, table column visibility, and export titles derive from `result.weight`.
- `validateParetoSearch` normalises an object-level axis + rate weight pair to `weight: "Count"` so the form never displays the illegal pair on load.
- The Weight select shows the applied value (Count) with DPMO/PPM disabled while an object-level axis is selected.

### 6.3 Rate charts: no cumulative line

| Weight | Bar value | Left axis | Cumulative series | Bar colour | Table / tooltip / PDF-table cumulative % and ★ |
|---|---|---|---|---|---|
| Count | `defectCount` | Defects | Cumulative defect % | vital-few / trivial-many / Others | Shown |
| Dpmo | `weightedScore` | DPMO | **Off** | uniform (Others still grey) | **Hidden** |
| Ppm | `weightedScore` | PPM | **Off** | uniform (Others still grey) | **Hidden** |

Do not plot a cumulative-defect line against rate bars. Preserve server row order; do not re-sort in the client.

**Why the extra columns.** The server computes `CumulativePercent` as cumulative *defect share along the active sort order* and derives `IsVitalFew` from it. Under a rate weight that is cumulative defect share along DPMO order — not a Pareto cumulative in any meaningful sense. Hiding the line while still colouring bars by `isVitalFew` and showing Cumulative % / ★ in the table would colour bars by a metric the chart just declared meaningless and would break the accessibility rule “table holds the same facts as the chart”. So in rate mode the whole vital-few / cumulative family is hidden in first-party presentation. The raw fields stay in JSON / CSV / Excel for compatibility; the dedicated Excel / CSV writers keep emitting them unchanged.

The dashed threshold `markLine` is attached to the cumulative **line series** in `ParetoChart.tsx`; removing that series in rate mode removes the mark line with it. Do not add a second guard. The legend must list only the bar series in rate mode.

Shared presentation helper (SPA and, equivalently, PDF SVG):

```ts
function paretoBarPresentation(weight: ParetoWeight): {
    barValue: "defectCount" | "weightedScore";
    showCumulative: boolean;   // line + threshold + table/tooltip cumulative column
    showVitalFew: boolean;     // bar colouring + ★ column; equal to showCumulative today
    leftAxisLabelKey: string;  // pareto.chart.yLeftDefects | yLeftDpmo | yLeftPpm
}
```

`ParetoChartSvg.Bar.Count` is `long` today; it becomes a `double` value so the same `Build(result, threshold)` can plot `WeightedScore`. `Build` already receives `result.Weight`, so no signature change is required.

### 6.4 Filter parity transport: one strict tile filter builder shared by export and canvas

Do not design a full POST of `ParetoFilter` and do not invent a second encoding of `FilterRequest` on the standalone GET in this initiative.

Extract the filter-building body of `RunParetoForTileAsync` into one helper used by:

- saved-report export (current caller)
- a new authenticated POST used only by the canvas tile, e.g. `POST /api/reports/pareto/from-tile`

Helper signature — do **not** take `PanelYieldFilter`, which would force the POST handler to fabricate one:

```csharp
internal static (ParetoFilter? Filter, string? Error) TryBuildParetoTileFilter(
    DateRange window,
    IReadOnlyCollection<int>? machineIds,
    IReadOnlyCollection<int>? productIds,
    string? configJson)
```

Request body (names can match existing export query fields):

```text
sourceId
startUtc / endUtc (or the same window object the canvas already has)
machineIds
productIds
configJson   // the tile's persisted JSON, including filters — passed verbatim from TileProps.config
```

The helper:

- parses axis / numerator / opportunity / weight / topN / vital-few from config (`ParseParetoTileConfig`, unchanged)
- maps Day/Shift tile axis back to Defect (unchanged)
- applies the **strict** tile filter parser described below

The tile then stops calling GET `runParetoReport` for canvas execution. Standalone `/report/pareto` keeps GET + URL search params.

React Query key for the tile is `["canvas", "pareto", sourceId, startUtc, endUtc, machineIds, productIds, configJson]` where `configJson` is the raw persisted string (already stable; no canonicalisation needed). Extract this into an exported `paretoTileQueryKey(filters, configJson)` so it is unit-testable.

**Strict tile filter parsing (replaces the fail-open parser for both callers).** Add

```csharp
internal static bool TryParseTileFilters(string? configJson, out FilterRequest? filters, out string? error)
```

Rules:

- No `filters` key, or `filters: []` → `filters = null`, success (unfiltered, as today).
- `filters` present but not an array → error.
- Any clause that is not an object, lacks `field` / `operator`, or names an unknown enum member → error naming the clause index.
- `FilterValidator.Validate(request)` invalid → error carrying the validator message.

Never drop a clause and continue. The existing lenient `ParseTileFilters` is deleted once both callers use the strict one (panel-yield export uses it too; it gets the same strict behaviour — that is intentional and covered by an export test).

Error surfacing:

- `POST /api/reports/pareto/from-tile` → 400 ProblemDetails (`title: "Invalid tile filter: …"`), same shape as the other report endpoints.
- Saved-report export (XLSX / PDF / CSV) → the tile renders as an **error section** (title + the message) in place of its data, the same way an unsupported tile type renders a placeholder today; the export as a whole still succeeds. The cover sheet status column says `invalid filter`.

The `TileConfigForm` already has `isClauseValid`; it must refuse to save a tile whose clauses are invalid so the strict server path is a backstop, not the normal UX.

### 6.5 Export cells for unavailable metrics

Add `OpportunitiesApplicable` to dedicated CSV and Excel.

When false: blank `OpportunityCount`, `OpportunitySharePercent`, and `DpmoPpm` (CSV) / blank or text `N/A` (Excel, keeping numeric types when applicable). PDF table uses `—` / localized `N/A` plus a one-line footnote on object-level axes.

Do not keep a second “legacy always-zero” column.

Defect axis: still fill opportunity count and DPMO; omit or dash opportunity share in the sheet/PDF.

Saved-report compact Pareto export must not print DPMO 0 for object-level rows without communicating unavailability (same footnote or N/A).

Scope note for the compact saved-report surfaces (verified in tree): the compact **PDF** (`ReportPdfRenderer.ComposePareto`) already prints only Count / Share % / Cumul % per row and no row DPMO, so it needs no availability change — only the rate-mode column hiding from 6.3. The compact **XLSX** (`WriteParetoSheet`) and **CSV** (`WriteParetoCsv`) in `ReportEndpoints.ReportExport.cs` do print `DpmoPpm` per row and are the two writers that need the N/A rule.

### 6.6 Defaults in this initiative

Do **not** migrate saved tiles or change fallback opportunity flavor.

Document, do not “fix”:

| Surface | Axis | Numerator | Opportunity | Weight | Top N |
|---|---|---|---|---|---|
| Standalone form | Defect | Real | **All** | Count | unset (all buckets) |
| Canvas tile default | Defect | Real | **Components** | Count | **10** |
| C# `ParetoFilter` default | — | Real | **All** | Count | null |

Tile compactness (Top N 10, Components) is a presentation default. Standalone All / no cap is a different presentation default. Pretending they are one config is out of scope.

### 6.7 `ParetoResult` echoes the vital-few threshold

Add `double VitalFewThresholdPercent` to `ParetoResult` (additive, after `SkipExcludedCards`, default 80 so existing positional construction in tests keeps compiling).

Reason: once WP3 makes the server parse `configJson`, the tile no longer has a trustworthy SPA-side threshold for the chart's dashed mark line. Today `ParetoTile` does not pass one at all, so the chart draws 80 while the server flags rows at `cfg.vitalFewThreshold` — a silent mismatch whenever a tile sets anything other than 80. Both `pareto.tsx` and `ParetoTile` pass `result.vitalFewThresholdPercent` to the chart; the dedicated PDF path can use it too instead of threading `built.Filter.VitalFewThresholdPercent`.

---

## 7. Delivery

Three ordered work packages, each leaving the repo buildable. Prefer three PRs. Do not fold WP2/WP3 into an unreviewable dump.

1. Characterization tests (no production change)
2. Metric availability + chart/ranking alignment + presentation
3. Canvas from-tile execution parity

WP2 before WP3 is intentional: once filters actually reach the report, object-level tiles must already show N/A rather than a newly correct (and still misleading) zero DPMO.

---

# Work Package 1: Characterization tests

## Objective

Lock intentional behavior and reproduce the confirmed defects before changing `ParetoReport` or the tile.

## Files

- `tests/Nieweb.Reports.Tests/ParetoReportTests.cs`
- `tests/Nieweb.Api.Tests/Endpoints/DpmoAndParetoEndpointsTests.cs`
- `tests/Nieweb.Api.Tests/Endpoints/ReportExportEndpointTests.cs`
- `src/Nieweb.Web/src/charts/ParetoChart.test.tsx`
- New: `src/Nieweb.Web/src/components/canvas/tiles/ParetoTile.test.tsx` (or adjacent)
- `src/Nieweb.Web/src/components/canvas/tiles/ParetoTile.tsx` — **refactor only**: extract the inline `queryKey` into an exported `paretoTileQueryKey(...)` so 1.6 can assert against it. No behaviour change.
- Optional: `tests/Nieweb.Api.Tests/Pdf/ParetoChartSvgTests.cs` snapshot of current count-height bars

## Tasks

### 1.1 Object-level zeros (current contract)

For `ReferenceDesignator`, `PartNumber`, and `Jedec`:

- Seed cards with nonzero component tests and at least one defective tested object.
- Assert overall `OpportunityCount > 0`.
- Assert each emitted row has `OpportunityCount == 0` and `DpmoPpm == 0`.
- After WP2 these tests flip to `OpportunitiesApplicable == false` and stop treating 0 as the user-visible contract.

### 1.2 True-zero supported denominator

Product axis, cards for that product with `NbOfTestsOnComp = 0` (and paste 0). Assert the product row remains a real group with `OpportunityCount == 0` and, after WP2, `OpportunitiesApplicable == true`.

### 1.3 Multi-bit occurrences

One tested object, two selected bits. Assert:

- overall tested-object count +1
- overall defect-bit count +2
- a non-Defect axis bucket has defect count 2
- Defect axis has one occurrence in each bit group

### 1.4 Tie order

Equal `WeightedScore`, assert sort by ordinal `GroupKey` for Count (and DPMO if the same comparator is used).

### 1.5 Vital-few crossing

First category already above the threshold: only that row is vital-few.

### 1.6 Tile query vs export filters (current defect)

Fixture: tile `configJson` with a nonempty `filters` array that excludes real rows.

- Export path (`RunParetoForTileAsync` / saved-report Pareto) **applies** the filter (smaller overall defect count).
- On-screen path (today: GET `ParetoSearch` without filters) **does not**.

After WP3 this becomes a parity test (same overall counts and row keys).

Also assert (via the extracted `paretoTileQueryKey`) that the key today omits `filters` and `vitalFewThreshold` — two configs differing only in those produce the same key. WP3 flips this.

### 1.7 Chart uses defectCount today

A DPMO-ranked fixture where the highest DPMO row is not the highest defect-count row. Assert the SPA chart (and PDF SVG if cheap) currently plots defect counts. WP2 flips the assertion to `weightedScore` and no cumulative series.

### 1.8 Existing suites stay green

Count ranking, DPMO ranking reversal, Top N / Other, 80% vital-few, auth, skip/NOGO, Day/Shift, CSV/XLSX/PDF generation.

### 1.9 Defect-axis Others denominator (current defect, 5.5)

Fixture: Defect axis, at least four active bits, `TopN = 2`, nonzero card opportunities. Assert today:

- `OthersBucket.OpportunityCount == (activeBits − 2) × Overall.OpportunityCount`
- `OthersBucket.OpportunitySharePercent > 100`
- `OthersBucket.DpmoPpm < 1e6 × OthersBucket.DefectCount / Overall.OpportunityCount`

WP2 flips these to `OpportunityCount == Overall.OpportunityCount`, share 100, DPMO on the overall denominator. Also run the same fixture with `Weight = Dpmo` and assert Others' `WeightedScore` matches.

### 1.10 Tile filters fail open (current defect, 5.6)

Three saved-report export fixtures (XLSX is enough), each with a tile `configJson`:

- `filters` contains one valid `PartNumber NotLike` clause and one clause with `"operator": "Lke"` → today the export runs with **only** the valid clause (assert overall defect count equals the one-clause run, not the unfiltered run).
- `filters` contains a single clause that fails `FilterValidator` (e.g. `Between` with one value) → today the export runs **unfiltered**.
- `filters` is a string, not an array → today the export runs unfiltered.

WP3 flips all three to an error section / 400.

### 1.11 Tile chart threshold mismatch (current defect, 6.7)

`ParetoTile` render test with `configJson` `{"vitalFewThreshold": 60}`: assert the chart today receives `vitalFewThresholdPercent` 80 (default) while the request carries 60. WP2/WP3 flip to 60 from `result.vitalFewThresholdPercent`.

## Acceptance

- Each confirmed defect has a failing-to-the-user characterization (or an explicit “documents current zeros” test).
- No production behavior change (the `paretoTileQueryKey` extraction is a pure refactor with identical key contents).
- Fixtures distinguish card opportunities, tested objects, and defect-bit occurrences.

---

# Work Package 2: Availability, ranking safety, charts, exports

## Objective

Stop presenting unavailable denominators as measured zero; make charts match ranking; hide Defect-axis opportunity share; fix the TypeScript weight contract.

## Files

- `src/Nieweb.Reports/ParetoReportDtos.cs` (`ParetoRow`, XML on `OpportunityCount`)
- `src/Nieweb.Reports/ParetoReport.cs`
- `src/Nieweb.Web/src/api/pareto.ts`
- `src/Nieweb.Web/src/routes/pareto.search.ts` (object-level weight snap if not already complete)
- `src/Nieweb.Web/src/routes/pareto.tsx`
- `src/Nieweb.Web/src/charts/ParetoChart.tsx` + tests
- `src/Nieweb.Pdf/ParetoChartSvg.cs` + `ParetoPdfRenderer.cs` + tests
- `src/Nieweb.Api/Endpoints/ReportEndpoints.Pareto.cs` (CSV/XLSX row writer)
- `src/Nieweb.Api/Endpoints/ReportEndpoints.ReportExport.cs` (compact saved-report Pareto)
- `src/Nieweb.Web/src/i18n/locales/en.ts`, `fr.ts`, `bundle.ts`
- WP1 tests updated to the new contract

## Tasks

### 2.1 Domain contract

Add `OpportunitiesApplicable` to `ParetoRow` **as the last positional parameter** (after `IsVitalFew`; see 6.1 Placement). Call sites to update:

- `ParetoReport.BuildRows` — both the visible-row constructor and the Others constructor in Step 5
- `tests/Nieweb.Api.Tests/Pdf/ParetoChartSvgTests.cs` — the `Row(...)` helper builds `ParetoRow` positionally
- `tests/Nieweb.Reports.Tests/Snapshots/Pareto_*.expected.json` (5 files) — append the property
- `DpmoAndParetoEndpointsTests` — the literal CSV header string

Implement `OpportunitiesApplicableForAxis`. When building rows:

- set the flag from the axis on visible rows **and** on the Others row
- keep emitting numeric 0 into opportunity/DPMO when false (compatibility)
- when requested weight is Dpmo/Ppm and the flag is false, score as Count and echo `Weight = Count` on `ParetoResult`
- Defect axis Others: `othersOpps = totalOpportunities` (5.5), not the overflow sum

Add `VitalFewThresholdPercent` to `ParetoResult` (6.7).

Fix XML that currently describes the old contract, in all three places: `ParetoRow.OpportunityCount` (“tested-object rows”), `ParetoWeight.Dpmo` (“Buckets with zero opportunities emit 0”), and the `ParetoReport` class remarks (“their rate is suppressed (0)”). State card test counts via `NbOfTestsOnComp` / `NbOfTestsOnPads`, that object-level axes have no per-group denominator and carry `OpportunitiesApplicable = false`, and that a rate weight on such an axis is applied as Count.

### 2.2 JSON + TypeScript

```ts
export type ParetoRow = {
    // existing fields…
    opportunitiesApplicable: boolean;
};

export type ParetoResult = {
    // …
    weight: "Count" | "Dpmo" | "Ppm";
    vitalFewThresholdPercent: number;
};
```

`ParetoChart` props gain `weight: ParetoWeight` (required). Both callers pass `result.weight` and `result.vitalFewThresholdPercent`; `pareto.tsx` also switches `axis={axis}` to `axis={data.axis}` so chart labelling cannot drift from the data it is drawing.

Shared formatter, used by table, tooltip, and any tile KPI that shows row DPMO:

```ts
formatApplicableMetric(value, applicable, formatter): string
```

Inapplicable → localized `N/A` / `—`, never `"0"`.

### 2.3 Standalone UI

- Table: N/A for opportunity, share, and DPMO when `!opportunitiesApplicable`. In `ParetoTable` keep the column `accessor` numeric-or-`null` (so `DataTable` sorting stays typed) and put the N/A text in `formatter` **and** `csvFormatter`; the table's own client-side CSV download must not emit `0` for inapplicable cells either.
- Defect axis: N/A or omit opportunity share even when applicable.
- Rate mode (`result.weight` is Dpmo/Ppm): hide the Cumulative % and Vital-few columns (6.3).
- Tooltip / helper under the table: object-level axes have no per-group opportunity denominator; overall KPIs still use card counts.
- Axis change onto ref des / part / JEDEC forces Count (same as drill). `validateParetoSearch` applies the same normalisation on load.
- Weight control: disable DPMO/PPM on those axes and show Count as selected.
- Accessible name of N/A cells must not be announced as zero.

Keep overall KPI opportunity and DPMO as now.

### 2.4 Chart + PDF SVG

Use 6.3. Count mode appearance stays as today. DPMO/PPM: left axis and tooltip named DPMO/PPM, bars = `weightedScore`, no cumulative series (which also removes the threshold mark line attached to it), legend lists only the bar series, uniform bar colour (Others stays grey), tooltip omits the cumulative line. Chart mode comes from the `weight` prop (= `result.weight`).

Mirror the rule in `ParetoChartSvg.cs` (same `Build(result, threshold)` signature; `Bar.Count` becomes a `double`) so a dedicated PDF titled DPMO does not draw count-height bars, and drop the polyline / circles / dashed threshold / “Cumulative %” legend entry in rate mode. The dedicated PDF's row table (`ParetoPdfRenderer.ComposeRows`) hides its Cumulative % and ★ columns in rate mode for the same reason.

### 2.5 Dedicated + saved-report exports

CSV header gains `OpportunitiesApplicable` **appended after `IsVitalFew`**. Blank the three inapplicable numeric cells. Excel: applicability column appended likewise; numeric cells only when applicable. PDF: `—` / N/A + footnote when any row is inapplicable (object-level axis).

Defect axis: do not print opportunity share as 100% in the table (leave the column blank or “—”, or drop the column for that axis). Keep DPMO. The Others row on the Defect axis now shows the overall denominator (5.5).

Compact saved-report Pareto: same N/A rule for DPMO in `WriteParetoSheet` (XLSX) and `WriteParetoCsv` (CSV). The compact PDF needs only the rate-mode column hiding (see 6.5 scope note).

### 2.6 i18n

English and French for:

- N/A / em dash
- object-level denominator footnote
- DPMO/PPM axis titles
- removal of cumulative series in rate mode (legend must not still list it)

### 2.7 Tests

Update 1.1 to assert `OpportunitiesApplicable == false` and that JSON still contains the numeric zeros.

Add:

- Product true-zero remains applicable
- PartNumber + requested Dpmo → result `Weight == Count` and sort by defect count
- Others row on an object-level axis has `OpportunitiesApplicable == false`
- Defect axis + TopN: Others `OpportunityCount == Overall.OpportunityCount`, DPMO on the overall denominator, both under Count and Dpmo (flips 1.9)
- Defect axis: DPMO populated, share not shown in CSV/PDF
- `ParetoResult.VitalFewThresholdPercent` echoes the filter value (JSON snapshot)
- Chart unit tests: bar field, `showCumulative`, `showVitalFew`, legend contents, uniform colour in rate mode, `weight` prop drives mode (not any request state)
- `validateParetoSearch` normalises `axis=PartNumber&weight=Dpmo` to Count
- `ParetoTable` rate mode hides cumulative / vital-few columns; client CSV emits N/A not 0
- SVG test: DPMO fixture uses weighted scores and contains no polyline
- Snapshots: 5 `Pareto_*.expected.json` files updated by append only

## Acceptance

- Object-level rows never **display** measured zero DPMO.
- API consumers can tell unavailable from true zero.
- Rate ranking cannot run on an axis with no row denominator (it becomes Count), and the UI shows what was applied.
- Defect-axis DPMO still uses the overall denominator, including on the Others row.
- Defect-axis opportunity share is not shown as a production partition.
- Count charts unchanged; rate charts match ranking and have no cumulative line, no vital-few colouring, and no cumulative / vital-few columns alongside them.
- Overall KPIs unchanged.
- JSON property order and CSV column order for existing fields are unchanged (new fields appended).

---

# Work Package 3: Canvas screen / export parity

## Objective

The on-screen tile and saved-report export run the same `ParetoFilter`.

## Files

- `src/Nieweb.Api/Endpoints/ReportEndpoints.ReportExport.cs` (extract helper; error section for invalid tile filters)
- `src/Nieweb.Api/Endpoints/ReportEndpoints.TileConfig.cs` (strict `TryParseTileFilters`; delete lenient `ParseTileFilters`)
- `src/Nieweb.Api/Endpoints/ReportEndpoints.Pareto.cs` (or a small new partial) for `POST /api/reports/pareto/from-tile`
- `src/Nieweb.Pdf/ReportPdfRenderer.cs` + the XLSX / CSV report writers (render the invalid-filter error section)
- `src/Nieweb.Web/src/api/pareto.ts`
- `src/Nieweb.Web/src/components/canvas/tiles/ParetoTile.tsx`
- `src/Nieweb.Web/src/components/reportConfig/TileConfigForm.tsx` (refuse to save invalid clauses)
- API tests in `ReportExportEndpointTests.cs` / `DpmoAndParetoEndpointsTests.cs`
- `ParetoTile.test.tsx`

## Tasks

### 3.1 Shared tile filter builder

Move `RunParetoForTileAsync`'s `ParetoFilter` construction into `TryBuildParetoTileFilter(window, machineIds, productIds, configJson)` (signature in 6.4 — it takes the window and id lists, **not** `PanelYieldFilter`). Export and the new POST both call it. Day/Shift → Defect guard stays in that helper. It returns an error instead of a filter when the strict filter parser rejects `configJson`.

### 3.2 Strict tile filters

Implement `TryParseTileFilters` per 6.4 and switch both `RunParetoForTileAsync` and `RunPanelYieldForTileAsync` to it. Saved-report export renders an error section for a tile whose filters are invalid; the export still completes. Cover-sheet status reads `invalid filter`.

### 3.3 POST `/api/reports/pareto/from-tile`

Same auth as GET Pareto (the report route group already has `RequireAuthorization()`; bearer auth, no antiforgery needed). Body carries source, window, machine/product ids, and `configJson`. 400 on missing source/window or invalid filters, same problem shape as other report endpoints. Response is existing `ParetoResult` (now including `opportunitiesApplicable`, applied `weight`, and `vitalFewThresholdPercent` from WP2). Call `resultCache.Store` after the run like the GET does, so a subsequent export reuses the pass (the cache key is the JSON of the full `ParetoFilter`, so filters are already part of it).

Do **not** replace standalone GET.

### 3.4 Tile client

- `runParetoFromTile(...)` POST helper; body `configJson` is `TileProps.config` verbatim.
- `ParetoTile` uses it instead of `runParetoReport(search)`. The SPA-side `parseParetoTileConfig` is no longer consulted for execution; presentation values (`weight`, `vitalFewThresholdPercent`, `axis`) come from the result.
- `paretoTileQueryKey` (extracted in WP1) now includes the raw `configJson` string and drops the individual `cfg.*` entries.

### 3.5 Parity tests

Same `configJson` with a filter that **removes** rows (e.g. `PartNumber NotLike` a seeded part):

- POST from-tile vs `RunParetoForTileAsync` / saved-report Pareto
- Compare overall tested-object count, opportunities, defect count, row keys, order, defect counts, `opportunitiesApplicable`, cumulative %, Other bucket, `weight`, `vitalFewThresholdPercent`

A dropped filter must not be able to pass this test.

Also:

- changing only `filters` or `vitalFewThreshold` in `configJson` changes the query key (flips 1.6);
- the three fail-open fixtures from 1.10 now yield 400 (POST) and an error section (export), and the surviving-clause case does **not** run partially;
- a legacy tile with `axis: "Day"` renders the Defect axis via POST (same as export today) — documents the on-screen behaviour change in §10.

## Acceptance

- One tile configuration → equivalent screen and export results.
- No filters applied only on export.
- Standalone URL Pareto still uses GET.
- Invalid generic filters → readable 400 on POST, error section on export; never a silently wider population.
- Panel-yield tiles get the same strict filter behaviour.

---

## 8. Pull requests

1. `test(pareto): characterize unavailable denominators, chart/rank mismatch, and tile filter drop`
2. `fix(pareto): mark row opportunity applicability and align rate charts`
3. `fix(pareto): run canvas tiles through the same filter builder as export`

Each PR notes:

- Finding IDs (PAR-001, PAR-002, PAR-003, PAR-006, plus plan §5.5 Others denominator and §5.6 fail-open filters, as applicable)
- Before/after
- Compatibility (additive JSON fields appended; Weight echo may change from Dpmo to Count on object-level axes; Defect-axis Others DPMO changes value; invalid tile filters now error instead of running wider)
- Tests added
- Remaining limitations (no per-group object-level denominator; All vs Components defaults still differ by surface)

---

## 9. Acceptance matrix (this initiative)

### Domain

- Count ranks by defect occurrences.
- Requested DPMO on object-level axes is applied as Count and echoed.
- Multi-bit object → one occurrence per selected bit.
- Overall opportunities remain card test counts.
- Object-level rows: `opportunitiesApplicable == false`.
- True-zero product/machine/day/shift denominators: applicable, numeric 0.
- Vital-few includes the crossing row.
- Ties by ordinal group key.
- Defect-axis Others row uses the overall denominator once.
- Others row carries the axis's applicability flag.

### API

- JSON includes `opportunitiesApplicable` (appended) and `vitalFewThresholdPercent`.
- `weight` on the result is the applied weight.
- from-tile POST and saved export share one filter builder.
- Invalid tile filters → 400 (POST) / error section (export); no partial or unfiltered run.
- Auth unchanged.

### Standalone UI

- Overall KPIs remain on object-level axes.
- Row opportunity/DPMO: N/A vs 0 distinguished (table, tooltip, client CSV).
- Defect-axis share hidden.
- Axis/weight cannot stay in a meaningless pair in the form, including on URL load.
- Chart / table mode follows `result.weight`.

### Canvas

- Saved generic filters affect on-screen data.
- Query key includes the raw `configJson` (hence filters and vital-few).
- Screen and export match, including vital-few threshold.

### Charts

- Count: `defectCount` + cumulative %, vital-few colouring.
- Rate: `weightedScore`, no cumulative series, no threshold line, uniform colour, no cumulative / ★ columns beside it.
- SPA and PDF SVG agree.

### Exports

- CSV/Excel/PDF distinguish unavailable from zero.
- Compact saved-report XLSX / CSV do not show unexplained DPMO 0.
- Defect-axis share not presented as 100% partition; Defect-axis Others DPMO is correct.
- New columns appended, never inserted.

### Accessibility

- Table holds the same facts as the chart.
- Unavailable values are not announced as zero.

---

## 10. Compatibility

**Additive.** Clients that ignore `opportunitiesApplicable` and `vitalFewThresholdPercent` still see zeros / the old shape. Both are appended so existing JSON property order and CSV/Excel column positions are unchanged. First-party surfaces must honor the flag in the same release.

**Weight echo.** Callers that requested `weight=Dpmo` with `axis=PartNumber` now receive `weight: Count` and Count ordering. That is a behavior change for a previously meaningless ranking, not a schema break.

**Defect-axis Others values change.** `OpportunityCount`, `OpportunitySharePercent`, `DpmoPpm`, and (under a rate weight) `WeightedScore` on the Others row of a Defect-axis Pareto with `TopN` change to the correct overall-denominator values. Any consumer that had been reading the old N× figure was reading a bug.

**Invalid tile filters now fail.** A saved tile whose `filters` array contains a malformed or invalid clause previously exported with the bad clause dropped (or entirely unfiltered). It now produces a 400 on the canvas POST and an error section in the saved-report export. Panel-yield tiles get the same behaviour. Authors fix the tile in the editor, which refuses to save invalid clauses going forward.

**Legacy Day/Shift tiles on screen.** A stored tile with `axis: "Day"` or `"Shift"` previously ran the GET with that axis on screen (UTC buckets, or a 400 for Shift) while exporting as Defect. After WP3 both surfaces run Defect. The editor has not offered those axes for tiles for some time.

**No saved-tile rewrite.** Existing `configJson` opportunity/topN values stay as stored. Changing global fallbacks is a later initiative.

**Later (not this plan):** nullable opportunity/DPMO, GET encoding of generic filters for standalone bookmarks, unifying All vs Components, POST of a full `ParetoFilter`.

---

## 11. Definition of done

Done when:

- PAR-001: unavailable vs zero is explicit on JSON, table, chart tooltip, CSV, Excel, PDF, and compact saved-report Pareto.
- PAR-002: bar geometry matches ranking weight; rate mode has no cumulative-defect line; SPA and PDF agree.
- PAR-003: canvas tile and export share one tile filter builder; a material generic filter changes both.
- PAR-006: Defect-axis opportunity share is not shown as a production partition.
- §5.5: Defect-axis Others row uses the overall denominator once; covered by a Defect-axis TopN test under both Count and Dpmo.
- §5.6: tile filters fail closed on both the canvas POST and the saved-report export; the three fail-open fixtures from WP1 1.10 error instead of running wider.
- Object-level DPMO requests normalize to Count and echo it, and every first-party surface renders from the echoed `weight`.
- Rate mode hides the cumulative / vital-few family consistently across chart, tooltip, table, and PDF.
- `ParetoResult` echoes `vitalFewThresholdPercent`; tile chart threshold equals the server's.
- WP1–WP3 tests pass together with existing Pareto suites.
- XML no longer calls Pareto opportunities “tested-object rows,” and `ParetoWeight.Dpmo` / the `ParetoReport` remarks no longer describe suppressed-zero rates.
- New JSON fields and export columns are appended, never inserted.
- Subpanel is still not a Pareto axis.
- The Subpanel-removal baseline commit exists on the branch before the WP1 commit.

Explicit remaining limitations, written on the last PR:

- No per-group opportunity model for ref des / part / JEDEC (needs placement or library data).
- Standalone default opportunity is All; tile default is Components; Top N 10 is tile-only.
- Standalone GET still does not accept generic `FilterRequest` (only the tile POST / export path does).
