# Plan: Board Trace — prior AOI passes for the same barcode

_Owner: Board Trace slice, Phase 3 Priority 1._
_Depends on: existing TC2 board-trace endpoint + SPA route._
_Reviewed against current TC2 stack 2026-08-21 (second pass)._

## TL;DR

Today `GetBoardByBarcodeAsync` returns only the **most-recent** PANELS row
per `(source, Face_Number)`. Users want the default UX unchanged but need
to see and jump to **previous passes** when a panel has been re-inspected.

Locked-in shape (approved 2026-08-19, tightened 2026-08-21):

1. Backend keeps returning the latest pass as the "selected" pass, and
   also returns a **lightweight `PriorPasses` list per side** (id +
   date + status flags — no card rows). **Cap: 10 passes** per side
   (enforced in the report layer; SQL adapter allows a higher ceiling
   so the data source is not permanently product-capped).
2. Users override the selected pass via **URL params**
   (`?panelId=<sourceId>:<panelId>`, repeatable). No param = latest.
   One pin per source (last-wins); the UI shows one face at a time via
   `?side=`. Side toggle **keeps** the pin in the URL; a pin that
   belongs to the other face is ignored for the visible face (falls
   back to latest) and reapplies when the operator flips back.
3. The route re-fetches with those params (React Query key **must**
   include the pin map) and **replaces the current page in-place**.
   Prior passes surface in each `StageCard` as a **dropdown menu** of
   date/time links. The selected pass is omitted from the menu.
4. **Scope:** the pass list will naturally only render for post-reflow
   because pre-reflow (`MEAOI` v4.3.1) never stores multiple PANELS
   rows for the same barcode. No source-side gating — the query
   returns 1 row on pre-reflow, `PriorPasses` is empty, the menu
   simply doesn't render. Zero coupling, forward-compatible if
   pre-reflow ever gains multi-pass data.
5. **Error / warning split:**
   - Malformed `panelId` syntax → HTTP 400 (request unparseable).
   - Unknown source id in a pin → **ignore that pin** (same as the
     SPA dropping bad search keys). Do **not** 400 the whole board
     after a source rename/config change.
   - Pinned id missing from the 10-row window, or belonging to
     another barcode → **fall back to latest** on that stage and set
     a soft per-stage `SelectionWarning` (never `Error`). Do **not**
     reuse `BoardStageTrace.Error` — today's SPA treats `error` as a
     source failure (red badge); conflating selection failures would
     either blank the stage or look like the AOI DB is down.
   - Real source/query failures still use `Error` as today.

Blast radius: FPY / DPMO / Pareto / skips / TC1 are untouched —
`ListPanelsByBarcodeAsync` is only consumed by board trace.
`GetPanelByBarcodeAsync` stays latest-only.

Phase A (`limit` defaulting to 1) is safely mergeable on its own.
Do not start Phase C until grouping, selected-only card loads, and
the soft-warning path are in the report layer. Do not start Phase D
until the React Query key, `SidedStageTrace` projection of
`priorPasses` / `pinnedPanelId`, and per-side historical pill are in
the SPA.

## Phases

### Phase A — Data layer (surface up to N passes per side)

1. Extend `IAoiSource` with an **overload** so `CancellationToken`
   stays last (matches the rest of the codebase; avoids optional
   args after `ct`):

   ```
   // existing — unchanged call sites
   ListPanelsByBarcodeAsync(string barcode, CancellationToken ct)

   // new
   ListPanelsByBarcodeAsync(string barcode, int limit, CancellationToken ct)
   ```

   Default interface implementation: the no-limit overload calls
   `limit: 1`. The `limit` overload's DIM still wraps
   `GetPanelByBarcodeAsync` and returns **at most one row** — DIM
   alone is not enough for multi-pass tests. Adapters and all fakes
   must override the `limit` overload. Putting an optional `limit`
   in the middle of a single method (`barcode, limit, ct`) would
   bind existing `CancellationToken` args as `int` and fail to
   compile.
2. Update `SqlServerAoiSourceBase`. Change the CTE's outer filter
   from `WHERE rn = 1` to `WHERE rn <= @limit` and the outer
   `ORDER BY` to `Face_Number, rn`. Keep `WITH (NOLOCK)`,
   `READ UNCOMMITTED`, ApplicationName tagging, 30 s timeout, and
   audit-row discipline. **Do not add a time window** — this query
   has never had one (barcode lookup is how we find old
   re-inspections). Clamp `@limit` to `[1, 100]` in the adapter
   (hard ceiling against unbounded dumps). The product cap of 10
   is applied in the report layer, not here. On post-reflow
   (`HLYAOI2024`) this returns up to `limit × sides` rows; on
   pre-reflow (`MEAOI`) it will just return the same 1 row per side
   it always did, so the pre-reflow stage naturally shows no prior
   passes and no dropdown.
3. Override the `limit` overload on **all three** fakes (none of
   them override `ListPanelsByBarcodeAsync` today — they use the
   interface default, which wraps latest-only
   `GetPanelByBarcodeAsync`, so `limit: 10` would be silently
   ignored):
   - `src/Nieweb.DataSources.Fake/FakeAoiSource.cs` (E2E / hosted fake)
   - `tests/Nieweb.Api.Tests/Fakes/FakeAoiSource.cs`
   - `tests/Nieweb.Reports.Tests/Fakes/FakeAoiSource.cs`
   - plus any wrapping test doubles in `TraceabilityReportTests`

   Return matching panels for that barcode sorted per face by
   `PanelNumericDate DESC, PanelId DESC`, `Take(limit)` per face.
   Add a `REPEAT-001` barcode with 3 timestamps on one face to the
   hosted fake so component tests and Playwright can exercise the
   flow without touching a live DB.
4. Keep `GetPanelByBarcodeAsync` unchanged (still powers TC1).

### Phase B — DTOs (pass metadata without cards)

1. New record `PanelPassSummary` in `Nieweb.Reports.Traceability`:
   `PanelId, FaceNumber, PanelUtc, PanelStatus, AnomalyBr, AnomalyAr,
   NbOfErrorObject, HasBeenReviewed`. Deliberately lightweight — no
   cards, no product/machine/operator names. `PanelId` is `int` to
   match `PanelRow.PanelId` and the existing `:int` route constraints.
2. Extend `BoardStageSide` with two optional positional extras
   (defaults keep every existing constructor compiling):
   - `IReadOnlyList<PanelPassSummary> PriorPasses = []`
     (sorted latest-first; **excludes** the currently selected pass
     so the dropdown never lists the current one).
   - `int? PinnedPanelId = null` — set **only when an explicit
     override is active for this face**. Null when showing the
     default latest pass. The panel actually rendered is always
     `Panel.Panel.PanelId`; do not overload that meaning.
     **Per side, not per stage.** The route projects one
     `BoardStageSide` via `?side=`; a stage-level flag would show
     the "viewing older pass" pill on the other face after a toggle.
3. Extend `BoardStageTrace` with optional
   `string? SelectionWarning = null`. Soft message when a pin could
   not be honoured and the stage fell back to latest. Distinct from
   `Error` (source/query failure). Do **not** put `PinnedPanelId` on
   the stage.
4. SPA can also derive "is historical" without the flag: any entry
   in `priorPasses` newer than the visible panel (or
   `pinnedPanelId != null`). Grain must stay per-side either way.
5. Mirror the new record + extras in
   `src/Nieweb.Web/src/api/traceability.ts`. Treat missing
   `priorPasses` as `[]` and missing `selectionWarning` /
   `pinnedPanelId` as null so older cached payloads don't crash.

### Phase C — Endpoint (selected-pass overrides)

1. `GET /api/traceability/boards/by-barcode` gains an optional
   repeated query param `panelId=<sourceId>:<panelId>`, e.g.
   `?barcode=X&panelId=postreflow:1234`. Parse by splitting on the
   **first** colon; source id and panel id must both be non-empty;
   panel id must parse as a positive `int`.
   - Repeated form (rather than one param per known source id)
     keeps the endpoint agnostic to source topology.
   - Duplicate pins for the same source: **last-wins**. The SPA
     shows one face at a time, so one pin per source is enough.
     Document this; do not invent a `sourceId:faceNumber` key.
   - Malformed values (`panelId=foo`, empty id, non-numeric id) →
     HTTP 400 (the request cannot be parsed).
   - `panelId` for an unknown / unconfigured source → **drop that
     pin and continue** (do not 400). A renamed source must not
     blank a valid barcode lookup.
2. Add an **overload** on `TraceabilityReport` (same CT-last rule
   as Phase A — do not insert an optional dictionary before `ct`
   on the existing method):

   ```
   // existing
   GetBoardByBarcodeAsync(sources, barcode, ct)

   // new
   GetBoardByBarcodeAsync(sources, barcode, selectedPanelIds, ct)
   ```

   Existing method forwards to the new one with an empty/null pin
   map. `selectedPanelIds` is `IReadOnlyDictionary<string, int>`
   keyed by source id (last-wins if the endpoint folded duplicates).

   Fan-out logic per stage:
   - Call `ListPanelsByBarcodeAsync(barcode, limit: 10, ct)` on
     every stage. **Product limit = 10** — matches the "no operator
     should re-run a panel more than 10 times" rule. Enforced here,
     not by the adapter's 100 ceiling.
   - **Group returned rows by `Face_Number` first**, sorted
     latest-first. Today's `ProbeStageAsync` treats every row as a
     side; without grouping, `limit: 10` would emit 10 duplicate
     faces and break the side toggle.
   - For each face, if the caller pinned a `panelId` for this
     source AND that panel is in **this face's** returned list AND
     its `PanelBarCode` matches the request barcode → use that pass
     as the selected pass and set `PinnedPanelId`. Otherwise the
     head-of-list (latest) wins (unchanged default) and
     `PinnedPanelId` stays null. A pin that belongs to the other
     face of the same source is ignored for this face.
   - **Load `ListCardsForPanelAsync` only for the selected pass
     per face.** Prior rows become `PanelPassSummary` only. Today's
     loop loads cards for every panel; with `limit: 10` that is a
     10× round-trip.
   - `PriorPasses` = the remaining rows (per face, latest-first)
     minus the selected pass id.
   - Pinned id not in the returned 10-row window, or pinned id
     whose `PanelBarCode` does not match the request barcode →
     **fall back to latest** for every face on that stage, leave
     `PinnedPanelId` null, set `SelectionWarning` (e.g. "This pass
     link is older than the retained 10-pass window and is no
     longer available. Showing the latest pass."). Do **not** set
     `Error`. Do **not** HTTP 400. Other stages stay intact.
3. Preserve per-stage try/catch for real source failures (`Error`).
   HTTP 400 is reserved for unparseable `panelId` syntax only.

### Phase D — SPA route (dropdown + URL state)

1. Extend `TraceabilityBoardSearch` in the **existing hand-rolled**
   `validateTraceabilityBoardSearch` (this file is not Zod — do not
   introduce Zod/`v` just for this). Optional
   `passes?: Record<string, number>` keyed by `sourceId`. Keys ≤ 32
   chars, values positive ints; unknown / invalid entries dropped
   the same way `?barcode=` is dropped today. Nested maps are
   awkward in TanStack search params — parse an object if the
   router gives one, otherwise accept a compact `postreflow:1234`
   string.
2. **Saved views:** pass a sanitized filter into `SavedViewsMenu`
   (`{ barcode, side }` only). The menu does
   `JSON.stringify(currentFilter)` as-is — documenting "strip on
   write" is not enough; sanitize the prop. A saved historical pin
   would later miss the 10-row window; barcode+side is the durable
   filter. `applySavedFilter` can keep accepting whatever comes
   back; the validator drops bad `passes`.
3. `fetchBoardByBarcode(barcode, passes?)` serialises the map as
   repeated `panelId=<src>:<id>` params.
4. Board `useQuery` **must** include the pin map:

   ```
   queryKey: ["traceability-board", search.barcode, search.passes]
   queryFn:  () => fetchBoardByBarcode(search.barcode!, search.passes)
   ```

   Today's key is barcode-only. Without this change, navigating to
   a pinned pass keeps showing the cached latest inspection.
   Failed-objects queries already key on `(sourceId, panelId)` and
   will swap automatically once the board refetch lands.
5. **Clear `primaryHighlight` when `search.passes` changes** (same
   as today's clear on barcode change). Otherwise a drill-down
   marker from pass A can phantom-highlight after switching to
   pass B.
6. **URL / navigation rules:**
   - New barcode submit already navigates with
     `{ barcode: trimmed }` only — that **clears** `side` and
     `passes`. Keep it that way so pins never leak across scans.
   - Side toggle keeps `passes` (`{ ...prev, side: next }`). A
     face-1 pin stays in the URL while viewing face 2 (ignored
     server-side for face 2) and reapplies when flipping back.
     Do **not** clear the pin on side change.
7. Extend `SidedStageTrace` / `pickSideForStage` to carry
   `priorPasses`, `pinnedPanelId`, and stage-level
   `selectionWarning` through the projection. Today's helper only
   forwards panel/cards/error — without this the menu never sees
   the new fields.
8. `StageCard` renders a compact "Passes" menu when
   `priorPasses.length > 0` on the projected side:
   - Trigger = `<Button variant="light" leftSection={<IconHistory/>}>`
     showing "3 more passes" / "1 more pass" (localised, with
     plural forms).
   - Menu items = per-pass date/time formatted via existing
     `useDateTimeFormatter` + a small `Badge` for pass status
     (OK / defects / skipped) reusing `panelStatusOrSkippedKey`.
     Latest-first; selected pass omitted (already in the card
     header).
   - A "Latest pass" reset item at the top when this side is
     showing a non-latest pass; clicking it drops the
     `passes[sourceId]` entry.
   - Selecting an item navigates to the same `/traceability/board`
     route with the `passes` search updated. Route re-fetches,
     page replaces in-place. **No new browser tab.**
9. When `selectionWarning` is set, show a non-error Alert (yellow /
   info — not the red stage-error path) and optionally strip the
   bad pin from the URL so a refresh does not keep warning.
10. Add a small pill inline in `PanelSummary` when **this side** is
    historical (`pinnedPanelId != null`, or derived: any prior is
    newer than the visible panel). Copy:

    `Viewing pass 2 of 3 · 2026-08-01 12:14`

    Rank inside the 10-row window, **latest = 1**, so the number
    matches the latest-first menu (pass 1 = current default, pass 2
    = next-older, …). Same pill acts as the reset-to-latest link.
    Do not drive this pill from a stage-level flag.

### Phase E — Tests + i18n

1. New endpoint tests in `TraceabilityEndpointsTests.cs`:
   - Post-reflow-style fake with a barcode that has 3 passes → 200
     with the latest as the selected pass, 2 in `priorPasses`
     latest-first.
   - Pre-reflow-style fake with the same barcode returning a single
     row → 200, `priorPasses` empty for that stage (menu will not
     render).
   - `panelId=postreflow:<oldId>` pins that side to the old pass;
     `priorPasses` then excludes it and includes the true latest;
     `pinnedPanelId` set.
   - Malformed `panelId=foo` → 400.
   - `panelId=unknown-source:X` → 200, pin ignored, latest shown,
     no `selectionWarning` required (silent drop).
   - `panelId=postreflow:<idFromOtherBarcode>` → 200, that stage
     on latest with `selectionWarning` set, other stage intact,
     `Error` null.
   - Pinned id no longer in the top-10 window → 200, latest +
     `selectionWarning`, other stage intact, `Error` null.
2. New report tests in `TraceabilityReportTests.cs`:
   - Fixture with same barcode across 3 timestamps (all one face) —
     verify latest is selected by default and prior list has the
     other two, latest-first.
   - Two-sided barcode with 2 passes per face — verify per-face
     partitioning of the prior list, and that a pin for face 1
     leaves face 2 on latest with `PinnedPanelId` null.
   - Fixture with 12 passes on one face → verify only 10 are
     returned (report-layer cap) and cards are loaded only for the
     selected pass (one `ListCardsForPanelAsync` per face, not 10).
   - Pinned id missing from the window → that stage returns latest
     + `SelectionWarning`, `Error` null; other stages still
     materialise.
3. `REPEAT-001` on the hosted fake (3 timestamps, one face) plus
   the same multi-pass shape on the two test fakes.
4. New vitest coverage on `traceability-board.test.tsx`:
   - Passes menu appears when `priorPasses.length > 0`; hidden
     otherwise (pre-reflow stage's card should not render a menu
     in the multi-pass fixture).
   - Clicking a pass entry updates URL search state and re-fetches
     **with the new query key** (assert `fetchBoardByBarcode` is
     called with the pin map, not just that the URL changed).
   - "Viewing pass X of Y" pill renders when a non-latest pass is
     pinned on the visible side and vanishes on reset. After
     toggling `?side=` to the unpinned face, the pill is absent
     while the pin remains in the URL.
   - New barcode submit clears `passes` from the URL.
   - `selectionWarning` renders a non-error alert; stage still
     shows the latest panel (not the red `stageErrorTitle` path).
   - Changing `passes` clears `primaryHighlight`.
5. Playwright in `src/Nieweb.Web/e2e/traceability-board.spec.ts`
   (existing smoke uses `E2E-005`): add a second smoke that loads
   `REPEAT-001`, opens the menu, clicks the oldest pass, and
   verifies the URL updated + the pass-count pill appeared. This
   is the only test that proves replace-in-place rather than a new
   tab.
6. i18n keys under `traceability.board.passes.*` — EN canonical, FR
   parity, **including plural forms** (`one more pass` /
   `N more passes`) plus a `selectionWarning` / soft-alert string.
   Reuse existing status text so we don't duplicate defect
   labelling.

## Relevant files

- `src/Nieweb.DataSources/IAoiSource.cs` — add `ListPanelsByBarcodeAsync(barcode, limit, ct)` overload; existing no-limit overload forwards with `limit: 1`.
- `src/Nieweb.DataSources.Sql/SqlServerAoiSourceBase.cs` — swap `WHERE rn = 1` for `WHERE rn <= @limit`, reorder by `Face_Number, rn`, clamp to `[1, 100]`. No time window.
- `src/Nieweb.DataSources.Fake/FakeAoiSource.cs` — override the limit overload; add `REPEAT-001` with 3 passes on one face.
- `tests/Nieweb.Api.Tests/Fakes/FakeAoiSource.cs` + `tests/Nieweb.Reports.Tests/Fakes/FakeAoiSource.cs` — same override (today they inherit the latest-only default).
- `src/Nieweb.Reports.Traceability/TraceabilityDtos.cs` — `PanelPassSummary`; `BoardStageSide` gains `PriorPasses` + `PinnedPanelId`; `BoardStageTrace` gains `SelectionWarning`. Do not put pin state on the stage; do not reuse `Error` for selection failures.
- `src/Nieweb.Reports.Traceability/TraceabilityReport.cs` — new overload `GetBoardByBarcodeAsync(sources, barcode, selectedPanelIds, ct)`; existing forwards; product limit = 10; group by face; cards only for selected pass; soft warning + latest on bad pins.
- `src/Nieweb.Api/Endpoints/TraceabilityEndpoints.cs` — parse repeated `panelId=<src>:<id>` (first colon, last-wins per source); HTTP 400 only on malformed syntax; drop unknown-source pins.
- `src/Nieweb.Web/src/api/traceability.ts` — mirror new DTOs; extend `fetchBoardByBarcode(barcode, passes?)`.
- `src/Nieweb.Web/src/routes/traceability-board.search.ts` — hand-rolled `passes`; helpers to sanitize saved-view payloads.
- `src/Nieweb.Web/src/routes/traceability-board.tsx` — query key includes `search.passes`; extend `SidedStageTrace` / `pickSideForStage`; Passes menu; soft-warning alert; per-side pill; clear highlight on pass change; keep pin across side toggle; barcode submit clears passes.
- `src/Nieweb.Web/src/i18n/locales/en.ts` + `fr.ts` — `traceability.board.passes.*` with plural forms + soft-warning copy.
- `tests/Nieweb.Reports.Tests/Traceability/TraceabilityReportTests.cs` + `tests/Nieweb.Api.Tests/Endpoints/TraceabilityEndpointsTests.cs` — multi-pass suites, 12-passes-cap, cards-once-per-face, soft warning (not Error / not 400) for stale/mismatched pins, unknown-source ignored.
- `src/Nieweb.Web/src/routes/traceability-board.test.tsx` — menu, query-key refetch, per-side pill, side-toggle keeps pin, barcode clears passes, soft warning, highlight clear.
- `src/Nieweb.Web/e2e/traceability-board.spec.ts` — REPEAT-001 click-through smoke.

## Verification

1. `dotnet test tests/Nieweb.Reports.Tests/Nieweb.Reports.Tests.csproj --filter Traceability`
2. `dotnet test tests/Nieweb.Api.Tests/Nieweb.Api.Tests.csproj --filter Traceability`
3. `cd src/Nieweb.Web && npm test -- --run src/routes/traceability-board.test.tsx`
4. `cd src/Nieweb.Web && npx playwright test e2e/traceability-board.spec.ts`
5. Optional smoke against the fake source: `dotnet run --project src/Nieweb.Api` then
   `GET /api/traceability/boards/by-barcode?barcode=REPEAT-001` returns 3 total passes
   with 2 in `priorPasses` and the latest as `panel`.
6. Live-source sanity (post-merge): hand-pick one known re-inspected
   barcode from `HLYAOI2024`, verify prior-passes ordering and dates
   in the SPA. **Read-only discipline preserved** — no writes on any
   Superviseur DB.

## Decisions

- **Selection model:** replace-current-page via URL state (approved).
- **Cap:** 10 passes per side (approved). Enforced in the **report**
  layer. SQL adapter clamps to `[1, 100]` only as a dump guard.
- **UI affordance:** dropdown menu inside `StageCard` (approved).
  Selected pass omitted from the menu (already in the card header).
  "Latest pass" reset row only when a non-latest pin is active.
- **Pass numbering:** rank inside the 10-row window, latest = 1, so
  "Viewing pass 2 of 3" matches the latest-first menu.
- **Post-reflow only, in practice:** confirmed by the user —
  pre-reflow (`MEAOI` v4.3.1) never stores multiple PANELS rows for
  the same barcode. Natural consequence (1 row → empty
  `PriorPasses` → no menu). No source-side flag.
- **One pin per source:** last-wins. Side toggle **keeps** the pin
  in the URL; the other face ignores it and shows latest; flipping
  back reapplies. Do not key by `sourceId:faceNumber`.
- **`PinnedPanelId` grain:** per `BoardStageSide` (`int?`), only
  set when an override is active. Never on `BoardStageTrace`.
  Visible panel id is always `Panel.Panel.PanelId`.
- **Error / warning split:**
  - HTTP 400 = malformed `panelId` syntax only.
  - Unknown source pin = drop silently.
  - Out-of-window / barcode-mismatch = fall back to latest +
    `SelectionWarning` (not `Error`).
  - `Error` remains source/query failure only — SPA red badge path.
- **Cards:** `ListCardsForPanelAsync` only for the selected pass
  per face. Prior rows are summaries.
- **API shape:** overloads for both `ListPanelsByBarcodeAsync` and
  `GetBoardByBarcodeAsync` so `CancellationToken` stays last.
  Interface default with `limit > 1` still returns at most one row
  until a fake/adapter overrides.
- **React Query:** board query key is
  `["traceability-board", barcode, passes]`. Clear
  `primaryHighlight` when `passes` changes.
- **Saved views:** sanitize to `{ barcode, side }` before passing
  into `SavedViewsMenu` (it stringifies the prop as-is).
- **Barcode submit:** navigate with `{ barcode }` only — clears
  `side` and `passes` so pins never leak across scans.
- **Search validator:** extend the existing hand-rolled helper; do
  not introduce Zod.
- **`SidedStageTrace`:** must forward `priorPasses`,
  `pinnedPanelId`, and `selectionWarning` from the projection.
- Default UX unchanged — latest pass renders exactly as today when
  no pin is present.
- URL-driven state preserves bookmarks and back-button. Saved views
  stay durable by not storing a pin that can age out.
- No pin-level data in `PanelPassSummary` (drill-down still uses
  TC1 endpoints).
- No source writes. Barcode lookup has **no time window**. Remaining
  read-only guards still apply: `WITH (NOLOCK)`,
  `READ UNCOMMITTED`, ApplicationName tagging, 30 s timeout, audit
  row.
- **Out of scope / no coupling:** FPY, DPMO, Pareto, skip reports,
  and TC1 panel-by-barcode are unaffected.

## Implementation order

A → B are additive and mergeable independently. C must include
grouping-by-face, selected-only card loads, overloads (CT last),
and the soft-warning / fall-back-to-latest path (never `Error` for
pins). D must include the query key, `SidedStageTrace` projection,
sanitized saved-view prop, highlight clear on pass change, keep-pin
side toggle, barcode-clears-passes, and the per-side historical
pill. E covers endpoint + report + vitest + all three fakes + the
Playwright REPEAT-001 smoke.
