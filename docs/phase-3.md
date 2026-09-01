# Phase 3 — Sigmalink absorption + deferred Vieweb items

_Status: **IN PROGRESS** — Board Trace (§6.1) is the active slice.
`BT3`, `BT4`, `BT9`, and the BoardViewer crosshair fix are **done**;
`BT1` / `BT2` are **partial**; kiosk mode (`BT6`–`BT8`) is not
started. Review and Analyse have **no code yet**._
_Depends on: `docs/tech-stack.md` (SIGNED-OFF 2026-07-20),
`docs/phase-2.md` (**COMPLETE** 2026-07-31). Post-plan chart work
(DPMO Trend by line, 2026-08-26) is recorded in §6.0._
_Successor of: `docs/phase-2.md`._

## 1. Purpose

Phase 2 replaced the reporting surface of legacy Vieweb 1.6.2 and
brought numeric parity for its report catalogue. **Phase 3 sharpens
Nieweb's identity as the site's data-analysis and traceability tool
for the SMT lines.** It borrows the two Sigmalink 1.6.5 (σLink /
Deep Blue) modules that pay the highest analytical dividend —
Analyse and Review — polishes them into first-class Nieweb features,
and finishes the Vieweb items Phase 2 deferred.

Nieweb is **not** trying to replace Sigmalink. Sigmalink continues to
exist as its own installation for anything that isn't in the Nieweb
scope — most importantly the Data Import (iCAD) module, which is the
tool the SMT programmers use to author new inspection programs.
Nieweb never reads, imports, or otherwise depends on CAD-authored
data: AOI machines write inspection results to the Superviseur DB and
Nieweb reads only from there.

Phase 3 priority order is now intentionally narrowed and sequenced:

1. **Board Trace.** Any operator or engineer should be able to scan
   a serial number (barcode) and get a clear, complete picture of
  what happened to that particular panel end-to-end: pre-reflow AOI
  defects and sanctions, post-reflow AOI defects and sanctions,
  repair actions, final state. The
   `Nieweb.Reports.Traceability` package shipped in Phase 2 provides
   the plumbing; Phase 3 finishes the UI, wires it into the top-level
   navigation, makes it the default landing tile for the QA role, and
   ships an opt-in **kiosk mode** so anyone with a barcode scanner
   and access to a shop-floor workstation can pull up a panel's
   history without needing a Nieweb login.
2. **Analyse dashboards.** The Analyse dashboards (Live, Line
   Performance, Product, Panel, Cp / Cpk) don't just display KPIs —
   they make it easy to pinpoint _where_ the process is losing yield
   (which line, which shift, which product, which pad, which
   component package) so engineers can go fix the actual cause.
3. **Review (offline + OIS export only).** We will implement the
   offline review workflow and, if needed, the OIS export path; the
   broader inline / remote / repair review surface is deferred past this
   phase because it is not required for the project's current scope.

SPI/PI work (DBQuery-Pi, SigmaLine feedforward, PI-capacity guard, and
SPI↔AOI mapping/correlation dashboards) is explicitly out of scope for
this phase.

Non-goals (deferred to Phase 4 or later): the SigmaLink dual-lane
review conveyor UI (needs a Zebra printer + IO board pair we don't
have on staging), Sigma Connect AMQP replacement, full offline review
of a remote workstation, and a browser-native CAD editor. See §8.

---

## 2. Scope

### 2.1 Sigmalink modules to absorb

Sigmalink 1.6.5 ships five modules. Phase 3 delivers Nieweb
equivalents for the three runtime modules that carry SMT-line-critical
workflow value; Configure / Monitor are already covered by Nieweb's
own admin UI shipped in Phase 2, and Data Import stays with Sigmalink
(see §1).

| Sigmalink module | Nieweb equivalent (Phase 3) | Skill reference | Why |
|---|---|---|---|
| **Data Import (iCAD)** | ❌ **Out of scope, never consumed.** Sigmalink remains the CAD authoring tool independently of Nieweb. Nieweb reads inspection results from the Superviseur DB, not from CAD-authored files. | — | Nieweb is a data-analysis tool, not a Sigmalink replacement. |
| **Review** | Offline review workflow under `/app/review/*`, plus OIS export where needed. We do not implement the full inline / remote / repair review surface in this phase. XML-configured layout, defect status constants, and custom messages stay in the backlog unless a concrete owner needs them. | `sigmalink-review` | The project only needs the offline review path and any required OIS export for the immediate workflow. |
| **Analyse** (Live / Line Performance / Product / Panel / Cp-Cpk) | AOI-focused dashboards under `/app/analyse/*` using the same tile-based `<ReportCanvas>` composition shipped in Phase 2 §7.6. Feed from **DBQuery-K** back-end only. | `sigmalink-analyse` | This becomes the second Phase 3 priority and removes the parallel Analyse WAR install (port 8082) while keeping KPI numbers aligned. |
| **SigmaLine feedforward** | ❌ Out of scope for this project. | — | Requires SPI/PI integration path the team has explicitly excluded. |
| **PI-Capacity guard** | ❌ Out of scope for this project. | — | Exists to throttle DBQuery-Pi traffic, which this phase does not use. |
| Configure | ✅ Already covered by Nieweb `/app/admin/*` (Users, Roles, Production lines, Shifts, MSA parameters, Databases, Board SVGs). | — | Nothing new required. |
| Monitor | ✅ Already covered by Nieweb `/health/*`, `/api/admin/audit`, and OpenTelemetry. | — | Nothing new required. |

### 2.2 Optional future additions (cut from the project scope)

The following are useful but are explicitly not required for this project and are therefore cut from the Phase 3 plan:

| Phase 2 reference | Status | Why cut |
|---|---|---|
| §7.7 · Automatic treatments | **Cut** — not in project scope. | Not required for the current line-operations deployment; no business need at this time. |
| §9 · Test Empty Master entity | **Cut** — not in project scope. | Requires MSA DB infrastructure that is not part of the current plan. |
| §9 · Additional locales | **Cut** — not in project scope. | Not required for operational delivery; the project remains EN + FR only. |

The remaining deferred Vieweb items are the MSA report and Process Capability dashboard; both remain optional future work once the dedicated empty-panel Superviseur DB is available. |

| Phase 2 reference | Nieweb future delivery | Why deferred |
|---|---|---|
| §9 · MSA report | `Nieweb.Reports.Msa` + `templatemsa` entity + MSA-threshold admin page. Cp / Cpk / EV / %EV / GR&R on `Reference Designator` and `Package`. | Needed a dedicated empty-panel Superviseur DB that is not yet commissioned on site. |
| §7.7 · Process Capability dashboard | `Nieweb.Reports.ProcessCapability` — per-production-line grid of DPMO, FPY_Diag, Machine efficiency, Avg cycle duration, Nb inspections, plus Cp / Cpk compo & paste rows sourced from the MSA DB above. | Depended on MSA data; parked with it. |

### 2.3 Cross-cutting features

- **Read-only discipline preserved.** Every new SQL statement (DBQuery-K,
  MSA queries) obeys the guards documented
  in `.github/copilot-instructions.md` — `WITH (NOLOCK)`,
  `READ UNCOMMITTED`, 30 s query timeout, `ApplicationName='Nieweb-...'`,
  time-window filter, per-query audit row. **No writes to any Superviseur
  DB, ever.**
- **Sigmalink licence tokens.** Sigmalink gated modules by a
  `sigmalink.licence` file (see `sigmalink-legacy` skill). Nieweb
  preserves the concept in an internal `LicenseToken` table so a
  customer without the Analyse token can still get Review + Reports and
  vice-versa. Bootstrap install grants all tokens; deployments can
  narrow later. No cryptographic licence file — we use signed feature
  flags in the AppParameter store.
- **Defect status constants preserved character-for-character.**
  `PANEL_*`, `SUBPANEL_*`, `COMPO_*`, `TERMINAL_*`, `PAD_*`, `PADS_*`,
  `UNSUPPORTED_DEFECT` all keep their Sigmalink spellings so historical
  audit / review rows stay queryable across products (Vieweb, Sigmalink,
  Nieweb). Enforced by a parity test that scans
  `pdf_text/Sigmalink-user-guide-V1.6.5.txt` for each constant.
- **Runtime-configurable defect ordering and panel-side mapping.**
  `dbqueryK/defectOrders` is exposed as an admin-editable `AppParameter`
  row — never hard-coded.
- **KPI numeric parity.** Nieweb Analyse, Nieweb Reports, Sigmalink
  Analyse (during coexistence), and Vieweb historical extracts must
  agree to rounding error for FPY / DPMO / Cp / Cpk / GR&R over the
  same time window on the same DB. Same snapshot-test pattern as
  Phase 2 R2.
- **Modernised authentication for absorbed modules.** Sigmalink's
  default admin/admin credentials and SHA-1 password hashes are
  dropped (as they were for Vieweb) — reuse the Argon2id / OIDC
  identity stack already shipped in Phase 1. The `admin/admin`
  hard-coded broker credentials for Sigma Connect AMQP are irrelevant
  because Sigma Connect is out of scope in Phase 3 (see §8).

---

## 3. Success criteria (definition of done)

1. **Sigmalink Review + Analyse decommissioned on the pilot line.**
   The Nieweb process serves every Review and Analyse URL that used
   to hit ports 8080 (Sigmalink Review) or 8082 (`VIT_Analyse.war`).
   The rest of Sigmalink (Data Import, Configure, Monitor) stays on
   the customer's box under its own routes — that is by design, not
   a miss (see §1).
2. **Review workflow numerical parity.** For a sampled week of live
   AOI production, the counts per defect status
   (`OK_OPERATOR` / `KO_OPERATOR` / repaired / scrap / false call /
   true call) computed by Nieweb Review match the counts Sigmalink
   Review recorded, on the same panels, to zero difference. Any
   divergence is a blocker.
3. **AOI-only Analyse scope enforced.** Analyse code paths, report
  contracts, and UI filters are backed by AOI sources only; no
  DBQuery-Pi, feedforward, or SPI-side mapping dependencies remain in
  shipping code.
4. **MSA numeric parity.** For a reference empty-panel run, the Cp,
   Cpk, EV, %EV, and GR&R values Nieweb computes match hand-computed
   values from the raw MSA DB rows to rounding error (snapshot test).
5. **Automatic-treatment reliability.** *(Cut from project scope — §2.2.
   Criterion retained for reference if automatic treatments are revived.)*
   A weekly treatment scheduled Monday 06:00 for the last full week runs
   unattended for **eight** consecutive weeks against staging and emails
   / saves the XLSX every time. Any failed delivery raises an audit row
   surfaced in the admin UI (bug #9699 mitigation).
6. **Locale coverage.** *(Cut from project scope — §2.2. EN + FR only for
   operational delivery.)* DE, ES, ZH bundles complete for every
   user-facing string exercised by an E2E smoke; missing-key gate in
   CI stays green.
7. **Read-only discipline preserved.** No new code path writes to
  any Superviseur DB. Every DBQuery-K / MSA read has an audit row with
  source tag, duration, and row count.
8. **Board Trace shipped and adopted.** Nieweb Board Trace (barcode
  → pre-reflow AOI → post-reflow AOI → repair → final state) is
   the default panel-lookup tool for QA and line engineers. A sampled
   week of live production has zero barcode lookups that return
  incomplete data (missing pre-reflow step, missing post-reflow step,
  missing repair sanction) where the underlying AOI Superviseur DB
   actually has the rows. Latency budget: p95 < 500 ms on a warm
   cache, < 2 s cold. **Kiosk mode** is enabled on at least one
   shop-floor workstation and has served ≥ 100 real barcode lookups
   over the sampled week without triggering the rate limiter or the
   PII redaction guard.
9. **CI green** on every push: `dotnet build`, `dotnet test`,
   `npm ci && npm run build && npm run lint && npm run test`,
   Playwright E2E smoke covering Review inline flow, Analyse Live
   dashboard load, MSA report render, and Board Trace barcode lookup.
10. **Docs.** This document plus one companion per absorbed module:
  `docs/review.md`, `docs/analyse.md`. Each
    describes the underlying data model, KPI formulas (reusing
    `aoi-quality-metrics` skill), and the Sigmalink features
    intentionally dropped.

Explicitly **not** a success criterion: matching Sigmalink's UI
pixel-by-pixel. Phase 3 targets the Mantine + ECharts + Canvas idiom
already established in Phases 1 and 2.

---

## 4. New components coming online

| Project | Purpose |
|---|---|
| `src/Nieweb.Review/` (new) | Review-workflow domain logic. Owns the defect-status state machine, the per-role authorisation matrix (`ROLE_REVIEWER`, `ROLE_ANALYZER`, supervisor override), the layout XML schema, and the OIS export pipeline. |
| `src/Nieweb.Web/src/review/` (new) | Review UI. Inline mode (one panel at a time, keyboard-first), offline mode (queue processing on a workstation), remote mode (browser at a QA desk), repair mode (annotates + prints Zebra label). Widget-composed so the layout XML from Sigmalink still describes it. |
| `src/Nieweb.Analyse/` (new) | Analyse KPIs on top of `Nieweb.Reports`. Consumes DBQuery-K back-end via an `IKSource` capability interface. Uses the same tile / canvas composition as Phase 2 reports so the dashboards are savable as reports. |
| `src/Nieweb.DataSources.K/` (new) | DBQuery-K client, same shape. |
| `src/Nieweb.Scheduling/` (deferred from Phase 2) | Automatic-treatment scheduler on `BackgroundService`. Row-level lease, per-treatment + global switches. |
| `src/Nieweb.Mail/` (deferred from Phase 2) | `MailKit`-backed SMTP delivery. Idempotent per `(treatmentId, runTimestamp)`. |
| `src/Nieweb.Reports.Msa/` (deferred from Phase 2) | MSA report + `TestEmptyMasterEntity`. Cp / Cpk / EV / %EV / GR&R over the empty-panel DB. |
| `src/Nieweb.Reports.ProcessCapability/` (deferred from Phase 2) | Per-line PC dashboard now that MSA data is available. |

The internal-DB schema (`Nieweb.Data`) gains:

- `ReviewSession` + `ReviewDefect` + `ReviewComment` + `ReviewSanction`
  + `ReviewCustomMessage` — persistence for the Review workflow.
- `AutomaticTreatment` + `EmailRecipient` (as designed in Phase 2 §5).
- `LicenseToken` (per-module feature flag).

Every new entity is created via EF Core migrations (dual-provider
Npgsql/Sqlite story from Phase 1).

---

## 5. Architecture overview

```mermaid
flowchart LR
    U[User] -->|HTTPS| K[Kestrel :8080]
    K -->|/app/*| S[React SPA static bundle]
    K -->|/api/*| A[Nieweb.Api Minimal API]

    subgraph Reports (Phase 2)
      A --> R[Nieweb.Reports]
      R --> TR[Nieweb.Reports.Traceability]
    end

    subgraph "Phase 3 — new modules"
      A --> REV[Nieweb.Review]
      A --> ANA[Nieweb.Analyse]
      A --> MSA[Nieweb.Reports.Msa]
      A --> PC[Nieweb.Reports.ProcessCapability]
      A --> SCH[Nieweb.Scheduling]
      SCH --> MAIL[Nieweb.Mail]
    end

    subgraph "Data adapters"
      R -->|IAoiSource| DS[Nieweb.DataSources.Sql]
      ANA -->|IKSource| KDS[Nieweb.DataSources.K]
      MSA -->|IMsaSource| MSADS[Nieweb.DataSources.Sql — MSA DB]
    end

    subgraph "Live SQL Server (read-only, WITH NOLOCK)"
      DS -->|SELECT| POST[HLYAOI2024 post-reflow]
      DS -->|SELECT| PRE[MEAOI pre-reflow]
      KDS -->|SELECT| KDB[DBQuery-K]
      MSADS -->|SELECT| EMPTY[Empty-panel MSA DB]
    end

    A -->|EF Core| N[Nieweb internal DB]
```

Key architectural principles carried over from Phase 2:

- **Capability interfaces, not one god adapter.** `IAoiSource`,
  `IKSource`, `IMsaSource`, `IPinLevelSource` are separate;
  a data source exposes only the capabilities its backing DB
  supports (matches the CR4/CR5 asymmetry documented in
  `.github/copilot-instructions.md`).
- **Reports are pure functions.** `(sources, filter, parameters) →
  typed DTO`. Analyse dashboards reuse the same tile registry as the
  Phase 2 `<ReportCanvas>` so they compose and export the same way.

---

## 6. Backlog

Legend: `M` = mandatory for Phase 3 sign-off, `S` = should-have,
`C` = could-have (drop if slipping). Every item cites the skill that
owns the canonical facts.

**Progress snapshot (2026-08-26).** Phase 2 closed complete on
2026-07-31. Phase 3 is **in progress** on the `phase-c` branch. Board
Trace (§6.1) is the only section with substantial new code since the
Phase 2 traceability slice: `BT3`, `BT4`, and `BT9` are **done**;
`BT1` and `BT2` are **partial**; QA landing tile (`BT5`) and kiosk
mode (`BT6`–`BT8`) are **open**. A post-plan **DPMO Trend by line** chart report landed on
`phase-c` (§6.0) — same family as Phase 2's FPY Trend (`CR4`) but
outside the original Phase 3 backlog. Everything else — Review,
Analyse, MSA, automatic
treatments, extra locales, and the Sigmalink coexistence / retirement
track — has **no code yet**.

### 6.0 Post-Phase-2 chart reports (shipped on `phase-c`, not Phase 3 scope)

These items extend the Phase 2 chart catalogue after the 2026-07-31
close-out. They are **not** Phase 3 sign-off criteria; recorded here so
the backlog snapshot stays honest.

- `CR5` ✅ done `1ea495b` / `46aca6c` — **DPMO Trend by line.**
  `DpmoTrendByLineReport` returns one `DpmoTrendResult` per source
  with time-bucket decomposition (same bucketing as FPY Trend).
  API: `/api/reports/dpmo-trend` + CSV / XLSX / PDF exports.
  SPA: `/report/dpmo-trend` with ECharts line chart, nav entry,
  and i18n (EN / FR). Uses a `DefectsOnly` predicate on
  `TESTED_OBJECT` streams so the denominator matches the DPMO table.

### 6.1 Board Trace UI (M) — `vit-aoi-database` + `aoi-quality-metrics`

Board Trace is the flagship Phase 3 feature (§1). The
`Nieweb.Reports.Traceability` package from Phase 2 already loads a
panel by barcode; Phase 3 wraps a first-class UI around it.

> **Status (2026-08-26).** Phase 2's traceability slice (`TC1`–`TC5`
> in `docs/phase-2.md`) plus follow-on Board Trace work on `phase-c`
> delivered more of this section than planned: `BT3`, `BT4`, and
> `BT9` are **done**; `BT1` / `BT2` are **partial**. The genuinely
> outstanding work is the consolidated timeline (`BT2`), the QA
> landing tile (`BT5`), and the whole kiosk track (`BT6`–`BT8`).

- `BT1` 🟡 partial — `/app/board-trace` route with a prominent barcode
  search
  box on every layout. Barcode scanner-friendly: input auto-focuses,
  Enter submits, no click required. Deep link: `/app/board-trace/<barcode>`.
  *Delivered by `TC3` + Board Trace redesign (`65683a5`):* the route
  (`src/Nieweb.Web/src/routes/traceability-board.tsx`), the reusable
  `BarcodeLookupCard` on the home page, a URL-driven **two-sided PCB**
  toggle (`?side=`), and saved-view support. *Outstanding:* the search
  box is not yet present on **every** layout, and the scanner-friendly
  auto-focus / Enter-submit behaviour is unverified.
- `BT2` 🟡 partial — End-to-end timeline UI: pre-reflow AOI defects &
  sanctions → post-reflow AOI defects & sanctions →
  repair actions → final panel state. Each step shows timestamp,
  machine, operator, and any defect bit-flags decoded per the
  `vit-aoi-database` skill.
  *Delivered by `TC2` / `TC5` Phase D:* both stages load side by side,
  and the `FailedObjectsTable` drill-down shows the decoded defect
  bits, repair result / date / comment and operator per failed object.
  *Outstanding:* the actual **timeline presentation** — the stages are
  rendered as parallel tables, not as one chronological sequence — and
  chronology cues between stage events.
- `BT3` ✅ done — Cross-DB stitch. Pre-reflow (`MEAOI`) and post-reflow
  (`HLYAOI2024`) live on different SQL Server instances with
  different schema revisions; Board Trace merges the two histories on
  `(Barcode, Panel_Numeric_Date)` while respecting the capability
  flags (`IPinLevelSource` present only on post-reflow, `IPasteSource`
  present only on pre-reflow — see `.github/copilot-instructions.md`).
  *Delivered by `TC2`:* `TraceabilityEndpoints` resolves a barcode
  across every registered source and returns a per-stage result,
  gated on the capability flags.
- `BT4` ✅ done — Defect visualisation. Where an admin-uploaded Board SVG
  exists for the product, overlay each defect at its subpanel /
  component location. Where no SVG exists, fall back to the tabular
  view.
  *Delivered by `TC4` + `TC5`:* the Board-SVG asset pipeline plus the
  `<BoardViewer>` component with dual-stage colour-coded highlights,
  optional **crosshair** on the primary highlight (header toggle,
  `localStorage` preference), zoom / pan, Foreign Material splat
  overlay, and a graceful tabular fallback when no SVG is cached.
  *Follow-up `403b9d5`:* crosshair dash lengths are sized from the SVG
  viewBox (micron user units) so the lines stay visible at full-panel
  zoom — a fixed CSS `stroke-dasharray` rendered as invisible speckles
  on real Sigmalink panel SVGs.
- `BT9` ✅ done `403b9d5` — **Prior AOI passes for the same barcode.**
  When a panel has been re-inspected, Board Trace returns up to **10
  prior passes per face** as lightweight metadata (`PriorPasses` on
  each `BoardStageSide`). The default UX is unchanged (latest pass).
  Operators pin an older pass via repeated URL params
  (`?panelId=<sourceId>:<panelId>`); stale or mismatched pins fall
  back to latest with a soft `SelectionWarning` (not a stage error).
  SPA: Passes menu per stage, historical pill, side toggle keeps the
  pin, saved views persist barcode + side only. Plan:
  [`plan.md`](../plan.md) at repo root.
- `BT5` — QA landing tile: Board Trace becomes the default first
  tile on the QA role's dashboard.
- `BT6` — **Kiosk mode.** Opt-in per deployment via an admin toggle
  (`Nieweb:BoardTrace:KioskEnabled` + a per-source-IP allowlist). When
  enabled, `/app/board-trace` and `/app/board-trace/<barcode>` — and
  only those routes — are reachable without authentication. Every
  other Nieweb route continues to require login; the kiosk allowance
  is scoped in ASP.NET's authorization policy, not toggled at the
  middleware level, so accidental privilege leaks are structurally
  prevented.
- `BT7` — Kiosk hardening. (a) Per-IP token-bucket rate limiter
  (default 30 lookups/min, burst 10) to defeat scraping. (b) **PII
  redaction guard**: operator names are shown as initials only when
  the request is unauthenticated; full names return for logged-in
  users. (c) Every kiosk lookup writes an audit row with source IP,
  barcode, and result count — no user id (there isn't one). (d) A
  visible "Kiosk mode — read-only" banner on the page. (e) Deep-link
  URLs use the barcode itself, not an internal panel id, so no id
  enumeration is possible.
- `BT8` — Playwright E2E: (a) authenticated scan flow — known-defective
  panel barcode from a fixture, assert the timeline shows pre-reflow
  and post-reflow AOI defects + repair sanction in chronological
  order; (b) kiosk-mode scan flow — same barcode over an
  unauthenticated session, assert same timeline but with operator
  names redacted; (c) kiosk-mode negative — attempt to hit
  `/app/settings` unauthenticated and assert a 401 / redirect.

### 6.2 Review UI (M) — `sigmalink-review`

> **Scope note (§1).** Phase 3 delivers **offline review + OIS export
> only** (`REV3` + export plumbing). Inline (`REV2`), remote (`REV4`),
> repair / Zebra label (`REV5`), and full XML layout parity (`REV6`)
> are **deferred** unless a design partner requests them — listed
> below for traceability, not as current-sprint work.

- `REV1` — Domain model (`ReviewSession`, `ReviewDefect`,
  `ReviewSanction`, `ReviewComment`, `ReviewCustomMessage`) +
  state machine covering `OK_OPERATOR`, `KO_OPERATOR`, repaired,
  scrap, false call, true call. Constants preserved
  character-for-character.
- `REV2` ⬜ deferred — Inline mode: full-screen, keyboard-first, one panel at a
  time, `review_inline.xml`-configurable layout. Timeout 720 s
  (Sigmalink default). *Out of Phase 3 scope (§1).*
- `REV3` — Offline mode: local queue on a review workstation,
  batch-sync back to Nieweb. Timeout 30 s (Sigmalink default). Ties
  into OIS export via `OISPlugin.xml` equivalent. **Primary Review
  deliverable for Phase 3.**
- `REV4` ⬜ deferred — Remote mode: browser at a QA desk, read-only unless the
  operator holds `ROLE_REVIEWER`. *Out of Phase 3 scope (§1).*
- `REV5` ⬜ deferred — Repair mode: annotates rejected panels, prints a Zebra
  label with the barcode + defect summary. Zebra ZPL is emitted
  server-side; the workstation just POSTs to the label spooler.
  *Out of Phase 3 scope (§1).*
- `REV6` ⬜ deferred — Layout XML compatibility. Nieweb reads the same
  `review_lines.xml`, `review_layout.xml`, `review_actions.xml`,
  `review_defects.xml`, `review_comments.xml`,
  `review_custom_messages.xml`, `review_policy.xml`,
  `review_plugins.xml` files Sigmalink used, so a customer's tuned
  review setup migrates without re-editing.
- `REV7` — Numeric-parity harness. For a sampled week, per-panel
  defect / status counts match Sigmalink Review exactly (§3
  criterion 2).
- `REV8` — Widgets ported: defect list, defect image, localization,
  reference image, shortcuts. Each widget lives under
  `src/Nieweb.Web/src/review/widgets/` with its own vitest. The
  reference-image widget resolves images from a configurable share
  path (e.g. `\\fileserver\reference-images\<product>\<component>.jpg`)
  or from an admin-uploaded set in Nieweb — no CAD parsing needed.
- `REV9` — PRE_REFLOW / POST_REFLOW equipment selection so a
  reviewer in the pre-reflow area doesn't see post-reflow panels.

### 6.3 Analyse dashboards (M) — `sigmalink-analyse`

- `ANA1` — DBQuery-K client (`Nieweb.DataSources.K`).
- `ANA2` — **Live** dashboard: real-time counters (last 5 min /
  hour / shift) for the top production KPIs. Tile-based so users can
  save a custom Live view.
- `ANA3` — **Line Performance** dashboard: AOI yields per line with
  per-shift comparison.
- `ANA4` — **Product** dashboard: FPY / DPMO / defect Pareto per
  product across all lines.
- `ANA5` — **Panel** dashboard: drill from panel barcode to AOI defect
  list → repair sanction. Uses the same
  `Nieweb.Reports.Traceability` back-end as Board Trace (§6.1) — the
  Panel dashboard is the analyst-oriented view (comparisons across
  boards) while Board Trace is the operator-oriented view (one
  barcode, full timeline).
- `ANA6` — **Cp / Cpk** dashboard: histogram + radar + panels +
  result tables per measure nature (Volume, Height, Area, Offset X,
  Offset Y, Theta). Reuses `aoi-quality-metrics` skill formulas.
- `ANA7` — Retirement of `VIT_Analyse.war`: after two weeks of
  parallel running, the Jetty install is uninstalled from the
  pilot line.

### 6.4 SigmaLine feedforward (cut) — `sigmalink-legacy`

Cut from scope. No SPI/PI integration is planned in this project.

### 6.5 PI-Capacity guard (cut) — `sigmalink-legacy`

Cut from scope. No DBQuery-Pi traffic is planned in this project.

### 6.6 MSA report + Process Capability (M, revives §7.7) — `aoi-quality-metrics`

- `MSA1` — MSA DB adapter. New `IMsaSource` capability, new
  `Nieweb.DataSources.Sql` implementation, same read-only discipline.
- `MSA2` — `templatemsa` entity in the report editor. Configurable
  by Reference Designator and Package. Renders EV, %EV, GR&R,
  Cp, Cpk with the formulas from `aoi-quality-metrics` — no
  re-derivation.
- `MSA3` — MSA-threshold admin page in Nieweb `Settings`. Bootstraps
  from the Vieweb `AppParameter` seed values that were parked in
  Phase 2 §7.7.
- `MSA4` — Process Capability dashboard now that MSA rows are
  available. Fills in the "MSA source not configured" placeholder
  Phase 2 shipped for Cp / Cpk compo & paste.
- `MSA5` — TestEmptyMasterEntity for the entity registry, so a
  report can include an empty-panel run summary directly.
- `MSA6` — Numeric parity snapshot test on a reference empty-panel
  run (§3 criterion 4).

### 6.7 Automatic treatments (M, revives §7.7) — `vieweb-legacy`

- `AT1` — `Nieweb.Scheduling.BackgroundService` with 5-minute wake
  cycle (Vieweb minimum). Row-level lease using
  `SELECT ... FOR UPDATE SKIP LOCKED` on Postgres and a service-wide
  `SemaphoreSlim` on SQLite.
- `AT2` — `Nieweb.Mail` on MailKit. Idempotent per `(treatmentId,
  runTimestamp)`.
- `AT3` — `AutomaticTreatment` + `EmailRecipient` entities +
  admin UI (create / edit / disable). Per-treatment `IsEnabled`
  plus global `Nieweb:Batch:Enabled`.
- `AT4` — File-drop delivery to
  `Nieweb:BatchOutputDirectory` (default
  `%ProgramData%\Nieweb\batch`).
- `AT5` — Failure surfacing: every delivery attempt writes an
  `automatictreatment.delivery.ok` or
  `automatictreatment.delivery.failed` audit row with SMTP
  transcript headers. Admin UI badge lights when there are
  failures (Vieweb bug #9699 mitigation).
- `AT6` — Regression tests for Vieweb bugs #9699 (email failure)
  and #12421 (weekly ≠ Σ daily). Both would have caught the legacy
  behaviour.
- `AT7` — Eight-week unattended run on staging (§3 criterion 5).

### 6.8 Locales (S, revives §9) — `sigmalink-legacy`

- `LOC1` — Bundle scaffolding for `de`, `es`, `zh-Hans` under
  `src/Nieweb.Web/src/i18n/locales/`. Matches the Sigmalink 1.6.5
  locale set. Server-side messages ship alongside.
- `LOC2` — Missing-key gate in CI so a new UI string can't ship
  without translations. Same gate already covers EN / FR.
- `LOC3` — Translator handoff — CSV export of every key + English
  value + optional context note. Roundtripped via `npm run
  i18n:import`.

### 6.9 Sigmalink coexistence + retirement (M) — `sigmalink-legacy`

- `SIG1` — Reverse-proxy plan. During coexistence, IIS routes
  `/analyse/*` and `/sigmalink/*` to the legacy installs while
  `/app/*` and `/api/*` hit Nieweb. Documented in `docs/deploy.md`.
- `SIG2` — Data-parity dashboard. A small internal dashboard that
  computes KPIs from both sources for the same time window and
  raises if they disagree — used during the coexistence weeks.
- `SIG3` — Cut-over checklist per absorbed module (Review, Analyse,
  Board Trace). Each cut-over is reversible for two weeks. The rest
  of Sigmalink (Data Import, Configure, Monitor) is not in the
  checklist — those modules stay untouched (§1).
- `SIG4` — Reverse-proxy retirement automation. PowerShell scripts
  under `tools/deploy/sigmalink-decommission/` that stop routing
  `/analyse/*` and (optionally) `/sigmalink/review/*` to the legacy
  installs and back up their state directories off-machine before the
  customer's ops team decides what to do with the Sigmalink process
  itself. Whether Sigmalink Jetty stays running for Data Import is
  a customer-ops decision, not a Nieweb decision.

### 6.10 Test coverage (S)

- `TC1` — Playwright E2E smoke per absorbed module (Board Trace
  barcode lookup, Review inline verdict, Analyse Live load, MSA
  report render).
- `TC3` — Contract tests between `IAoiSource` / `IKSource` /
  `IMsaSource` implementations and their fakes.
- `TC4` — Data-parity snapshot tests: for the same reference week,
  each Nieweb Analyse dashboard's numeric output matches a captured
  Sigmalink Analyse dump.

### 6.11 Documentation (S)

- `DOC1` — `docs/board-trace.md` — UX flow, cross-DB stitching
  strategy, defect bit-flag decoding, Board SVG overlay contract.
- `DOC2` — `docs/review.md` — state machine, XML schemas honoured,
  mode differences, OIS export contract.
- `DOC3` — `docs/analyse.md` — dashboard-by-dashboard KPI definitions,
  and DBQuery-K back-end shapes.
- `DOC5` — Update `docs/deploy.md` with the reverse-proxy plan and
  the per-module cut-over checklists for Review + Analyse + Board
  Trace.

---

## 7. Risks & mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Board Trace slow / spinner-heavy on cold cache. | QA operators bounce back to whatever they used before. | §3 criterion 8 has an explicit p95 < 500 ms warm / < 2 s cold target. Materialised views + a small in-memory panel-cache warm on scan. Load test in `TC*`. |
| Cross-DB stitch produces duplicate or misaligned events (pre-reflow row without a matching post-reflow row, or vice versa). | Board Trace timelines look wrong; loss of trust. | `BT3` explicitly matches on `(Barcode, Panel_Numeric_Date)`, and unmatched rows render as their own step rather than being silently hidden. Snapshot test on a curated fixture week. |
| Sigmalink XML layout files don't parse the same in Nieweb. | Customer must re-configure Review from scratch. | `REV6` explicitly parses the Sigmalink XML files as-is; parity test fixtures pulled from `VIT_Sigmalink/conf/`. |
| Analyse KPI drift vs Sigmalink Analyse during coexistence. | Loss of trust; blocks retirement of Analyse WAR. | `SIG2` data-parity dashboard runs continuously; `TC4` snapshot tests fail CI on drift. |
| MSA DB commissioning slips again. | `MSA*` items can't ship. | Land the DB-adapter shell and MSA UI in an `IsEnabled=false` state; block only the report render on real data. Keep Process Capability placeholder from Phase 2 rather than regressing. |
| SMTP credentials still not confirmed by go-live. | Automatic treatments can't email. | File-drop delivery works without SMTP; ship `AT4` first, email later. |
| Read-only discipline forgotten in a new DBQuery-K or MSA path. | Could write to a Superviseur DB. | Reference guard in `tools/db/probe-schema.ps1` is re-used; every new adapter must reuse `SqlServerAoiSourceBase` (which enforces `WITH (NOLOCK)`, isolation level, timeouts, and `ApplicationName`). Enforced by architecture test in CI. |

---

## 8. Deferred to Phase 4 (or later)

- **Dual-lane Review conveyor UI** — needs the Zebra printer +
  IO board pair we don't have on staging, and the two design
  partners running dual-lane haven't asked us to take it over yet.
- **Sigma Connect AMQP replacement** — Sigmalink used QPID for
  cross-machine messaging (event bus, remote review notifications).
  Revisited only if a customer topology requires broker semantics.
- **Full offline review from a remote workstation** — Nieweb ships
  offline-on-workstation (`REV3`) but not the disconnected-remote
  case where a workstation has no LAN link to Nieweb for hours.
- **JEDEC / part-number library editor** — Sigmalink included a
  library manager. Deferred until a customer asks; Nieweb reads
  library rows read-only from the Superviseur DB in the meantime.
- **Glue-deposit family editor** (`glue_deposits.xml`) — narrow
  customer footprint; deferred.
- **Browser-native CAD editor** — not on Nieweb's roadmap. Nieweb is
  a data-analysis / traceability tool, not a Sigmalink replacement.
  If a customer ever needs to leave Sigmalink entirely, that becomes
  a separate project.
- **PCB Image Recorder / Image Matrix editing UI** — stays with
  Sigmalink Data Import. Nieweb doesn't consume these artefacts.
- **Additional locales beyond DE / ES / ZH** — JA, KO, PT-BR, RU
  are listed in `artifacts/publish/` as scaffolds only. Not
  guaranteed complete until a design partner needs them.

---

## 9. Open questions

### 9.1 Resolved (record as they land)

- **Should Board Trace be reachable without login?** _Resolved
  2026-07-23._ Yes — via an admin-toggled **kiosk mode** scoped to
  `/app/board-trace` and `/app/board-trace/<barcode>` only. Rate
  limiter, PII redaction, source-IP audit, and a visible "read-only"
  banner ship with the feature. See §6.1 `BT6`–`BT8`.

### 9.2 Still open

1. **SMTP host + credentials.** Rolls over from phase-2 §10.2 Q1.
   Interim: `Nieweb.Mail` compiles against `ISmtpDelivery`; the
   choice of anonymous relay vs authenticated submission becomes
   an ops-time configuration.
2. **Do we support Sigmalink's licence-file model, or just token
   flags in the internal DB?** Draft answer: token flags only
   (§2.3), keep the door open for a signed-file model later if a
   customer demands it.
3. **Empty-panel MSA DB commissioning date.** Blocks `MSA*` from
   moving past scaffolding. Tracked with QA lead.
4. **Zebra label ZPL template.** Reuse Sigmalink's, or design a new
   one? Reuse buys migration; new gets us the Nieweb branding on
   labels.
5. **Sigmalink dependency freeze.** Sigmalink continues to run
   independently for Data Import (§1). During coexistence for the
   modules Nieweb is absorbing (Review + Analyse), Sigmalink writes
   to its own HSQLDB/PostgreSQL. Do we need any coordination between
   Sigmalink's internal DB and Nieweb's internal DB, or is complete
   independence fine? Draft answer: complete independence.

---

## 10. Cross-references

- `docs/tech-stack.md` — stack decisions (SIGNED-OFF).
- `docs/phase-1-mvp.md` — vertical slice groundwork.
- `docs/phase-2.md` — Vieweb parity + report infrastructure Phase 3
  depends on (report tile canvas, admin surfaces, PDF pipeline,
  audit log, OpenTelemetry).
- `docs/deploy.md` — updated by Phase 3 `DOC5` with reverse-proxy
  and per-module cut-over checklists.
- Skills consulted throughout: `sigmalink-legacy`, `sigmalink-review`,
  `sigmalink-analyse`, `vit-aoi-database`, `aoi-quality-metrics`,
  `vieweb-legacy`. `sigmalink-cad-import` is not consulted — Nieweb
  does not read CAD-authored data (§1).
