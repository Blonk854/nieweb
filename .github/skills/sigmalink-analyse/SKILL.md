---
name: sigmalink-analyse
description: |
  Deep expertise on the Sigmalink Analyse module (a.k.a. Sigma Analysis,
  Sigmalink Analysis, VIT_Analyse). Covers the standalone analyse.war
  server (default port 8082), the Live / Line Performance / Product /
  Panel / Cp-Cpk dashboards, DBQuery Pi and DBQuery K back-ends,
  dbquery-pi-client-ipccamx, sigmalink_configuration.xml's <analyse> node,
  dbqueryK / dbqueryPI defect ordering, feedforward status filter,
  panel-side mapping (SPI ↔ AOI), analyse_layout.xml widget grid,
  correlated defects, Cp/Cpk histogram/radar/panels/result tables,
  measure natures (Volume, Height, Area, Offset X, Offset Y, Theta),
  Advanced Analyse Pi/K license, DBQuery-PI-updater, Line Performance
  combined yields, SigmaLine correlated defects chart. Use this skill
  whenever a question or task involves the Sigmalink Analyse dashboards,
  Analyse WAR install/config, DBQuery servers, DPMO / FPY / Cp / Cpk
  visualisation, or when porting these analytics into Nieweb.
---

# Sigmalink Analyse — Legacy Expert Skill

Authoritative reference for the Sigma Analyse companion webapp
(`VIT_Sigmalink/VIT_Analyse/app/analyse.war`) and the Analyse module
surfaced inside Sigmalink itself. Sources:
`pdf_text/Analyse-user-guide-V1.6.5.txt`,
`pdf_text/Analyse-release-note-V1.6.5.txt`,
`VIT_Sigmalink/1.6.5/DBQuery-PI-updater-V1.6.5/`, and
`conf/global/sigmalink_configuration.xml`.

## 1. Architecture

```
   Sigmalink Client (browser) ─── HTTP ───► Sigmalink Server
                                              │
                                              ├── HTTP ──► Analyse Server (port 8082)
                                              │              │
                                              │              ├── HTTP ──► DBQuery K (K machine, /dbquery)
                                              │              └── HTTP ──► DBQuery Pi (PI machine, /dbquery)
                                              │                            │
                                              │                            └── SQL ──► PI DB
                                              └── SQL ──► K Supervisor DB (Vision3D CR4/CR5)
```

- **Analyse Server** — a separate installer (`C:\VIT_Analyse`), a WAR
  (`app/analyse.war`), embedded Jetty; check with
  `http://<host>:8082/analyse/version` → response contains e.g. `DBQuery: 1.6.4`.
- **DBQuery Pi** — installed **on every SPI Pi machine**; check with
  `http://<pi-host>:8080/dbquery/version` → e.g.
  `DBQuery: 1.6.4 - OctopusRemoteAccess: 1.1.10 - Database: 1.74`.
- **DBQuery-PI-updater-V1.6.5** — Windows installer that upgrades the
  DBQuery Pi component on Pi machines without touching the Vision app.
- No shared folder is exposed by the PI machine — all Pi data is fetched
  over HTTP by the Analyse server. K data is read directly from the
  Vision3D CR4/CR5 supervisor database (see `vit-aoi-database`).

Licenses required to reach the Analyse module from a Sigmalink client:
`Analysis Pi`, `Advanced Analysis Pi`, `Analysis K`, `Advanced Analysis K`.

## 2. Sigmalink → Analyse wiring

The Sigmalink server points at the Analyse server via
`conf/global/sigmalink_configuration.xml`:

```xml
<analyse host="172.25.72.13" port="8082"/>

<dbqueryK>
  <defectOrders>
    <defect>Missing</defect>           <!-- canonical K defect order -->
    <defect>Polarity</defect>
    <defect>Solder Joint</defect>
    <defect>Solder Bridge</defect>
    <defect>OCV</defect>
    <defect>Delta X</defect>
    <defect>Delta Y</defect>
    <defect>Delta Theta</defect>
    <defect>Tilt</defect>
    <defect>Thickness</defect>
    <defect>SPI defect</defect>
    <defect>Side Overhang</defect>
    <defect>Length Overhang</defect>
    <defect>Foreign Material</defect>
    <defect>Component Present</defect>
    <defect>Lifted Lead</defect>
  </defectOrders>
  <parameters>
    <parameter key="LIVE_TIME_RANGE"        value="7"/>   <!-- hours -->
    <parameter key="TOP_DEFECT_ORDER_RANGE" value="1.0"/>
    <parameter key="TOP_DEFECT_TIME_RANGE"  value="8"/>   <!-- hours -->
  </parameters>
</dbqueryK>

<dbqueryPI>
  <defectOrders>
    <defectsOrder>Coplanarity</defectsOrder>
    <defectsOrder>Volume</defectsOrder>
    <defectsOrder>Height</defectsOrder>
    <defectsOrder>Area</defectsOrder>
    <defectsOrder>Position</defectsOrder>
    <defectsOrder>Shape</defectsOrder>
  </defectOrders>
  <parameters>
    <parameter key="LIVE_TIME_RANGE"        value="7"/>
    <parameter key="TOP_DEFECT_ORDER_RANGE" value="1.0"/>
    <parameter key="TOP_DEFECT_TIME_RANGE"  value="8"/>
  </parameters>
</dbqueryPI>

<feedforward>
  <status/>
  <components>
    <excludes/><includes/>
  </components>
  <notifications component="true" panel="true"/>
</feedforward>

<panelSideMapping>
  <side eqp="spi" top="0" bottom="1"/>
  <side eqp="aoi" top="1" bottom="0"/>
</panelSideMapping>
```

The user guide's guide-configured "Defect Order (K)" is defined in this
file — Analyse charts use it to classify each component's most-serious
defect without double-counting. The guide's canonical K order is:
Missing, Polarity, Solder Joint, Solder Bridge, OCV, ΔX, ΔY, ΔTheta,
Connector, Others (the shipped file adds Tilt/Thickness/SPI defect/
Side/Length Overhang/Foreign Material/Component Present/Lifted Lead).
PI order: Bridge, Missing, Co-planarity, Volume, Height, Area, Position,
Shape, Others, Stencil.

`panelSideMapping` matches the AOI side name to the SPI side name so that
combined SPI+AOI panels reconcile by (id-code, side).

## 3. Modules

### 3.1 Live module

Per-equipment real-time dashboard. Two variants:

**SPI Live** widgets: FPY gauge (panel + subpanel), Stencil offset scatter,
Yields+DPMO (last 2.5 h), Volume-per-panel curve with Q25/Q75 whiskers,
Top-defect trend by TYPE (Good-operator vs KO/KO-operator), Production
history (last 10 panels), Inspection result bar (last 10 panels with
PASS/KO_OP/KO/OK_OP/OK), Top-10 JEDEC with defects.

**AOI Live** widgets: FPY gauge, Yields+DPMO, Top-defect trend by TYPE
(good vs KO), Top-defect trend by JEDEC (good vs KO), Top-10 JEDEC.

Sampling: DPMO trend uses 15-minute buckets over the last 8 h.

Layout customisable via `conf/statistics/analyse_layout.xml`:

```xml
<layouts>
  <live>
    <ALL>...</ALL>                <!-- reserved -->
    <K>
      <graphs layout="3H_3H">     <!-- 3H_3H or 2H_2H -->
        <graph name="K_fpyGauge"/>
        <graph name="topDefect">
          <parameters>
            <parameter key="category" value="defecttype"/>  <!-- jedec|partnum|topo|defecttype -->
            <parameter key="status"   value="okop"/>        <!-- ok|okop|ko -->
          </parameters>
        </graph>
        ...
      </graphs>
    </K>
    <PI> ... </PI>
  </live>
</layouts>
```

Allowed graph names:
- **K**: `K_fpyGauge`, `K_liveFPYDPMO`, `K_liveInspResult`, `K_livePareto`
- **PI**: `PI_fpyGauge`, `PI_liveFPYDPMO`, `PI_liveInspResult`,
  `PI_livePareto`, `PI_liveStencil`, `PI_liveVolume`
- **Both**: `topDefect` (parametrised by `category` and `status`)

### 3.2 Line Performance module

Line-level rollup over a chosen period (presets or custom min/max). Filters
by status: SPI = `Good operator | Acceptable | Warning | Not Good Operator |
Not Good`; AOI = `Good operator | Repaired | Not Good Operator | Not Good`.

Widgets: Combined Yields (one chart per equipment in a vertical stack),
SigmaLine Correlated Defects chart, Panel Status pie, Line Performance
Time pie (inspection / conveying / smema / review, in seconds and %),
Defect-per-type SPI, Defect-per-type AOI (bars ascending, glue vs paste
side-by-side if applicable), Subpanel Status pie, Top-5 Programs (horizontal
bars, variants on separate bars), Top-10 JEDEC, Dashboard Table, Summary
Table, Log Table.

### 3.3 Product module

Same period filter + program/batch selection. Adds Defects-per-group
(tolerance group), Top-10 Components-in-error, Panels Table with per-panel
inspection date / id-code / side / lot / status / errors / warnings /
duration / review status / operator / squeegee direction / batch / lot /
iteration error type / variant. Also Log Table, Version Table (program
versions inspected in period), Measures Table (per pin / pad measurement
with offset X µm, offset Y µm, Shape2D %, Shape3D %, Area %, Volume %,
Height %), Errors Table, Warning Table.

Drill-down: "Defect per type" bars → drill to "Top 5 programs" → click a
program → jumps to Product for that program.

### 3.4 Panel module (rewritten in 1.6.5)

Panel-level analysis with a single view spanning every equipment of a line;
each equipment gets a tab (Summary / Charts / Errors / Warnings / Measures /
History). Correlated-defect table joins per-defect status across equipment.

Selected panel filters differ per program type:
- **SPI-only extra filters**: Variants, Batches, Squeegee direction; status
  set adds `Warning` and `Acceptable`.
- **AOI-only extra filters**: Lots, OCR.

Equipment icon decoration on Program / Line / Panel id-codes marks which
equipments produced data:
- SPI, AOI Pre-reflow, AOI Post-reflow, or any combination.

Summary CAD view supports colour-scaled "Nature" overlays:
- SPI: Volume, Height, Area, Offset (with arrow), plain defect.
  Colour scale: lowest=blue, 100%=white, highest=red (100=−lowest).
  Offset arrow: 0–50 px, saturates at max-of-(100 µm, most-important-offset).
- AOI: Theta, Offset (with arrow), plain defect.
  Angle colour scale: 0=white, 3° or max=red.

Zoom is bounded: min = full CAD, max = 5 µm/px.

### 3.5 Cp/Cpk module (SPI only)

Filters: Program (required), tolerance Group (required), Profile version,
Variants, Batch, plus comma-separated lists for subpanel/JEDEC/PartNumber/Topo.

Widgets: Cp/Cpk Radar view, Cp/Cpk Histograms Status per nature (Height,
Volume, Area, Offset X, Offset Y — histograms with theoretical Gauss curve
matching the observed µ and σ; red vertical lines mark advanced defect
thresholds when defined), Cp/Cpk panels table, Cp/Cpk result table (per
nature: Min, Max, Average, Std deviation, Cp, Cpk).

For Cp / Cpk formulas defer to the `aoi-quality-metrics` skill — Sigmalink
uses the same VIT canonical definitions and computes them from PIN_MEASURE
rows filtered by tolerance group, program, and variant.

## 4. Data model touchpoints

Analyse never persists to the Superviseur DB. It reads:

- **K DB** (Vision3D CR4/CR5) — `PANELS`, `CARDS`, `TESTED_OBJECT`,
  `TESTED_OBJECT_HISTO`, `MACHINE`, `PRODUCT`, `RECIPE`, `LIBRARY`,
  `OPERATOR`, `TOLERANCE`, `PART_NUMBER`, `JEDEC`. See `vit-aoi-database`
  for column-level docs. Anomaly bit-flags decoded per that skill.
- **Pi DB** (via DBQuery HTTP) — same table structure with `PIN_MEASURE`
  populated for paste-deposit measures.

Panel status is decoded from `PANELS.Panel_Status` (enum -2..3) and the
`Anomaly_BR/AR` bit-fields.

## 5. Live "Header" contents

Program name and batch / lot name — driven by
`equipment/@parameters/@prog_matcher` regex applied to the TST file name
in `tst_folder_path` (see `sigmalink-review` §3).

## 6. Tables — download & export

Every table has a CSV download button. Every chart can export as PNG /
JPEG / PDF / SVG. Both are baseline behaviour that Nieweb should preserve.

## 7. Ports & health-checks

| Component               | URL                                          |
|-------------------------|----------------------------------------------|
| Sigmalink server        | `http://<host>:8080/sigmalink` (or `/SigmaLink`) |
| Analyse server version  | `http://<host>:8082/analyse/version`         |
| DBQuery Pi version      | `http://<pi-host>:8080/dbquery/version`      |

## 8. Modernization notes for Nieweb

- **Consolidate Analyse into Nieweb** rather than running a second Jetty
  process — the license-token model can be reused inside Nieweb.
- **Replace DBQuery Pi HTTP polling** with either direct Pi DB access (if
  Nieweb has credentials) or a modern message-bus feed via Sigma Connect.
- **Reuse the defect-order XML** as a Postgres table so line engineers can
  edit it without touching disk; the Analyse guide explicitly encourages
  per-installation customization.
- **Fix Bug parity items** — the release notes list "New Panel Analysis"
  (single-view merge across equipment) as 1.6.5's headline feature; Nieweb
  should ship that as the default (not a tab-per-equipment) with lazy
  fetch per equipment.
- **Preserve KPI formulas** from `aoi-quality-metrics` — line engineers
  compare Sigmalink and Nieweb totals daily. Never average FPY percentages;
  always aggregate raw counts first (this is a direct fix for legacy
  Vieweb bug #12421).
- Panel-side mapping is under-documented in the guide — expose it as a
  UI setting with a live "test with panel X" preview.
