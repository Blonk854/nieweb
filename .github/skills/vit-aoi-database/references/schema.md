# Vision3D CR4 / CR5 schema — column reference

Complete list of columns per table, derived from
`Database fields and constants (Vision3D CR4).pdf` (Vincent SAFFRE, VIT,
10/05/2018). Types are as stated in the spec; check the physical DB before
generating DDL.

Legend: **PK** = primary key, **FK →** foreign key, *(v5)* introduced in
v5.0, *(obsolete)* kept for legacy compatibility only.

---

## PANELS — one row per side-inspection of a panel

| Column | Type | Notes |
|---|---|---|
| `Panel_Id` | BIGINT | **PK**, auto-incr |
| `Panel_Bar_Code` | string | Panel barcode / DataMatrix; AOI-generated if no reader |
| `Face_Number` | int | Side inspected: 0, 1, or -1 (undefined) |
| `Face` | string | Human name ("Top", "Bottom", …) |
| `Machine_Id` | BIGINT | **FK →** MACHINE |
| `Lane_Number` | int | Dual-lane AOI lane (1 or 2); 0 for single lane |
| `Prod_Step` | tinyint | 0 undefined / 1 pre-reflow / 2 post-reflow |
| `Prod_Info` | string | Lot / batch number (customer-defined) |
| `Panel_Info` | string | Operator comment on global sanction |
| `Panel_Numeric_Date` | int | ANSI `time_t` inspection UTC |
| `Conveying_Time_s` | int | Not available yet |
| `Test_Time` | int | Inspection duration in s (excl. conveying) |
| `Buy_Sell_Panel_Time_s` | int | Not available yet |
| `Waiting_Review_Time_s` | int | Embedded review wait (NA) |
| `Product_Id` | BIGINT | **FK →** PRODUCT |
| `Library_Id` | BIGINT | **FK →** LIBRARY |
| `IPC610_Inspection_Class` | tinyint | 1..3, or 0 if none |
| `Nb_Of_Valid_Cards` | int | Good sub-panel count |
| `Nb_Of_Tested_Object` | int | Components + paste pads inspected |
| `Nb_Of_Error_Object` | int | Faulty objects (0 if washed) |
| `Has_Been_Reviewed` | tinyint | 0 / 1 / 255 |
| `Anomaly_BR` | int(12) | Bitfield, AOI state |
| `Anomaly_AR` | int(12) | Bitfield, after-review state |
| `Panel_Status` | tinyint | -2..3 enum |
| `Msg_For_Repair_Operator` | string | Shown on review station |
| `Crossing` | int | Re-inspection count |
| `Is_Last_Inspection` | tinyint | 0 / 1 |
| `Operator_Id` | BIGINT | **FK →** OPERATOR (review operator) |
| `Pixel_Size_Id` | BIGINT | **FK →** PIXEL_SIZE *(obsolete)* |
| `Components_AvgDev_X` / `_Y` / `_Theta` | double | For Cp |
| `Components_StdDev_X` / `_Y` / `_Theta` | double | For Cp |
| `PastePads_AvgDev_X` / `_Y` / `_Surf` | double | For Cp |
| `PastePads_StdDev_X` / `_Y` / `_Surf` | double | For Cp |
| `Stencil_DX` / `_DY` / `_DTheta` | double | Stencil offset (µm, degree) |
| `FreeField1..4` | string | Customer-defined |

## CARDS — sub-panels

Unique: `(Panel_Id, Card_Bar_Code)`.

| Column | Type | Notes |
|---|---|---|
| `Card_Id` | BIGINT *(v5)* | **PK** |
| `Panel_Id` | BIGINT | **FK →** PANELS |
| `Card_Bar_Code` | string | Sub-panel code (AOI can synthesize from panel) |
| `Card_Number` | int | 1..n from TST |
| `Card_Info` | string | Operator comment |
| `Anomaly_BR` / `Anomaly_AR` | int(12) | Bitfield, same layout as PANELS with card-specific bits |
| `Card_Status` | tinyint | -2..3 enum |
| `Operator_Id` | BIGINT | **FK →** OPERATOR |
| `Number_Of_Component` | int | Components inspected |
| `Number_Of_Pads` | int | Paste pads inspected |
| `Number_Of_Anomaly` | int | Faulty count (see notes on invalidated cards) |
| `Nb_Of_Tests_On_Comp` | long | Denominator for component DPMO |
| `Nb_Of_Tests_On_Pads` | long | Denominator for paste DPMO |
| `Components_AvgDev_*`, `Components_StdDev_*`, `PastePads_AvgDev_*`, `PastePads_StdDev_*` | double | For Cp |
| `FreeField1..4` | string | Customer-defined |

## TESTED_OBJECT — inspected components, paste pads, macros, foreign materials

Unique: `(Card_Id, Topology)`.

| Column | Type | Notes |
|---|---|---|
| `Tested_Object_Id` | BIGINT *(v5)* | **PK** |
| `Card_Id` | BIGINT *(v5)* | **FK →** CARDS |
| `Topology` | string | Reference designator (unique per sub-panel) |
| `Object_Type_Id` | int | **FK →** OBJECT_TYPE |
| `Part_Number_Id` | BIGINT | **FK →** PART_NUMBER (or macro type) |
| `Belong_To` | string *(obsolete)* | Parent connector topology |
| `Error_Table` | int | Bitfield of AOI defects |
| `Error_Table_AR` | BIGINT | After-review bitfield; upper 32 bits = classification |
| `Not_Inspected_Cause` | tinyint | 0..3 enum |
| `Score` | float | Winner-model score |
| `Delta_X` / `_Y` / `_Theta` / `_Thickness` / `_Surface` | double | Deviations; for macros / foreign materials, see notes in main SKILL |
| `Delta_Volume` | double *(obsolete)* | % |
| `Mes_Tilt_um` | double | Height range across treatment area |
| `Expected_PosX_um` / `_PosY_um` / `_Angle_dg` | double | Non-zero only for macros |
| `Expected_Thickness` / `_Surface` / `_Volume` | double | Expected values |
| `Read_Text` | string | OCR / OCV / ID-code text |
| `Measures` | string | Internal treatment variables from winner model |
| `Model` | string | Library model name |
| `Tolerance_Id` | BIGINT | **FK →** TOLERANCE |
| `Is_3D_Test` | tinyint | 0 / 1 |
| `Repair_State_result` | small int | -2..3 enum |
| `Repair_Button_Comment` | string | Button chosen by operator |
| `Repair_Error_Comment` | string | Text associated to button |
| `Repair_Operator_Comment` | string | Operator free comment |
| `Repair_Numeric_Date_Hour` | int | ANSI `time_t` UTC |
| `Operator_Id` | BIGINT | **FK →** OPERATOR |
| `Feeder_Id` | BIGINT | **FK →** FEEDER |
| `Traceability` | tinyint | 1 = keep even if good |
| `FreeField1..2` | string | Customer-defined |

## PIN — per-pin defect status

| Column | Type | Notes |
|---|---|---|
| `Pin_Id` | BIGINT | **PK** |
| `Tested_Object_Id` | BIGINT | **FK →** TESTED_OBJECT |
| `Component_Side` | tinyint | 0 N / 1 E / 2 S / 3 W (library-oriented) |
| `Pin_Index_On_Side` | int | 0-based, clockwise |
| `IPC_Pin_Nb` | int | IPC global pin index; 0 = undefined |
| `Error_Table` | int | Bitfield (same bit meanings as TESTED_OBJECT) |
| `Error_Table_AR` | BIGINT | After-review |
| `Bridge_Id` | int | Groups pins in the same solder bridge (unique per panel); 0 = not in bridge |
| `Review_Sanction` | tinyint | -1..3 enum |
| `Review_Comment` | string | Operator comment |

## PIN_MEASURE — measurements per pin

| Column | Type | Notes |
|---|---|---|
| `Pin_Measure_Id` | BIGINT | **PK** |
| `Pin_Id` | BIGINT | **FK →** PIN |
| `Measure_Type` | int | See table in main SKILL |
| `Value` | double | Unit depends on `Measure_Type` |
| `Tolerance_Min` | double | Lower bound |
| `Tolerance_Max` | double | Upper bound |
| `Degraded_Mode` | tinyint | 0 / 1 (2D fallback) |

## *_HISTO tables (v5) — audit trail of review updates

### PANELS_HISTO
`Panel_Id`, `old_Panel_Bar_Code`, `old_Anomaly_AR`, `old_Panel_Status`,
`old_Nb_Of_Valid_Cards`, `old_Nb_Of_Error_Object`, `old_Operator_Id`,
`old_Panel_Info`, `DateTime` (UTC).

### CARDS_HISTO
`Card_Id`, `old_Card_Bar_Code`, `old_Anomaly_AR`, `old_Card_Status`,
`old_Number_Of_Anomaly`, `old_Operator_Id`, `old_Card_Info`, `DateTime` (UTC).

### TESTED_OBJECT_HISTO
`Tested_Object_Id`, `old_Error_Table_AR`, `old_Repair_State_Result`,
`old_Repair_Button_Comment`, `old_Repair_Error_Comment`,
`old_Repair_Operator_Comments`, `old_Repair_Numeric_Date_Hour`,
`old_Operator_Id`, `DateTime` (UTC).

### PIN_HISTO
`Pin_Id`, `old_Error_Table_AR`, `old_Review_Sanction`,
`old_Review_Comment`, `DateTime` (UTC).

## MACHINE

| Column | Type | Notes |
|---|---|---|
| `Machine_Id` | BIGINT | **PK** |
| `Machine_Name` | string | Computer name |
| `Machine_Type` | tinyint | 1 AOI / 2 Review |
| `Machine_Type_Name` | string | Readable form |
| `Create_Date` | float | Custom `2.YYYYMMDDHHMMSS` |

## PRODUCT

Unique: `(Product_Name, Revision)`.

| `Product_Id` | **PK** |
| `Product_Name`, `Revision`, `Description` |
| `Product_Date` | Record creation |

## RECIPE

Unique: `(File_Name, File_Date)`.

| Column | Notes |
|---|---|
| `Recipe_Id` | **PK** |
| `File_Name` | Inspection program filename (no path / ext) |
| `File_Date` | ANSI `time_t` |
| `Product_Id` | **FK →** PRODUCT |
| `Inspected_Side_Nb` | -1 both, 0/1 side |
| `Inspected_Side_Name` | text |
| `Author`, `Revision`, `Production_Step`, `Customer` |
| `FreeField1..2` |

## LIBRARY

Unique: `(Library_Name, Library_Date)`.

| `Library_Id` | **PK** |
| `Library_Name`, `Library_Date` (`time_t`), `Comments`, `Create_Date` |

## OPERATOR

Review operator names. `Operator_Id` **PK**, `Operator_Name`, `Create_Date`.

## PIXEL_SIZE *(obsolete)*

`Pixel_Size_Id` **PK**, `Size_X`, `Size_Y`, `Size_Z` (µm).

## TOLERANCE

Unique: all tolerance columns.

| Column | Notes |
|---|---|
| `Tolerance_Id` | **PK** |
| `Delta_Position_X_Min_um` / `Delta_Position_X` | X-axis min/max deviation (µm) |
| `Delta_Position_Y_Min_um` / `Delta_Position_Y` | Y-axis min/max deviation (µm) |
| `Delta_Angle_Min_dg` / `Delta_Angle` | Rotation min/max (°) |
| `Delta_Thickness_Min` / `_Max` | Thickness limits |
| `Delta_Surface_Min` / `_Max` | Paste surface limits (%) |
| `Delta_Volume_Min` / `_Max` *(obsolete)* | Paste volume limits (%) |
| `Max_Tilt_um` | Max tilt |

## PART_NUMBER

| `Part_Number_Id` | **PK** |
| `Part_Number` | Manufacturer ref (or macro name) |
| `Jedec_Id` | **FK →** JEDEC |
| `Create_Date` |

## JEDEC

| `Jedec_Id` | **PK** |
| `Jedec_Name` | Component shape (or macro name) |
| `Face_N` / `Face_S` / `Face_E` / `Face_W` | Pin count per side |
| `Create_Date` |

## FEEDER

Unique: all columns except PK.

| `Feeder_Id` | **PK** |
| `Feeder_Machine` | Placement machine name |
| `Feeder_Level_1..4` | Placement machine sub-parts |
| `Create_Date` |

## OBJECT_TYPE

`Object_Type_Id` (hex code — see main SKILL), `Object_Type_Name`, `Comments`.

## SPC / SPC_Object / LOG_PROD

Obsolete or reserved — ignore for Nieweb.
