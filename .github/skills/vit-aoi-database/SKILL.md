---
name: vit-aoi-database
description: 'Expert knowledge of the ViTechnology (VIT) Superviseur / K-Series production database used by the Vision3D CR4 and Vision20 CR5 AOI (Automated Optical Inspection) systems. Use when: writing SQL against PANELS, CARDS, TESTED_OBJECT, PIN, PIN_MEASURE, MACHINE, PRODUCT, RECIPE, LIBRARY, OPERATOR, TOLERANCE, PART_NUMBER, JEDEC, FEEDER, OBJECT_TYPE, or the *_HISTO tables; decoding the Anomaly_BR / Anomaly_AR bit-fields on PANELS/CARDS or the Error_Table / Error_Table_AR bit-fields on TESTED_OBJECT/PIN; interpreting Panel_Status / Card_Status / Repair_State_result / Not_Inspected_Cause / Object_Type_Id / Measure_Type constants; converting Panel_Numeric_Date / Repair_Numeric_Date_Hour / File_Date / Library_Date (ANSI time_t) and the Create_Date float format; understanding the panel → sub-panel (card) → tested object → pin → measure hierarchy; safely purging or archiving old data. Includes ALL bit masks and enum values from Vision3D CR4 documentation.'
---

# VIT AOI Superviseur database (Vision3D CR4 / Vision20 CR5)

Source: `Database fields and constants (Vision3D CR4).pdf` (extracted at
`pdf_text/Database fields and constants (Vision3D CR4).txt`), authored by
Vincent SAFFRE, VIT, 10/05/2018.

> **CRITICAL WARNING (verbatim from VIT):** Do NOT run heavy or long-running
> queries against this database. It is written to in real time by the
> Superviseur while an AOI is inspecting. A slow request can prevent the
> Superviseur from persisting inspection results within the cycle-time
> budget and **stop the SMT line**. Nieweb is a **read-only** consumer.
> Use indexes, filter by date, cap row counts, and prefer replicated /
> snapshot copies for heavy analytics.

Supported DBMS (per legacy Vieweb `web.xml` `clientDataBaseType`):
`SQLSERVER`, `ORACLE`, `ACCESS`.

## Big picture

```
MACHINE 1─┐
          ├─< PANELS 1─┐
          │            ├─< CARDS 1─┐
          │            │           ├─< TESTED_OBJECT 1─┐
          │            │           │                   ├─< PIN 1─< PIN_MEASURE
          │            │           │                   ↓
          │            │           │              Foreign material rows also live here
          │            │           │                   (Object_Type_Id = 0x02000000)
PRODUCT ──┘    RECIPE ─┘  LIBRARY, OPERATOR, TOLERANCE, PART_NUMBER, JEDEC, FEEDER
```

Historisation (added in v5.0):
`PANELS_HISTO`, `CARDS_HISTO`, `TESTED_OBJECT_HISTO`, `PIN_HISTO` capture the
previous values every time a row is updated by the review station. Rows are
inserted, never mutated. Use them to reconstruct the pre-review state and to
audit operator changes.

Detailed table-by-table reference lives in
[`./references/schema.md`](./references/schema.md) — load it when you need a
column list. This SKILL.md holds the **constants and rules** you almost
always need at hand.

## Date/time conventions

- `Panel_Numeric_Date`, `Repair_Numeric_Date_Hour`, `File_Date` (RECIPE),
  `Library_Date` (LIBRARY) → **ANSI `time_t`**: seconds since
  1970-01-01 00:00:00 UTC. Convert with `DATEADD(SECOND, col, '19700101')`
  (SQL Server) or `TO_TIMESTAMP('1970-01-01','YYYY-MM-DD') + col * INTERVAL '1 second'`
  (Oracle). Store UTC; render in local TZ only at the UI boundary.
- `Create_Date` on MACHINE, LIBRARY, OPERATOR, PART_NUMBER, JEDEC, FEEDER →
  proprietary **readable float**. Example `2.0050128183733` means
  2005-01-28 18:37:33 (structure: `2.YYYYMMDDHHMMSS` truncated to double
  precision). Treat as informational only.
- `PANELS_HISTO.DateTime`, `CARDS_HISTO.DateTime`, `TESTED_OBJECT_HISTO.DateTime`,
  `PIN_HISTO.DateTime` are already UTC `datetime` values.

## Bit-flag fields — decode exactly

Reproduce these masks verbatim in Nieweb. Legacy bug **#11211** (wrong defect
displayed) came from mis-mapping these bits.

### `PANELS.Anomaly_BR` / `PANELS.Anomaly_AR` (12-bit)

| Bit | Value | Meaning |
|---|---|---|
| 1 | 1 | Fiducial error → panel not inspected |
| 2 | 2 | Panel ID code reading error (barcode / DataMatrix) |
| 3 | 4 | Unexpected panel code root |
| 4 | 8 | Ejected by review operator |
| 5 | 16 | Washed by review operator |
| 6 | 32 | One or more defects on panel (see `TESTED_OBJECT`) |
| 7 | 64 | Not-specified error (often: ejected without inspection via comm module) |
| 8 | 128 | Axis error (results should not be persisted) |
| 9 | 256 | Panel not inspected – all sub-panels skipped |
| 10 | 512 | One or more sub-panels has ID-code error |
| 11 | 1024 | Too many defects – overflow, not saved in `TESTED_OBJECT` |
| 12 | 2048 | Reference: 0 = inspected, 1 = NOT inspected |

`Anomaly_AR` is updated by the review station; `Anomaly_BR` freezes AOI's
original opinion.

### `CARDS.Anomaly_BR` / `CARDS.Anomaly_AR` (12-bit)

Same encoding as panel, with these differences on the higher bits:

| Bit | Value | Meaning |
|---|---|---|
| 9 | 256 | Skipped sub-panel |
| 10 | 512 | Sub-panel invalidated by review operator (does NOT taint parent panel) |
| 11 | 1024 | Too many defects on panel — not saved |
| 12 | 2048 | 0 = sub-panel inspected, 1 = NOT inspected |

If **every** sub-panel is invalidated, the parent panel is considered
ejected (`PANELS.Anomaly_AR bit 4 = 8`).

### `TESTED_OBJECT.Error_Table` / `Error_Table_AR` (25-bit; `Error_Table_AR` is BIGINT — upper 32 bits reserved for classification)

| Bit | Value | Meaning |
|---|---|---|
| 1 | 1 | Object missing |
| 2 | 2 | Polarity error |
| 3 | 4 | Solder joint defect (refer to `PIN`) |
| 4 | 8 | Solder bridge defect (refer to `PIN`) |
| 5 | 16 | OCV error |
| 6 | 32 | Model not found in library **(obsolete → use `Not_Inspected_Cause = 2`)** |
| 7 | 64 | `Delta_X` out of range |
| 8 | 128 | `Delta_Y` out of range |
| 9 | 256 | `Delta_Theta` out of range |
| 10 | 512 | `Delta_Thickness` out of range |
| 11 | 1024 | Paste surface area out of range |
| 12 | 2048 | Element skipped **(obsolete → use `Not_Inspected_Cause = 1`)** |
| 13 | 4096 | Connector: bad pin-column spacing *(obsolete)* |
| 14 | 8192 | Connector: bad pin-row spacing *(obsolete)* |
| 15 | 16384 | Connector: pin missing *(obsolete)* |
| 16 | 32768 | Connector: bad pin alignment *(obsolete)* |
| 17 | 65536 | Volume out of range *(obsolete)* |
| 18 | 131072 | Bad appearance *(obsolete)* |
| 19 | 262144 | Potential defect imported from SPI |
| 20 | 524288 | Tilt error (bad coplanarity) |
| 21 | 1048576 | Side overhang (IPC 610) |
| 22 | 2097152 | Length overhang (IPC 610) |
| 23 | 4194304 | Foreign material detected |
| 24 | 8388608 | Component present (should not be) |
| 25 | 16777216 | Lifted lead (refer to `PIN`) |

Rules:
- `Error_Table_AR = 0` alone is **not** enough to declare a component
  “good”. You must also check `Not_Inspected_Cause = 0`.
- `Error_Table` reflects AOI's original opinion; `Error_Table_AR` reflects
  operator review (classification + sanction). A `DummyFault` sanction
  clears all bits.
- `PIN.Error_Table` and `PIN.Error_Table_AR` use the **same bit
  positions** but only bits 3, 4, 25 (joint, bridge, lifted lead) plus the
  overhang bits are typically populated.

## Enum / status columns

### `PANELS.Panel_Status` and `CARDS.Card_Status` (tinyint)
Like `Anomaly_AR` but ignores the ID-code-reading error and is a real enum
so it can be filtered directly.

| Value | Meaning |
|---|---|
| -2 | Still faulty after review |
| -1 | Faulty after inspection |
| 0 | Not inspected |
| 1 | Good after inspection |
| 2 | Good because all defects were dummy faults |
| 3 | Good after review |

### `TESTED_OBJECT.Not_Inspected_Cause` (tinyint)

| Value | Meaning |
|---|---|
| 0 | Inspected |
| 1 | Manually skipped |
| 2 | Model not found |
| 3 | Bad programming |

### `TESTED_OBJECT.Repair_State_result` (small int)
Negative = AOI-side, positive = review-side.

| Value | Meaning |
|---|---|
| -2 | Not inspected (`ManuallySkipped` or `BadProgramming`) |
| -1 | Not detected as faulty by AOI |
| 0 | AOI-detected faulty, not yet reviewed |
| 1 | Repaired |
| 2 | Good (acceptable or dummy fault) |
| 3 | Confirmed faulty |

### `PIN.Review_Sanction`
Same meanings as `Repair_State_result` `-1..3`.

### `PIN.Component_Side` (0=N, 1=E, 2=S, 3=W)
Sides are numbered clockwise starting at the top of the library-oriented
component (0° reference). `Pin_Index_On_Side` is 0-based within the side,
also clockwise. `IPC_Pin_Nb` = global IPC-style pin index (0 = undefined).

### `PANELS.Has_Been_Reviewed`
- 0 = not yet on a review station
- 1 = passed on a review station (even if operator pressed Escape)
- 255 = transitory state; results not fully saved; **exclude from queries**
  (used by offline FIFO Review to gate reads).

### `PANELS.Is_Last_Inspection`
0 = FALSE, 1 = TRUE. When a panel side is re-inspected on the same machine,
`Crossing` increments and the previous row now has `Is_Last_Inspection = 0`.
For most analyses filter `WHERE Is_Last_Inspection = 1`.

### `PANELS.Prod_Step`
`0` undefined, `1` pre-reflow, `2` post-reflow.

### `PANELS.IPC610_Inspection_Class`
`1..3` per IPC-610; `0` when no IPC-610 test.

### `OBJECT_TYPE.Object_Type_Id` (bit-code, use bitmasks for filtering)

| Hex | Dec | Meaning |
|---|---|---|
| `0x00000001` | 1 | Component |
| `0x00000008` | 8 | Text alone (not attached to a component) |
| `0x00000010` | 16 | Paste pad |
| `0x00001000` | 4096 | Macro |
| `0x00010000` | 65536 | Connector *(obsolete)* |
| `0x00020000` | 131072 | Group of connectors *(obsolete)* |
| `0x02000000` | 33554432 | Foreign material |

### `PIN_MEASURE.Measure_Type` (integer)

| Code | Measure | Unit |
|---|---|---|
| 1 | SideOverhang | % |
| 2 | LengthOverhang | µm |
| 3 | Joint3D_Height | µm |
| 4 | Joint3D_Width | µm |
| 5 | Joint3D_Length | µm |
| 6 | Joint3D_Convexity | ratio (-1..1) |
| 7 | Joint3D_TailVolume | mm³ |
| 8 | Joint3D_Volume | mm³ |
| 9 | Joint3D_Uniformity | % |
| 10 | Joint2D_Average | grey level (0..255) |
| 11 | Joint2D_Area | % |
| 12 | LeadHeight | µm |
| 13 | Joint3D_TerminationHeight | µm |
| 14 | Joint3D_WettingAngle | ° |
| 15 | Joint3D_Height (%) | % |
| 16 | Joint3D_Width (%) | % |
| 17 | Joint3D_TailVolume (%) | % |

`Degraded_Mode = 1` means the measurement was taken on the 2D image
because the 3D image was unusable at that spot.

### `MACHINE.Machine_Type` (tinyint)
1 = AOI, 2 = Review station. `Machine_Type_Name` is the human-readable form.

### `RECIPE.Inspected_Side_Nb`
-1 = both sides (symmetrical boards); 0/1 = a particular side (site
convention).

### `PART_NUMBER` / `JEDEC` also carry macro types
Both tables can store macro descriptors (used when
`OBJECT_TYPE.Object_Type_Id = 4096`). Supported macro names:

`DISTANCE_X`, `DISTANCE_Y`, `DISTANCE`, `ANGLE_2_TOPO`,
`ANGLE_STRAIGHTLINE_TOPO`,
`DISTANCE_POS+ANGLE_STRAIGHTLINE_2_TOPO`, `ANGLE_3_TOPO`,
`DISTANCE_2_TOPO_STRAIGHTLINE_TOPO`,
`DISTANCE_TOPO1_TO_PROJECTED_TOPO3_ON_STRAIGHTLINE_TOPO1_TOPO2`.

For a macro, `TESTED_OBJECT.Delta_X` / `Delta_Y` carry the computed
distance and `Delta_Theta` the computed angle. `Expected_PosX_um`,
`Expected_PosY_um`, `Expected_Angle_dg` are populated only for macros.

For a Foreign Material row (`Object_Type_Id = 0x02000000`):
`Delta_X` / `Delta_Y` = XY position of the bounding-box center;
`Delta_Thickness` = height; `Expected_Thickness` = height of the
encompassing area; `Expected_Volume` = width of the encompassing area.

## Unique constraints (guard writes / de-duplication)

- `PANELS`: no unique constraint. A panel side can be inspected many times
  → use `(Panel_Bar_Code, Face_Number, Machine_Id, Crossing)` as a logical
  key.
- `CARDS`: `(Panel_Id, Card_Bar_Code)`.
- `TESTED_OBJECT`: `(Card_Id, Topology)` — `Topology` = reference
  designator.
- `PRODUCT`: `(Product_Name, Revision)`.
- `RECIPE`: `(File_Name, File_Date)`.
- `LIBRARY`: `(Library_Name, Library_Date)`.
- `TOLERANCE`: combination of **all** tolerance values.
- `FEEDER`: combination of all columns except `Feeder_Id`.

## “Default” records (do not delete)

Rows with `Id = 1` in `LIBRARY`, `OPERATOR`, `TOLERANCE`, `PART_NUMBER`,
`JEDEC`, `FEEDER` exist as fallbacks for orphan `TESTED_OBJECT` rows and
must never be deleted.

## Purge / archive rules (from §7 of the spec)

Two supported strategies:

**Purge in place** – delete oldest results to shrink the DB. Order
matters (older SQL Server DBs are not cascading):

1. `PIN_MEASURE`
2. `PIN`
3. `TESTED_OBJECT`
4. `CARDS`
5. `PANELS`

Common WHERE clauses:
- `Panel_Numeric_Date < :cutoff_time_t`
- `Panel_Status >= 0` → keep only faulty panels
- `Has_Been_Reviewed = 1` → keep only unreviewed

**Copy to new DB** – for each retained panel you must insert dependencies
first (`MACHINE`, `PRODUCT`, `RECIPE`, `LIBRARY`, `OPERATOR`,
`PIXEL_SIZE`), then `PANELS`, then per-card `OPERATOR`, then `CARDS`, then
per-object `JEDEC`, `PART_NUMBER`, `TOLERANCE`, `OPERATOR`, `FEEDER`, then
`TESTED_OBJECT`, then `PIN`, then `PIN_MEASURE`. Never re-use IDs from the
source DB.

## Obsolete tables (skip in Nieweb)

- `PIXEL_SIZE` – only referenced by old 3D machines.
- `SPC`, `SPC_Object` – embedded SPC warnings, not used.
- `LOG_PROD` – reserved for future machine-transition logging.
- `JOINT_BRIDGE` – **removed in DB v5.0**; replaced by `PIN` + `PIN_MEASURE`.
  Do NOT reintroduce it.

## Safe-query checklist for Nieweb

1. `WHERE Panel_Numeric_Date BETWEEN :from AND :to` (indexed) — always
   bound the time window.
2. Filter `WHERE Has_Been_Reviewed <> 255` to avoid transitory rows.
3. Use `WHERE Is_Last_Inspection = 1` unless the report explicitly wants
   re-inspections.
4. Join up (`PIN_MEASURE → PIN → TESTED_OBJECT → CARDS → PANELS →
   MACHINE`) — never scan `TESTED_OBJECT` without a `Card_Id` or
   `Panel_Numeric_Date` predicate.
5. For panel/board **status** filtering prefer `Panel_Status` /
   `Card_Status` over decoding `Anomaly_AR` (the enum ignores irrelevant
   code-reading bits).
6. Always parameterize; never concatenate user input into SQL — the
   Superviseur DB is often reachable from the factory network.
7. Set explicit `SET LOCK_TIMEOUT` / read-committed-snapshot or
   `WITH (NOLOCK)` on SQL Server for reporting queries, so the AOI writer
   is not blocked. Document the isolation level per query.

## Related resources

- Full column reference: [`./references/schema.md`](./references/schema.md)
- Formulas that consume these fields: **skill `aoi-quality-metrics`**
- Legacy consumer: **skill `vieweb-legacy`**
