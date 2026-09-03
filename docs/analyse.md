# Nieweb Analyse — dashboard companion (DOC3)

_Phase 3 §6.11 DOC3. AOI-only Analyse dashboards under `/app/analyse/*`
(ANA1–ANA6). Status: **delivered on `phase-c`** — Live, Line Performance,
Product (+ detail drilldown), Panel, Cp/Cpk._

## 1. Scope

Nieweb Analyse is the AOI-only port of Sigmalink Analyse (§6.3). It feeds
exclusively from the AOI Superviseur DBs via `IAoiSource` (DBQuery-K
equivalent) — no DBQuery-Pi, no SigmaLine feedforward, no SPI↔AOI mapping
(all cut per §1 / §6.4–§6.5). SPI/PI work is explicitly out of scope.

Legacy reference: `sigmalink-analyse` skill; `VIT_Analyse/app/analyse.war`
(port 8082) is retired per ANA7 after parallel running.

## 2. Data model

Every dashboard builds on the same three streams (see `vit-aoi-database`):

| Stream | Source table | Grain | Notes |
|---|---|---|---|
| Panels | `PANELS` | one row per inspection | `Panel_Status` drives FPY; `Panel_BarCode` + `Face_Number` drive dedupe |
| Cards | `CARDS` | one row per sub-panel | `Nb_Of_Tests_On_Comp` is the component-DPMO denominator |
| Tested objects | `TESTED_OBJECT` | one row per component/pad | `Error_Table` defect bits drive DPMO/Pareto; `Delta_*` drives Cp/Cpk |

**Read-only discipline** (§2.3): `WITH (NOLOCK)`, `READ UNCOMMITTED`,
30 s timeout, `ApplicationName='Nieweb-…'`, time-window filter, per-query
audit row. Never write to either Superviseur DB.

**Last-inspection dedupe.** Post-reflow (`HLYAOI2024`, schema 5.0) exposes
`IS_LAST_INSPECTION` and dedupes at the DB level. Pre-reflow (`MEAOI`,
schema 4.3.1) lacks it, so reports fall back to in-memory dedupe by
`(Panel_Bar_Code, Face_Number)` keeping the latest `Panel_Numeric_Date`
(tie-break: highest `Panel_Id`). The UI surfaces a "Fallback dedupe mode"
banner whenever the fallback fires.

## 3. Dashboards

### ANA2 — Live (`AnalyseLiveSummaryReport`)

Headline counters over the window: total / inspected / good / faulty /
not-inspected panels + FPY %. Status mapping: good = `1,2,3`; faulty =
`-2,-1`; not-inspected = `0` (plus unknown codes). `3` covers the
pre-reflow-only status value.

- API: `GET /api/analyse/live-summary`
- UI: `analyse-live-summary-card` on `/app/analyse`

### ANA3 — Line Performance (`AnalyseLinePerformanceReport`)

Overall FPY + component DPMO plus per-machine rows (machine name resolved
via `ListMachinesAsync`, null when the machine catalogue lacks the id).
DPMO denominator: Σ `CARDS.Nb_Of_Tests_On_Comp`; numerator: set-bit count
of `TESTED_OBJECT.Error_Table` on component rows (`Object_Type_Id & 1`).

- API: `GET /api/analyse/line-performance-summary`
- UI: `analyse-line-performance-card`

### ANA4 — Product (`AnalyseProductSummaryReport` + `AnalyseProductDetailReport`)

Per-product FPY / component DPMO / defect-bit Pareto preview, sortable by
defects / FPY / DPMO. Detail route adds a Day/Week trend (FPY + DPMO per
bucket via `TimeBucketer`), an ECharts line chart, and per-bucket top
defect bits.

- API: `GET /api/analyse/product-summary`,
  `GET /api/analyse/product-detail/{productId}?bucket=Day|Week`
- UI: `analyse-product-summary-card` + `/app/analyse/product/$productId`
  (`AnalyseProductDetailChart`, bucket selector)

### ANA5 — Panel (`AnalysePanelSummaryReport`)

Analyst-oriented worst-panel ranking (top 50 by defect-bit count) with
product/machine names and top-3 defect bits — the complement to Board
Trace (operator-oriented single-barcode view). Each row deep-links to
`/traceability/board?barcode=…` for defect list → repair sanction.

- API: `GET /api/analyse/panel-summary`
- UI: `analyse-panel-summary-card` with defects / barcode / date sort

### ANA6 — Cp/Cpk (`AnalyseCpCpkReport`)

Process capability per deviation axis × opportunity (Components/Paste)
from `TESTED_OBJECT.Delta_*` samples. Canonical formulas
(`aoi-quality-metrics`, do NOT re-derive):

- `Cp = IT / (6σ)`
- `Cpk = min(IT/2 − d̄, IT/2 + d̄) / (3σ)`

σ is the Bessel-corrected sample std-dev (Welford online). IT comes from
AppParameter (`tolerance.{component,paste}.{itx,ity,its}`, mm→µm for X/Y;
Surface passes through); 0/missing means "not configured" → null Cp/Cpk
with a badge. Theta/Thickness have no tolerance key and always report
unconfigured. Legacy Cp/Cpk was SPI-only (PIN_MEASURE by tolerance
group); the AOI-only port uses placement/print deviations instead.

- API: `GET /api/analyse/cp-cpk`
- UI: `analyse-cp-cpk-card` (10 axis cards)

## 4. DBQuery-K back-end shape

There is no separate `Nieweb.DataSources.K` process — the Minimal-API
group `AnalyseEndpoints` (`/api/analyse/*`, `RequireAuthorization`) plays
the DBQuery-K role, with reports as pure functions
`(source, filter, parameters) → typed DTO`:

| Endpoint | Report | Filter |
|---|---|---|
| `GET /contracts` | `AnalyseDashboardContractsReport` | `AnalyseDashboardFilter` |
| `GET /live-summary` | `AnalyseLiveSummaryReport` | `AnalyseDashboardFilter` |
| `GET /line-performance-summary` | `AnalyseLinePerformanceReport` | `AnalyseDashboardFilter` |
| `GET /product-summary` | `AnalyseProductSummaryReport` | `AnalyseDashboardFilter` |
| `GET /product-detail/{id}` | `AnalyseProductDetailReport` | `AnalyseProductDetailFilter` (+ Day/Week bucket) |
| `GET /panel-summary` | `AnalysePanelSummaryReport` | `AnalyseDashboardFilter` |
| `GET /cp-cpk` | `AnalyseCpCpkReport` | `AnalyseCpCpkFilter` (+ tolerance intervals) |

Shared envelope: `AnalyseDashboardFilter(Window, MachineIds?,
ProductIds?, OnlyLastInspection=true)`. Window parsing defaults to the
last day; `startUtc ≥ endUtc` is 400. Unknown `sourceId` is 404.
`product-detail` rejects buckets other than Day/Week with 400.

Capability gates surface via `/contracts` (`latest-inspection-filter`,
`machine-efficiency-time-pie`); Panel and Cp/Cpk are always supported.

## 5. KPI definitions

All formulas are canonical per `aoi-quality-metrics` — never re-derive:

- **FPY** = good / inspected × 100 (panel-level, `Panel_Status`).
  Aggregate raw counts first; never average percentages (bug #12421).
- **DPMO** = 1e6 × defect bits / Σ `Nb_Of_Tests_On_Comp` (board-level,
  components only in Analyse; paste excluded).
- **Cp / Cpk** — see §ANA6 above.
- **Defect ordering** — display order from AppParameter
  `analyse.defect_order` (JSON array of `DefectBit` names); unknown names
  ignored. Runtime-configurable, never hard-coded (§2.3).

## 6. Intentionally dropped (Sigmalink features not ported)

| Sigmalink feature | Why dropped |
|---|---|
| SPI Live widgets (stencil scatter, volume-per-panel, squeegee) | SPI/PI out of scope (§6.4–§6.5) |
| `analyse_layout.xml` widget grid | Replaced by fixed Mantine tile composition (§3 criterion: no pixel-parity) |
| Correlated-defects / SigmaLine chart | Needs SPI+AOI join; no SPI path |
| Panel Summary CAD overlays (nature colour scales, offset arrows) | Covered by Board Trace SVG overlay (BT4) instead |
| Cp/Cpk radar + histograms + panels/result tables | Replaced by per-axis Cp/Cpk cards; histograms live in the Deviation chart |
| `Advanced Analyse Pi/K` licence split | Single `license.analyse.enabled` token (§2.3) |
| `VIT_Analyse.war` standalone server (port 8082) | Retired per ANA7; Nieweb serves `/app/analyse/*` directly |

## 7. Tests

- Report unit tests + snapshots: `tests/Nieweb.Reports.Tests/Analyse*`
  (Live, LinePerformance, ProductSummary, ProductDetail, PanelSummary,
  CpCpk) over `FakeAoiSource` (pre-reflow dedupe vs post-reflow raw).
- Endpoint tests: `tests/Nieweb.Api.Tests/Endpoints/AnalyseEndpointsTests`
  (401 wall, dedupe behaviour, invalid bucket → 400, row-shape parity).
- SPA tests: `src/Nieweb.Web/src/routes/analyse.test.tsx` (source
  auto-select, sort controls, detail navigation + bucket switch, Panel
  and Cp/Cpk cards).
- E2E: `src/Nieweb.Web/e2e/analyse.spec.ts` (Panel worst-first ordering
  + Cp/Cpk 10-row shape against `FakeAoiSource`; requires a green SPA
  build — currently blocked by pre-existing TS errors, tracked
  separately).
