---
name: aoi-quality-metrics
description: 'Formulas and interpretation for the AOI reporting KPIs used by Vieweb / Nieweb: FPY (AOI, Diagnostic, After-repair), DPMO and its sub-flavors (defects components, defects paste, real defects, false / dummy defects), PPM, Cp, Cpk, EV, %EV, GR&R (repeatability & reproducibility), MSA classification of components/packages into OK / Acceptable / In error. Use when: implementing a chart/table/process-capability entity; deciding what to divide/multiply by; converting between panel-level and board-level counts; interpreting Machine efficiency and Average cycle duration; applying the ITx / ITy / ITS tolerance intervals; computing 6σ or ±3σ overlays on deviation charts. All formulas are canonical (defined by VIT in Vieweb-user-guide-V1.6.2 §3 & appendix) — do NOT re-derive.'
---

# AOI reporting KPIs — canonical formulas

Source: `Vieweb-user-guide-V1.6.2.pdf` (§3, §Appendix glossary),
extracted at `pdf_text/Vieweb-user-guide-V1.6.2.txt`.

> Do not invent alternative definitions. Line engineers cross-check Nieweb
> figures against Vieweb 1.6 numerically. If a value drifts by more than a
> rounding error the report is treated as broken.

## Glossary shorthand

- **Panel** — a group of boards fed into the AOI as a single unit.
- **Board / Card / sub-panel** — one populated PCB within a panel.
- **Tested object** — one inspected component, paste pad, macro, or foreign
  material row in `TESTED_OBJECT`.
- **σ** — standard deviation of the measured deviations.
- **IT** — Interval of Tolerance (customer-set application parameter).
- **ITx**, **ITy**, **ITS** — separate IT for X, Y and Surface deviations,
  configured separately for paste pads and components under
  `Parameters > Application parameters`.
- **Real defect** — defect NOT overridden as dummy by the review operator.
- **Dummy fault / dummy false** — defect that the review operator
  reclassified as "not a real defect" (also called *false call*).

## FPY — First Pass Yield (%)

Universal shape:

$$
\text{FPY} = \frac{\text{good units}}{\text{inspected units}} \times 100
$$

where a "unit" is a panel or a board (customer's choice at entity
configuration time). Three flavors:

- **FPY AOI** — numerator counts units that AOI flagged as good.
  Reflects AOI's raw view; a high false-call rate lowers this.
- **FPY Diagnostic** — numerator excludes units whose only defects were
  reclassified as dummy faults during review. Reflects **true product
  quality**. This is the number quality engineering cares about.
- **FPY After Repair** — numerator counts units that ended good after
  operator repair actions. Reflects post-repair yield to the next stage.

Panel-level counts use `PANELS.Panel_Status`; board-level counts use
`CARDS.Card_Status` (see `vit-aoi-database` skill for the enum values).

Legacy bug **#12421** (weekly total ≠ daily totals) lived in this
aggregation — always aggregate raw counts first, compute the ratio last.
Never average FPY percentages.

## DPMO — Defects Per Million Opportunities

$$
\text{DPMO} = \frac{\text{number of defects}}{\text{total number of tests}} \times 10^{6}
$$

- "Number of defects" = count of failing bits set across
  `TESTED_OBJECT.Error_Table_AR` (or `Error_Table` if analyzing AOI
  performance), summed for the panels in scope.
- "Total number of tests" = per-sub-panel
  `CARDS.Nb_Of_Tests_On_Comp + CARDS.Nb_Of_Tests_On_Pads` (or the
  appropriate subset for the flavor).

Available flavors in Vieweb (also produced by Nieweb):

| Flavor | Numerator | Typical denominator |
|---|---|---|
| **DPMO defects** | All defects (components + paste) | All tests |
| **DPMO defects components** | Component defects only | `Nb_Of_Tests_On_Comp` |
| **DPMO defects paste** | Paste-pad defects only | `Nb_Of_Tests_On_Pads` |
| **DPMO real defects** | Only defects not reclassified as dummy | Same as flavor above |
| **DPMO real defects components** / **DPMO real defects paste** | Real-defect subset per opportunity kind | Component / paste tests |
| **DPMO false defects** / …components / …paste | Only dummy-fault reclassifications | Same denominators |

DPMO only applies to boards (per user-guide §3.1.4.6). Do not present a
"panel DPMO".

## PPM — Parts Per Million

$$
\text{PPM} = \frac{\text{number of defects}}{\text{number of components + pads}} \times 10^{6}
$$

Divisor is objects, not tests. Use PPM when the audience wants "how many
placed parts were defective per million placements".

## Cp — Capability

$$
Cp = \frac{IT}{6\sigma}
$$

- IT = configured tolerance interval for the axis being measured (ITx, ITy,
  or ITS for surface).
- σ = standard deviation of the observed deviations in the scope. Use
  `PANELS.Components_StdDev_*` / `CARDS.Components_StdDev_*` or
  `..._StdDev_Surf` where already pre-aggregated by the AOI, or recompute
  from `TESTED_OBJECT.Delta_*`.

Higher Cp = tighter dispersion vs tolerance. `Cp ≥ 1.33` is a common
target; MSA-limit thresholds in `Parameters > MSA limits` decide
OK / Acceptable / Out coloring (defaults in `vieweb-legacy` skill).

## Cpk — Process Capability Index (centered)

$$
Cpk = \min\!\left(
\frac{IT/2 - \overline{d}}{3\sigma},\;
\frac{IT/2 + \overline{d}}{3\sigma}
\right)
$$

- `\overline{d}` = mean of the deviations on the axis in scope
  (`Components_AvgDev_*`, or recomputed).
- Same σ as Cp.
- Cpk ≤ Cp; equal only when the process is perfectly centered.

Cpk exists per axis (X paste, Y paste, Surface paste, X component,
Y component, Theta component) and drives the Cp/Cpk radio buttons in the
Trend / Process Capability entities.

## Deviation charts — overlays

For deviation charts on `X`, `Y`, `Z`, `Surface`, `Theta` (per component)
overlay: `+ tolerance`, `− tolerance`, average, `+3σ`, `−3σ`.

For the averaged versions (`Average(X)`, `Average(Y)`, …, per panel)
overlay only the average and `±3σ` (no tolerance line — tolerances are
per-object).

The X-axis for per-object charts is the reference designator; for
averaged charts it is the panel.

## MSA — Measurement System Analysis

MSA is only valid on a **dedicated database** populated by re-inspecting
the same panel repeatedly on the AOI. Analysis is by **Reference
Designator** or **Package (JEDEC)** and by axis (X / Y / Theta).

### EV — Equipment Variation (repeatability)

$$
EV = k_{\text{conf}} \times \sigma
$$

- `k_conf` = **Confidence coefficient** application parameter
  (default 4.33 in `ViewebParameters.properties`).
- σ = std-dev of the deviations across the repetitions of that reference
  designator (or package).

### %EV — normalized EV

$$
\%EV = \frac{EV \times 100}{\text{Tolerance EV}}
$$

`Tolerance EV` is an application parameter.

### GR&R — Gage Repeatability & Reproducibility

Legacy Vieweb uses the "range" form (per user-guide appendix, verbatim):

$$
GR\&R = \frac{4.33 \times \overline{|d_{i,i+n/2}|} \times 100}{IT}
$$

Given `n` measurements per component, split them into two ordered halves;
`d_{i,i+n/2}` is the absolute difference between the `i`-th element of the
first half and the `i`-th element of the second half. Take the mean of
those absolute differences (`Mean of differences`) and plug it in.

- 4.33 = the default GR&R constant (`defaultGR_R` in `ViewebParameters`)
  — **do not** change it silently; expose it as an application parameter
  so customers who calibrate against MSA-4 studies can override.
- IT = the tolerance interval for the axis under study.

Example (user-guide table) — 8 usable elements from a 9-element list:

```
list1: d1, d2, d3, d4          list2: d5, d6, d7, d8
diffs: |d5-d1|, |d6-d2|, |d7-d3|, |d8-d4|
Mean of differences = arithmetic mean of the 4 diffs
```

### MSA classification (per axis)

For each component or package the Cp/EV/GR&R value is compared to two
customer-defined thresholds (`Acceptable` and `Out`, configured under
`Parameters > MSA limits`):

- **OK** — every displayed axis (any subset of X, Y, Theta) is OK.
- **Acceptable** — no axis is In-Error, but at least one axis is
  Acceptable.
- **In error** — at least one axis is In-Error.

The MSA summary table shows counts and % of components per classification
for Capability, Repeatability and Reproducibility rows.

Default MSA limits (from user-guide §2.4.1) — keep as seed values in
Nieweb `parameter` table:

| Metric | Dev X `Accept` / `Out` | Dev Y `Accept` / `Out` | Dev θ `Accept` / `Out` |
|---|---|---|---|
| Average | 0.2 / 0.3 | 0.2 / 0.3 | 5 / 10 |
| Std Dev | 0.2 / 0.3 | 0.2 / 0.3 | 5 / 10 |
| 6σ | 1.2 / 1.8 | 1.2 / 1.8 | 30 / 60 |
| Cp | 8 / 10 | 8 / 10 | 8 / 10 |
| GR&R | 10 / 30 | 10 / 30 | 10 / 30 |
| EV | 0.1 / 0.3 | 0.1 / 0.3 | 0.1 / 0.3 |
| %EV | 10 / 15 | 10 / 15 | 10 / 15 |

Allowed ranges (validation):

| Metric | Dev X / Y range | Dev θ range |
|---|---|---|
| Average | [-10, 10] | [0, 360] |
| Std Dev | [-10, 10] | [-10, 360] |
| 6σ | [-60, 60] | [-60, 2160] |
| Cp | [0, 100] | [0, 100] |
| GR&R | [0, 100] | [0, 100] |
| EV | [0, 10] | [0, 10] |
| %EV | [0, 100] | [0, 100] |

## Process Capability entity — extras

Beyond Cp/Cpk/DPMO/FPY, this entity also exposes:

- **Number of inspections** — count of inspected panels or boards
  (`PANELS` or `CARDS` in scope) per AOI.
- **Average cycle duration** — mean of `PANELS.Test_Time` (seconds) for
  the AOI in scope over the reporting window.
- **AOI efficiency (%)** — percentage of the reporting window during
  which the AOI was inspecting. Practical formula:

  $$
  \text{AOI efficiency} = \frac{\sum \text{Test\_Time}}{\text{window length}} \times 100
  $$

  Guard against `> 100 %` (multi-lane machines can exceed if you don't
  divide by lane count) — legacy Vieweb clips to 100.

`PANELS.Conveying_Time_s`, `Buy_Sell_Panel_Time_s`,
`Waiting_Review_Time_s` are declared "NOT AVAILABLE YET" in Vision3D CR4.
Do not report on them unless / until data appears.

## Panel-vs-Board rules

- **Cp / Cpk / FPY** — configurable at entity level via a radio button
  (panel or board).
- **DPMO** — board only.
- **PPM** — board only (opportunity denominator is per placed part).
- **MSA / TestEmptyMaster / Traceability** — operate at the reference
  designator / component level.

Always echo the choice in the report legend so a downstream reader knows
which population produced the number.

## Sanity-check tips before shipping any new report

1. Recompute one row manually against a tiny known dataset (e.g. one
   panel, 10 components, 2 defects) and match Vieweb's number to within
   0.01. Any drift = bug.
2. Weekly totals must equal `sum(daily totals)` after truncating each
   day to the same time zone. If they differ, you re-implemented bug
   **#12421**. Aggregate counts, not ratios.
3. For DPMO/PPM, confirm the divisor is stable across the report window
   (no missing `Nb_Of_Tests_*` on some sub-panels — those are typically
   overflow rows with `Anomaly_* bit 1024`).
4. For MSA, verify the same reference designator appears the expected
   number of times before averaging — a partially-inspected repetition
   biases σ.
