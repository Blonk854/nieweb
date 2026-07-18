# Vision3D CR4 / CR5 schema — column reference

Column-level reference derived from `Database fields and constants (Vision3D
CR4).pdf` (Vincent SAFFRE, VIT, 10/05/2018) and **verified against the
archived HLYAOI database on 2026-07-17** via `tools/db/probe-schema.ps1` +
`probe-schema-extra.ps1`.

- Schema version on `HLYAOI`: `VERSION.Numero = '5.0'` (DATABASEID 1762100668).
- All tables live in the `dbo` schema.
- Where a column name in this reference differs from the on-disk name the
  physical spelling is called out (mostly `UPPER_SNAKE_CASE` columns added
  in CR4/CR5 vs. the earlier `Mixed_Snake_Case`).
- Numeric ID columns are a mix of `int` and `bigint`; do NOT assume BIGINT
  everywhere. `bigint` is used on the row-heaviest tables (`PIN`,
  `TESTED_OBJECT`, `CARDS`, and their `*_HISTO` counterparts). `Panel_Id`,
  `Machine_Id`, `Product_Id`, `Recipe_Id`, `Library_Id`, `Operator_Id`,
  `Tolerance_Id`, `Feeder_Id`, `Pixel_Size_Id`, `Object_Type_Id`,
  `Part_Number_Id`, `Jedec_Id` are all `int`.

Legend: **PK** = primary key, **FK →** foreign key.

---

## PANELS — one row per side-inspection of a panel

39 columns, on-disk names verified. 10.87 M rows on HLYAOI.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `Panel_Id` | int | NO | **PK**, auto-incr |
| `Machine_Id` | int | NO | **FK →** MACHINE |
| `Lane_Number` | int | NO | Dual-lane AOI lane (1 or 2); 0 for single lane |
| `Panel_Bar_Code` | varchar(64) | NO | Panel barcode / DataMatrix; AOI-generated if no reader |
| `Face_Number` | int | NO | Side inspected: 0, 1, or -1 (undefined) |
| `Face` | nvarchar(16) | NO | Human name ("Top", "Bottom", …) |
| `Prod_Info` | nvarchar(50) | YES | Lot / batch number (customer-defined) |
| `Panel_Info` | nvarchar(64) | YES | Operator comment on global sanction |
| `Panel_Numeric_Date` | int | NO | ANSI `time_t` UTC inspection |
| `Nb_Of_Valid_Cards` | int | NO | Good sub-panel count |
| `Test_Time` | float | NO | Inspection duration in s (excludes conveying) |
| `Panel_Status` | int | NO | -2..3 enum (observed on archive: {-2,-1,0,1,2}) |
| `Anomaly_BR` | int | NO | 12-bit AOI-state bitfield |
| `Anomaly_AR` | int | NO | 12-bit after-review bitfield |
| `Has_Been_Reviewed` | tinyint | NO | 0 / 1 / 255 |
| `Nb_Of_Tested_Object` | int | NO | Components + paste pads inspected |
| `Nb_Of_Error_Object` | int | NO | Faulty objects (0 if washed) |
| `Components_AvgDev_X` / `_Y` / `_Theta` | float | YES | For Cp |
| `Components_StdDev_X` / `_Y` / `_Theta` | float | YES | For Cp |
| `Crossing` | int | YES | Re-inspection count |
| `Operator_Id` | int | YES | **FK →** OPERATOR (review operator) |
| `Product_Id` | int | NO | **FK →** PRODUCT |
| `Recipe_Id` | int | NO | **FK →** RECIPE |
| `Library_Id` | int | NO | **FK →** LIBRARY |
| `Pixel_Size_Id` | int | NO | **FK →** PIXEL_SIZE |
| `Msg_For_Repair_Operator` | nvarchar(255) | YES | Shown on review station |
| `FreeField1..4` | nvarchar(64) | YES | Customer-defined |
| `IPC610_INSPECTION_CLASS` | tinyint | NO | 1..3, or 0 if none |
| `CONVEYING_TIME_S` | float | YES | Now populated — was "NA" in the CR4 spec |
| `BUY_SELL_PANEL_TIME_S` | float | YES | Buy/sell handshake time (s) |
| `WAITING_REVIEW_TIME_S` | float | YES | Embedded review wait (s) |
| `IS_LAST_INSPECTION` | tinyint | NO | 0 / 1 — filter on `= 1` for most reports |

Columns from the CR4 spec that are **NOT** present in HLYAOI: `Prod_Step`,
`PastePads_AvgDev_*`, `PastePads_StdDev_*`, `Stencil_DX/_DY/_DTheta`. Those
paste/stencil deltas live on `CARDS` on this archive.

## CARDS — sub-panels

107 M rows. Unique: `(Panel_Id, Card_Bar_Code)`.

| Column | Type | Notes |
|---|---|---|
| `Card_Id` | bigint | **PK** |
| `Panel_Id` | int | **FK →** PANELS |
| `Card_Bar_Code` | varchar / nvarchar | Sub-panel code |
| `Card_Number` | int | 1..n from TST |
| `Card_Info` | nvarchar | Operator comment |
| `Anomaly_BR` / `Anomaly_AR` | int | 12-bit bitfield |
| `Card_Status` | int | -2..3 enum |
| `Operator_Id` | int | **FK →** OPERATOR |
| `Number_Of_Component` / `Number_Of_Pads` | int | Inspected count |
| `Number_Of_Anomaly` | int | Faulty count |
| `Nb_Of_Tests_On_Comp` / `Nb_Of_Tests_On_Pads` | bigint | Denominator for DPMO |
| `Components_AvgDev_*`, `Components_StdDev_*` | float | For Cp |
| `PastePads_AvgDev_*`, `PastePads_StdDev_*` | float | For Cp |
| `FreeField1..4` | nvarchar | Customer-defined |

## TESTED_OBJECT — inspected components, paste pads, macros, foreign materials

14.7 M rows. Unique: `(Card_Id, Topology)`.

| Column | Type | Notes |
|---|---|---|
| `Tested_Object_Id` | bigint | **PK** |
| `Card_Id` | bigint | **FK →** CARDS |
| `Topology` | nvarchar | Reference designator (unique per sub-panel) |
| `Object_Type_Id` | int | **FK →** OBJECT_TYPE |
| `Part_Number_Id` | int | **FK →** PART_NUMBER |
| `Error_Table` | int | Bitfield of AOI defects |
| `Error_Table_AR` | bigint | After-review bitfield; upper 32 bits = classification |
| `Not_Inspected_Cause` | tinyint | 0..3 enum |
| `Score` | float | Winner-model score |
| `Delta_X` / `_Y` / `_Theta` / `_Thickness` / `_Surface` | float | Deviations |
| `Mes_Tilt_um` | float | Height range across treatment area |
| `Expected_PosX_um` / `_PosY_um` / `_Angle_dg` | float | Non-zero only for macros |
| `Expected_Thickness` / `_Surface` / `_Volume` | float | Expected values |
| `Read_Text` | nvarchar | OCR / OCV / ID-code text |
| `Measures` | nvarchar | Internal treatment variables |
| `Model` | nvarchar | Library model name |
| `Tolerance_Id` | int | **FK →** TOLERANCE |
| `Is_3D_Test` | tinyint | 0 / 1 |
| `Repair_State_result` | smallint | -2..3 enum |
| `Repair_Button_Comment` | nvarchar(40) | Button chosen by operator |
| `Repair_Error_Comment` | nvarchar(40) | Text associated to button |
| `Repair_Operator_Comment` | nvarchar(255) | Operator free comment |
| `Repair_Numeric_Date_Hour` | int | ANSI `time_t` UTC |
| `Operator_Id` | int | **FK →** OPERATOR |
| `Feeder_Id` | int | **FK →** FEEDER |
| `Traceability` | tinyint | 1 = keep even if good |
| `FreeField1..2` | nvarchar | Customer-defined |

## PIN — per-pin defect status

22 M rows. **10 columns**, all verified.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `Pin_Id` | bigint | NO | **PK** |
| `Tested_Object_Id` | bigint | NO | **FK →** TESTED_OBJECT |
| `Component_Side` | tinyint | NO | 0 N / 1 E / 2 S / 3 W (library-oriented) |
| `Pin_Index_On_Side` | smallint | NO | 0-based, clockwise |
| `IPC_Pin_Nb` | smallint | YES | IPC global pin index; 0 = undefined |
| `Error_Table` | int | NO | Bitfield (same bit meanings as TESTED_OBJECT) |
| `Error_Table_AR` | bigint | NO | After-review |
| `Bridge_ID` | int | NO | Groups pins in the same solder bridge; 0 = not in bridge (on-disk name is `Bridge_ID`, uppercase) |
| `Review_Sanction` | tinyint | NO | -1..3 enum |
| `Review_Comment` | nvarchar(255) | YES | Operator comment |

## PIN_MEASURE — measurements per pin

104 M rows. **7 columns**.

| Column | Type | Notes |
|---|---|---|
| `Pin_Measure_Id` | bigint | **PK** |
| `Pin_Id` | bigint | **FK →** PIN |
| `Measure_Type` | int | See table in main SKILL |
| `Value` | float | Unit depends on `Measure_Type` |
| `Tolerance_Min` | float | Lower bound |
| `Tolerance_Max` | float | Upper bound |
| `Degraded_Mode` | tinyint | 0 / 1 (2D fallback) |

## *_HISTO tables — audit trail of review updates

All four have a `DateTime` (`datetime`) column recording when the change
was applied.

### PANELS_HISTO (9 columns)
`Panel_Id` (int), `old_Panel_Bar_Code` (varchar 64), `old_Anomaly_AR` (int),
`old_Panel_Status` (int), `old_Nb_Of_Valid_Cards` (int),
`old_Nb_Of_Error_Object` (int), `old_Operator_Id` (int),
`old_Panel_Info` (nvarchar 64), `DateTime` (datetime).

### CARDS_HISTO (8 columns)
`Card_Id` (bigint), `old_Card_Bar_Code` (varchar 64), `old_Anomaly_AR` (int),
`old_Card_Status` (int), `old_Number_Of_Anomaly` (int),
`old_Operator_Id` (int), `old_Card_Info` (nvarchar 64), `DateTime` (datetime).

### TESTED_OBJECT_HISTO (9 columns)
`Tested_Object_Id` (bigint), `old_Error_Table_AR` (bigint),
`old_Repair_State_Result` (int), `old_Repair_Button_Comment` (nvarchar 40),
`old_Repair_Error_Comment` (nvarchar 40),
`old_Repair_Operator_Comments` (nvarchar 255),
`old_Repair_Numeric_Date_Hour` (int), `old_Operator_Id` (int),
`DateTime` (datetime).

### PIN_HISTO (5 columns)
`Pin_Id` (bigint), `old_Error_Table_AR` (bigint),
`old_Review_Sanction` (tinyint), `old_Review_Comment` (nvarchar 255),
`DateTime` (datetime).

Row counts on HLYAOI: TESTED_OBJECT_HISTO 8.1 M, CARDS_HISTO 3.0 M,
PANELS_HISTO 1.9 M, PIN_HISTO 1.8 M.

## MACHINE (5 columns)

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `Machine_Id` | int | NO | **PK** |
| `Machine_Type` | int | NO | 1 = Vision (AOI), 2 = Repair (verified on HLYAOI) |
| `Machine_Name` | varchar(32) | NO | Computer name (e.g. `L3PSTAOI`, `L7PSTREP`) |
| `Machine_Type_Name` | varchar(32) | YES | Readable form ("Vision", "Repair") |
| `Create_Date` | float | YES | Custom `2.YYYYMMDDHHMMSS` |

HLYAOI has 23 machines total covering lines L1..L8 with both AOI and Repair
seats plus `_POST_REFLOW` machines.

## PRODUCT (5 columns)

Unique: `(Product_Name, Revision)`.

| Column | Type | Nullable |
|---|---|---|
| `Product_Id` | int | NO **PK** |
| `Product_Name` | varchar(64) | YES |
| `Revision` | nvarchar(16) | YES |
| `Description` | nvarchar(64) | YES |
| `Product_Date` | int | YES (`time_t`) |

## RECIPE (14 columns)

Unique: `(File_Name, File_Date)`.

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `Recipe_Id` | int | NO | **PK** |
| `File_Name` | varchar(64) | YES | Inspection program filename (no path / ext) |
| `File_Date` | int | YES | ANSI `time_t` UTC |
| `Revision` | nvarchar(16) | YES | |
| `Author` | nvarchar(32) | YES | |
| `Product_Id` | int | NO | **FK →** PRODUCT |
| `Inspected_Side_Nb` | int | NO | -1 both, 0/1 side |
| `Inspected_Side_Name` | nvarchar(16) | YES | |
| `Customer` | nvarchar(64) | YES | |
| `Production_Step` | nvarchar(32) | YES | |
| `Warning` | nvarchar(128) | YES | Program-level warning message |
| `FreeField1` / `FreeField2` | nvarchar(64) | YES | |
| `VARIANT_NAME` | nvarchar(50) | NO | Sigmalink variant name (empty string when no variant) |

## LIBRARY

Unique: `(Library_Name, Library_Date)`. 122 K rows on HLYAOI.

| Column |
|---|
| `Library_Id` (int) **PK**, `Library_Name`, `Library_Date` (`time_t`), `Comments`, `Create_Date` |

## OPERATOR

Review operator names. `Operator_Id` (int) **PK**, `Operator_Name`,
`Create_Date`.

## TOLERANCE (all-column unique key)

| Column | Notes |
|---|---|
| `Tolerance_Id` (int) | **PK** |
| `Delta_Position_X_Min_um` / `Delta_Position_X` | X-axis min/max deviation (µm) |
| `Delta_Position_Y_Min_um` / `Delta_Position_Y` | Y-axis min/max deviation (µm) |
| `Delta_Angle_Min_dg` / `Delta_Angle` | Rotation min/max (°) |
| `Delta_Thickness_Min` / `_Max` | Thickness limits |
| `Delta_Surface_Min` / `_Max` | Paste surface limits (%) |
| `Delta_Volume_Min` / `_Max` *(obsolete)* | Paste volume limits (%) |
| `Max_Tilt_um` | Max tilt |

## PART_NUMBER

`Part_Number_Id` (int) **PK**, `Part_Number`, `Jedec_Id` (int) **FK →** JEDEC, `Create_Date`.

## JEDEC

`Jedec_Id` (int) **PK**, `Jedec_Name`, `Face_N` / `Face_S` / `Face_E` /
`Face_W`, `Create_Date`.

## FEEDER (7 columns)

3 rows on HLYAOI: `""` (default), `DNP`, `NOGO`.

`Feeder_Id` (int) **PK**, `Feeder_Machine`, `Feeder_Level1..4`,
`Create_Date`.

## OBJECT_TYPE (4 columns)

**Verified enum values** on HLYAOI:

| `Object_Type_Id` | `Object_Type_Name` |
|---|---|
| 1 | COMPONENT |
| 8 | TEXT |
| 16 | PASTE |
| 4096 | MACRO |
| 8192 | REF 3D |
| 32768 | FEEDER |
| 65536 | CONNECTOR |
| 131072 | CONNECTOR_GRP |
| 33554432 | FOREIGN_MATERIAL |

Column layout: `Object_Type_Id` (int) **PK**, `Object_Type_Name` (varchar 32),
`Comments` (varchar 64), `Create_Date` (float).

## PIXEL_SIZE (4 columns)

Still present in CR4/CR5 schema. On HLYAOI there is exactly one row with
`Size_X = Size_Y = Size_Z = 0` — the field is not populated by current
inspection code even though `PANELS.Pixel_Size_Id` is NOT NULL and
references it.

`Pixel_Size_Id` (int) **PK**, `Size_X` / `Size_Y` / `Size_Z` (float).

## Barcode_Product (2 columns) — barcode → product lookup

3.4 M rows on HLYAOI. Not in the original CR4 spec; added by Vieweb / OIS
consumers to speed panel-to-product resolution.

| Column | Type |
|---|---|
| `Panel_bar_code` | varchar(64) NOT NULL |
| `Product_Name` | varchar(64) |

Two companion **views**:

- `BarcodeProduct` — deduplicated / normalized wrapper.
- `BarcodeProductDate` — same with a date-of-first-seen column.

## LOG_PROD (6 columns)

Production-log table. **Empty (0 rows) on HLYAOI** — reserved but not
populated by the current toolchain.

| Column | Type | Nullable |
|---|---|---|
| `Log_Prod_Id` | int | NO **PK** |
| `Log_Type` | int | YES |
| `Log_Number` | varchar(50) | YES |
| `Machine_Id` | int | YES **FK →** MACHINE |
| `Comments` | varchar(128) | YES |
| `Create_Date` | float | YES |

## SPC / SPC_OBJECT — reserved

Both tables are present but **empty (0 rows) on HLYAOI**. Not consumed by
Vieweb/Sigmalink queries in production. Retain the definition but treat
as unused when planning Nieweb.

- `SPC` (6): `SPC_Id`, `SPC_Anomaly`, `Panel_Id` (**FK →** PANELS),
  `SPC_Object_Id`, `Value` (float), `Create_Date` (float).
- `SPC_OBJECT` (5): `SPC_Object_Id`, `Object` (varchar 64),
  `Card_Number` (int), `Object_Type_Id` (int **FK →** OBJECT_TYPE),
  `Variable` (int).

## VERSION (2 columns)

DB schema version marker. Exactly one row.

| Column | Type | Live value |
|---|---|---|
| `Numero` | varchar(10) | `5.0` |
| `DATABASEID` | int | `1762100668` |

Query it before opening any migration script to confirm you are still on
the expected schema:

```sql
SELECT Numero, DATABASEID FROM VERSION WITH (NOLOCK);
```
