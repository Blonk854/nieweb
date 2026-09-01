# Plan: DPMO Trend by line

**Status:** locked 2026-07-31, not started.
**Branch at time of writing:** `phase-c` @ `48d4773` (6 commits ahead of
`origin/phase-c`, all local).
**Baseline to protect:** .NET **716/716** green, SPA vitest **538/538** green.

Add a **DPMO Trend** report that mirrors the existing FPY Trend: DPMO over
time (day / week) per AOI line, rendered across **both** pre- and
post-reflow sources at once. New nav entry, new route
`/report/dpmo-trend`.

This is deliberately *not* a pure copy of FPY Trend — see
[§0 Decisions](#0-decisions) and [§5 Landmines](#5-landmines-do-not-rediscover-these).

---

## 0. Decisions

Agreed 2026-07-31:

| Question | Decision |
|---|---|
| Numerator toggle (Real / AOI / Dummy) | **Instant** — all three carried in one response, no refetch. Mirrors how FPY Trend carries all three FPY flavours. |
| Opportunity toggle (All / Components) | **Refetches** — it changes the denominator *and* which object types count. |
| Paste opportunity | **Hidden entirely for now.** `Nb_Of_Tests_On_Pads` is pre-reflow only (paste is a pre-reflow stage, gated on `Capabilities.PastePrintMetrics`), so a Paste trend would have no post-reflow data at all. |
| Scope of first pass | **Phase 1 only**, then review. |

---

## 1. Phase 1 — data layer: the `DefectsOnly` predicate

### Why this is first, not last

Pre-reflow `TESTED_OBJECT` is **not** physically defect-only — the v4.3.1
DB emits one row per tested object, not one per defect. `DpmoTableReport`
pass 2 streams that table with **no predicate** and a **1 000-row page
size** (`src/Nieweb.Reports/DpmoTableReport.cs` ~L195).

That is survivable for the single-source DPMO table. Across all lines ×
two DBs × a week it is the exact performance cliff FPY Trend fell off, and
the fix there was the `SkipInputsOnly` lever. We add the equivalent lever
for the defect numerator **before** building anything on top of it.

### Changes

**1. `src/Nieweb.DataSources/Queries.cs`** — add to `TestedObjectQuery`,
immediately after `SkipInputsOnly` (~L60):

```csharp
public bool DefectsOnly { get; init; }
```

XML doc must state:

- Restricts the stream to rows carrying at least one defect bit.
- **Exact-parity** for defect-counting callers: rows with no bits set
  popcount to zero, so pruning them cannot change a numerator.
- Do **not** set it when the caller needs a count of tested objects — the
  pruned stream can no longer answer "how many objects were inspected".
- Opportunity denominators must come from `CARDS.Nb_Of_Tests_On_Comp` /
  `_On_Pads` regardless, never from a `TESTED_OBJECT` row count.
- Defaults to `false` so every existing caller keeps the full stream.

**2. `src/Nieweb.DataSources.Sql/SqlServerAoiSourceBase.cs`** — in
`BuildTestedObjectsQuery`, directly after the `if (q.SkipInputsOnly)` block
(~L1047):

```csharp
if (q.DefectsOnly)
{
    sb.AppendLine().Append(
        $"  AND (t.Error_Table <> 0 OR {arColumn} <> 0)");
}
```

> **CRITICAL.** Use the already-computed `arColumn` local (~L989). Do
> **not** write a literal `t.Error_Table_AR`. Pre-reflow v4.3.1
> `TESTED_OBJECT` **lacks** that column; `arColumn` already degrades to
> `t.Error_Table` there via `HasTestedObjectErrorTableAr`, which keeps the
> predicate valid on both schemas. A literal would emit SQL that only runs
> against post-reflow.

`ListTestedObjectsForSubpanelAsync` (~L683) has its own copy of `arColumn`
but takes no `TestedObjectQuery` — leave it alone.

**3. `src/Nieweb.DataSources.Fake/FakeAoiSource.cs`** — `FilterTestedObjects`
(~L345) currently honours neither `SkipInputsOnly` nor `DefectsOnly`. Add
`DefectsOnly` filtering (`ErrorTable != 0 || ErrorTableAr != 0`) so the
fake models the real stream shape and Phase 2's tests mean something.

**4. Test seam.** `BuildTestedObjectsQuery` is `private`.
`src/Nieweb.DataSources.Sql/Nieweb.DataSources.Sql.csproj` already carries
`<InternalsVisibleTo Include="Nieweb.DataSources.Sql.Tests" />`, so change
it to `internal` and add
`tests/Nieweb.DataSources.Sql.Tests/TestedObjectQuerySqlTests.cs`:

- `DefectsOnly = false` → SQL contains no `Error_Table <> 0`
- `DefectsOnly = true`, post-reflow shape → contains
  `(t.Error_Table <> 0 OR t.Error_Table_AR <> 0)`
- `DefectsOnly = true`, pre-reflow shape
  (`HasTestedObjectErrorTableAr = false`) → contains
  `(t.Error_Table <> 0 OR t.Error_Table <> 0)` and **not**
  `Error_Table_AR`
- `DefectsOnly` + `SkipInputsOnly` together → both predicates present

A small test-only subclass of `SqlServerAoiSourceBase` will be needed to
flip `HasTestedObjectErrorTableAr` without opening a connection.

### Verification

```powershell
dotnet build Nieweb.slnx
dotnet test  Nieweb.slnx     # baseline 716/716
```

No existing caller sets `DefectsOnly`, so every existing test must be
unaffected. If anything moves, the predicate is wrong.

---

## 2. Phase 2 — `DpmoTrendByLineReport`

`src/Nieweb.Reports/DpmoTrendDtos.cs`:

- `DpmoTrendFilter` — Window, Bucket, SiteTimeZone, Opportunity,
  MachineIds, ProductIds, SkipExclusion, SkipConfig, SkipStatuses,
  ExcludeNogo
- `DpmoTrendBucket`, `DpmoTrendPoint`, `DpmoTrendLine`, `DpmoTrendResult`

Each cell carries the opportunity count **plus all three numerators**, so
the numerator toggle is display-only:

```
(long OpportunityCount, long DefectsAoi, long DefectsReal, long DefectsDummy)
```

- `Aoi`   = popcount(`ErrorTable`)
- `Real`  = popcount(`ErrorTableAr`)
- `Dummy` = popcount(`ErrorTable & ~ErrorTableAr`)

`src/Nieweb.Reports/DpmoTrendByLineReport.cs` is `DpmoTableReport`'s
**two-pass** design re-keyed by `(machineId, bucketIndex)`:

- **Pass 1 (denominator)** — `StreamCardsAsync`, sum
  `Nb_Of_Tests_On_Comp`, adding `_On_Pads` only when `Opportunity = All`
  **and** the source has `Capabilities.PastePrintMetrics`.
- **Pass 2 (numerator)** — `StreamTestedObjectsAsync` with
  **`DefectsOnly = true`** and `PageSize = 10_000`; filter object type to
  match the opportunity flavour.
- **Bucketing** — `TimeBucketer.Decompose` plus binary search over bucket
  start epochs, identical to `FpyTrendByLineReport.FindBucketIndex`.

Skip / NOGO machinery is identical to `FpyTrendByLineReport`
(`SkipInputsIndex`, `KeepClass`, `NogoProducts`).

Line × time is card-derivable, so **every cell gets a correct rate** —
nothing suppressed, unlike the DPMO table's refdes / part-number / JEDEC
axes.

Tests: `tests/Nieweb.Reports.Tests/DpmoTrendByLineReportTests.cs`, plus a
parity test asserting the trend's window total equals
`DpmoTableReport`'s overall for the same scope.

---

## 3. Phase 3 — API

`src/Nieweb.Api/Endpoints/ReportEndpoints.DpmoTrend.cs`, modelled on
`ReportEndpoints.FpyTrend.cs`:

- `GET /api/reports/dpmo-trend` + `/export.csv` + `/export.xlsx`
- `BuildDpmoTrendAsync(..., IReportResultCache resultCache, bool useCache, ...)`
  — the view passes `useCache: false` and `Store`s its result; the exports
  pass `useCache: true`. Same asymmetry as FPY Trend (see `TR4`).
- Multi-source fan-out with per-source isolation: an offline / mis-configured
  DB is omitted, never fatal.
- **Line filter by line NUMBER**, resolved to machine ids **per source**
  via `ListMachinesAsync` + `TryParseLineNumber`.
- Validation through `CodedProblem` / `ProblemCodes` so the SPA renders a
  localized message rather than "HTTP 400".

PDF: `src/Nieweb.Pdf/DpmoTrendChartSvg.cs` + `DpmoTrendPdfRenderer.cs`
mirroring the FpyTrend pair; register in `ReportEndpoints.Pdf.cs` with
`IReportResultCache` threaded through.

Register every route in `MapReportEndpoints`.

Tests: `tests/Nieweb.Api.Tests/Endpoints/DpmoTrendEndpointTests.cs` — 401
anonymous, 400 on a bad bucket, `empty_window` code, one result per source,
and the lines filter actually narrowing.

---

## 4. Phase 4 — SPA

- `src/Nieweb.Web/src/routes/dpmo-trend.search.ts` — see
  [§5 landmine 1](#5-landmines-do-not-rediscover-these). Ship
  `dpmo-trend.search.test.ts` asserting `toApiQuery({ lines: [2, 7] })`
  round-trips.
- `src/Nieweb.Web/src/api/dpmoTrend.ts`
- `src/Nieweb.Web/src/charts/DpmoTrendChart.tsx` — lazy-loaded, echarts is
  ~1.1 MB gzipped
- `src/Nieweb.Web/src/routes/dpmo-trend.tsx`
- Exports **must** use `downloadWithAuth` — a plain `<a href>` 401s
- Filter pickers use `MultiSelectField` so the placeholder disappears once
  a value is selected
- Errors render through `<ApiErrorAlert>`
- Router: `dpmoTrendRoute` at `/report/dpmo-trend`
- Nav: see [§5 landmine 2](#5-landmines-do-not-rediscover-these); update
  `RootLayout.test.tsx`
- i18n: `nav.dpmoTrend` + a full `dpmoTrend.*` block in `bundle.ts`,
  `en.ts`, `fr.ts`. `bundle.ts` is the typed contract — a missing FR key
  fails the build.

---

## 5. Landmines (do not rediscover these)

These all cost real time during FPY Trend. Each has a concrete guard.

### 1. The Line filter silently does nothing

`toNumberArray` in `fpy-trend.search.ts` delegated to `toStringArray`,
whose `Array.isArray(v) ? v.filter(x => typeof x === "string")` **drops
numbers**. `formToSearch` emits `lines` as a *number* array; TanStack
Router re-runs `validateSearch` on every navigation, so `toNumberArray([2])`
returned `undefined`, `lines` vanished from both the URL and the API call,
and no `Machine_Id IN` ever reached SQL.

> **Guard:** copy the array helpers from **`dpmo.search.ts`**, never from
> `fpy-trend.search.ts`. Ship the round-trip test.
>
> **Tell:** string arrays like `skipStatuses` survive in the URL while
> `lines` does not.

### 2. Two nav items highlight at once

`/report/dpmo-trend` starts with `/report/dpmo`, so a `startsWith` check
lights up both. This is exactly the FPY / FPY Trend bug.

> **Guard:** in `RootLayout.tsx`, change the existing DPMO link to the
> exact-match form already used for FPY —
> `active={active === "/report/dpmo" || active.startsWith("/report/dpmo/")}`
> — and give the new link `active.startsWith("/report/dpmo-trend")`.

### 3. An empty machine list means "no filter"

`FakeAoiSource` checks `MachineIds is { Count: > 0 }` and the SQL
`AppendInClause` omits the `IN` clause when the list is empty. So an empty
list returns **everything**.

> **Guard:** a source with no machine on any selected line must
> `return null` and contribute nothing — never an empty `MachineIds`.

### 4. The DPMO denominator

Counting `TESTED_OBJECT` rows collapses the opportunity count to roughly
the defect count and pins DPMO near the 1e6 ceiling.

> **Guard:** denominator from `SUM(CARDS.Nb_Of_Tests_On_Comp [+ _On_Pads])`,
> always.
>
> **Golden numbers** — archive `HLYAOI`, 24 h ending the freeze at
> 2025-11-14T22:32:51 UTC, component objects, `Real` numerator:
> defects 5 191, opportunities 102 027 829, **DPMO ≈ 50.88**. A row-count
> denominator yields ≈ 956 690 — about 18 800× overstated.

### 5. Machine ids do not correspond across the two DBs

The same numeric `Machine_Id` means a different physical line in
`HLYAOI2024` vs `MEAOI`.

> **Guard:** never merge or filter machines by id across sources. Filter by
> line number, parsed from the machine name with `^L(\d+)`, and resolve to
> machine ids per source.

### 6. Exports 401

A plain anchor cannot carry the bearer token.

> **Guard:** `downloadWithAuth` from the start.

### Also inherited, already solved — keep using them

- **Vieweb #12421** — count-first / divide-last. Accumulate as `long`,
  compute the ratio only at emit time.
- **Vieweb #11211** — every bit-to-defect translation through
  `DefectBitDecoder`.
- **`TR4` result cache** — the view runs fresh and `Store`s; exports
  `GetOrRunAsync`. Keeps view + 3 exports at one AOI pass.
- **Dev server** — `dotnet run --no-launch-profile` from
  `src/Nieweb.Api`, browse `http://127.0.0.1:5000/app/` (not `localhost`).
  Backend changes need a restart; the SPA needs `npm run build`.

---

## 6. Docs

Add `CR5` (DPMO Trend by line) to `docs/phase-2.md` §7.3, alongside the
`CR4` FPY Trend entry, once the work lands.
