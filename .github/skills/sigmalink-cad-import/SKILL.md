---
name: sigmalink-cad-import
description: |
  Deep expertise on Sigmalink's Data Import (iCAD) module — the CAD editor
  that turns Gerber / CAD-XY / images / JPSys JSON / VIS / PAD / .project
  input into inspection programs for VIT SPI (Pi) and AOI (Kseries) machines.
  Use this skill whenever the question or task involves the Sigmalink CAD
  editor, iCAD project, deepblue-icadmodule-*, cadeditorfx, JavaFX applet,
  jsweet, autopanelization, autolayout, autonaming, pad2component,
  fast link vs standard link, footprint / catalog / propagation rules,
  fiducial edit, id-code zone, skip mark, PI zone, glue deposit family,
  glue_deposits.xml, program variant / variadic / optional component,
  panel referential (lower left / upper right / stencil height),
  subpanel pattern / subpanel layout / carrier, PCB Image Recorder /
  PCB Image Matrix / PI scan, `Preview_..._H###mm_W###mm_###um.jpg`
  image naming convention, Gerber 274x import, ignore/fiducial line syntax,
  CSV column codes (X Y R P J K T ...), unit factor, angle offset,
  rotation clockwise vs counter-clockwise, orientation lower-left/upper-right
  swap, export VIS / export PAD / export .project / publish program,
  reference image bank naming, or when re-implementing this module in Nieweb.
---

# Sigmalink Data Import (iCAD) — CAD Editor Expert Skill

Companion to `sigmalink-legacy`. This skill captures the exact rules,
formats, and workflows that govern Sigmalink's Data Import module (a.k.a.
Sigma Data Import, iCAD module, CAD editor). Authoritative sources:
`pdf_text/Sigmalink-user-guide-V1.6.5.txt` chapters 4–5 and the JSPs under
`VIT_Sigmalink/VIT_Sigmalink/app/sigmalink/WEB-INF/views/cad/`.

## 1. Purpose

Convert design-side CAD data (Gerber stencil apertures, component pick-and-
place CSV, glue-deposit CSV, reference images, JPSys JSON) into inspection
programs consumable by:

- **AOI Kseries** — `.vis` file (component inspection).
- **PI (paste inspection)** — `.pad` file.
- **Sigmalink itself** — `.project` file (round-trip, editable).

Access requires `ROLE_PROGRAMMER` (see `sigmalink-legacy` §4).

## 2. Vocabulary — MUST match Vieweb / Sigmalink usage

| Term                | Meaning                                                                             |
|---------------------|-------------------------------------------------------------------------------------|
| **Panel**           | The physical PCB inspected as one unit; has outline (rectangle or polygon), size (X × Y), optional carrier, optional stencil height, one referential (LL/LR/TL/TR) and one unit. |
| **Subpanel pattern**| A named group defining outline + component/pad layout. Rotation preserves pattern; mirror produces a different pattern. |
| **Subpanel layout** | Arrangement of subpanel patterns on the panel (arrays: N columns × M rows, per-cell rotation 0/90/180/270). |
| **Subpanel**        | Concrete instance produced by the layout. |
| **Fiducial**        | Reference target for machine alignment. Symmetrical shape required. |
| **Id-code zone**    | Rectangle where the inspector reads the panel/subpanel barcode. |
| **Skip mark**       | 2–15 mm mark telling the inspector to skip this panel/subpanel. Scope: whole panel OR a specific subpanel. |
| **Zone (PI only)**  | Region with special stencil height, or ignored region to help auto-programming. |
| **Carrier**         | External size around the panel + panel offset inside it. Can be linked to an image. |
| **Variant**         | Program variant: optional components (present only in some runs) or variadic (different part number per run). Managed via **Manage Variants** on the components layer. |
| **Feedforward**     | S500-licensed feature that injects SPI defects into the AOI coverage. |

## 3. Supported inputs

| Data                                 | PI Pi   | Kseries | Sigma Analyse | Review    |
|--------------------------------------|---------|---------|---------------|-----------|
| Reference image (JPG, stitched)      | opt.    | opt.    | opt.          | required  |
| CAD XY (component CSV)               | opt.    | req.    | req.          | req.      |
| Gerber 274x (stencil / copper / glue)| req.    | opt.    | req.          | req.      |
| Glue deposit CSV                     | -       | opt.    | opt.          | opt.      |
| JPSys JSON (Micronic Jet)            | opt.    | opt.    | opt.          | opt.      |

Outputs: `.vis`, `.pad`, `.project`, reference images (OIS bank).

## 4. High-level process (Sigmalink wizard tab order)

1. **Create / update / import project** (customer + product + side + name;
   name unique, illegal chars `\/?:*"><|`).
2. **Panel** — size, unit (mm/cm/µm/dm/m/in/'' /ft/foot/feet/yd/thou/mil/mils),
   referential (LL default, LR, TL, TR), origin position, carrier, stencil height,
   panel fiducials / id-codes / skips / PI zones.
3. **Image** (optional but required for Review localization) — see §7 for
   the filename convention.
4. **Subpanel pattern** — describe outline of one pattern.
5. **Subpanel layout** — describe arrays that place patterns on the panel.
   Data outside subpanels is IGNORED on export.
6. **Data import** — one file per layer: components CSV, Gerber, glue CSV,
   JPSys JSON; each with its own parsing profile.
7. **Browse object** — link pads to components, edit fiducials, set variants.
8. **Export / Publish**.

A live validation engine annotates every tab (✓ / warning ⚠ / error ✕).
Project can save with errors; only the unique-name constraint is hard.

## 5. CSV import — column codes

Each column of the CSV is mapped to a single letter (the "column variable").
Components CSV codes:

| Code | Meaning                                              |
|------|------------------------------------------------------|
| `X`  | X coordinate of component center                     |
| `Y`  | Y coordinate of component center                     |
| `R`  | Rotation angle in **degrees** (no unit conversion)   |
| `T`  | Topology / reference designator (e.g. `C1000`)       |
| `P`  | Part number                                          |
| `J`  | JEDEC / package name                                 |
| `K`  | Key to distinguish subpanel (only when Data target = n subpanels) |
| `I`  | Ignore this column                                   |

Fiducials in a components CSV: use the same columns, provide any values in
part-number and JEDEC columns (they are ignored), and mark the row via the
"Fiducial" line list.

Glue-deposit CSV codes (subset):

| Code | Meaning                                    |
|------|--------------------------------------------|
| `X`  | X of glue-deposit center                   |
| `Y`  | Y of glue-deposit center                   |
| `F`  | **Family** name (dispenser class)          |
| `T`  | Reference designator of parent component   |
| `P`  | Part number of parent component            |
| `J`  | JEDEC / package                            |
| `I`  | ignore                                     |
| `K`  | subpanel key                               |

Required: `X`, `Y`, `F`. Diameter is looked up from
`conf/data_import/glue_deposits.xml`:

```xml
<gluedeposits>
  <default_diameter>2000</default_diameter>       <!-- µm -->
  <families>
    <family_diameter name="S">1800</family_diameter>
    <family_diameter name="M">2500</family_diameter>
    <family_diameter name="L">3000</family_diameter>
  </families>
</gluedeposits>
```
"Set size" → "use for future projects" writes back into this file.
Values are always micrometers regardless of project unit.

**Parsing profiles** can be saved per import layer and reused later.

## 6. Gerber 274x import

- Ignore-line syntax: `5` (line 5), `5,8`, `5-8` (range). Same syntax for
  the "Fiducial" list.
- Click once on a line number in the preview table → toggle **ignore**.
- Click twice → toggle **fiducial**.
- Contextual "Select same pattern" — selects every pad sharing the same
  aperture code (used to find all panel fiducials).
- The CAD viewer renders large blocks as bitmaps at low zoom for performance;
  individual pads become clickable only when zoomed in. `Ctrl+drag` still
  works at any zoom.
- Autopanelization tool is available ONLY when the current file has shaped
  items and `Data target = Panel`. Draw the first subpanel outline, press
  Enter, the engine finds the other subpanels. Convert pads to fiducials
  BEFORE autopanelization; don't include panel fiducials inside the polygon.

## 7. Reference image filename convention

Images live at `image_storage_path` (default `c:/VIT_Sigmalink/data/pcbi`,
often a network share `W:/3_Conception/3_DATA/PCBImageMatrix`), one
subdirectory per PCB. Filenames are strictly parsed:

```
[Preview_][YYYY-MM-DD-HH.MI.SS_]_H<sizeX><unit>_W<sizeY><unit>_<res><unit>.jpg
```

- `Preview_` prefix — optional.
- Date block — optional, informative only.
- `_H<size>` and `_W<size>` — real panel size in X and Y. Only units allowed
  after the number: `mm`. But the resolution suffix accepts
  `mm|cm|dm|m|in|ft|yd|mil|thou|um`.
- Resolution — pixels/unit, may be a float (e.g. `21.45`).
- Extension MUST be lowercase `.jpg`.

Valid: `Preview_H386.0mm_W218.0mm_45um.jpg`,
`_H386000um_W218.0mm_0.045mm.jpg`.
Invalid: thousand separators, spaces inside the token, uppercase `.JPG`.

An automatic `cache.xml` is maintained in the root image folder — never
delete it manually.

## 8. Coordinate & unit conversion rules

Sigmalink is aggressive about auto-conversion. The rules:

- **Panel referential** (LL/LR/TL/TR) affects **display only**. Data are
  stored canonically internally.
- **Unit** on the panel tab is the "project unit". All imported data are
  converted from their declared import unit into the project unit.
- **Unit factor** — imported X/Y multiplied by `10^n` where `n` ∈ ℤ (e.g.
  `+1` × 10, `-2` ÷ 100). Only for coordinates, not angles.
- **Angle offset** — added to every imported component angle **before**
  the rotation direction correction.
- **Rotation direction** (`Clockwise` or `Counter Clockwise`) — imported
  angles are normalized to Clockwise internally, so mis-selecting this
  produces mirrored pin patterns even when the position looks correct.
- **Orientation** (LL/LR/UL/UR of the source data) — swap LL↔UL mirrors on X,
  swap LL↔LR mirrors on Y.
- **Origin position** — one-shot helper that fills the offset fields to
  center or corner-align the imported data on the panel. Not re-applied
  if panel size later changes.
- **Import angle** — global rotation of the whole file (0 / 90 / 180 / 270°).
  Center of rotation = data origin **before** offset. Custom angles can be
  added to the list via the parameter dial.
- **Data target** — `Panel` (single) / `1 subpanel` (data duplicated per
  layout instance) / `n subpanels` (requires a `K`ey column in file and a
  Key value on each subpanel pattern).

## 9. Component ↔ pad linking

- **Load footprint from catalog** — instant if the component's JEDEC exists.
- **Standard Link (`L` key)** — opens the assignation dialog: numbering
  rules, propagation to all same-JEDEC/part-number components.
- **Fast Link (`F` key)** — no dialog; forced to Standard when the component
  has > 2 pads.
- Default pin labeling follows IPC-7351 (numeric ordered by JEDEC angle 0°).
- Delete footprint → reverts the pads to unassigned; component keeps its
  center + rotation.
- Rotate Component uses the component center as pivot, does NOT touch pads.
- Include / Exclude from feedforward is a per-component flag.
- `Manage Variants` on the components layer creates optional and variadic
  variants; each variant then binds to a `Variant` value in the project
  meta or at inspection time.

## 10. Fiducial editor

Shapes: circle (default), square, diamond, cross, plus, custom (only use
custom for unsupported shapes and ensure symmetry).
Accuracy hint: Unknown / Low / Medium / High — passed through to PI.
Fiducial → contextual menu on any object (pad, id-code, skip, glue) to
promote it to fiducial. Only fiducials are used by machine alignment.

## 11. Move / offset best practice

- `Move` contextual action supports:
  - Absolute move (enter destination coordinates)
  - Relative move (delta X/Y)
  - **Import Offset** — automatically sets the import parameters' `X offset`
    and `Y offset` fields so that the next re-parse lands the file in the
    same place.
- Always save before a bulk move; there is **no undo** in Sigmalink.
- Nudge with arrow keys, hold `Shift` to zoom to a rectangle.

## 12. Export & publish

| Action        | Output                                                       |
|---------------|--------------------------------------------------------------|
| Export VIS    | `.vis` for AOI Kseries CI (components + fiducials).          |
| Export PAD    | `.pad` for PI SPI (pads + fiducials + skips + id-codes).     |
| Export Project| `.project` — full editable round-trip; can be re-imported.   |
| Export reference images | populates the OIS reference image bank.            |
| Publish       | Copies to `publish_folder_path` (`c:/VIT_Sigmalink/data/publish`, ideally a network share). Publishing makes the version an "official version" (tracked in `Version` table in Analyse). |

Reference image bank layout (`standard-library` by default):

```
<bank>/<JEDEC>/<PartNumber>/<JEDEC>_<PartNumber>_<Light>_<Angle>_<Sanction>_<Order>.<ext>
```

- Special chars `[:/*?|<>_\"]` stripped from JEDEC/PartNumber.
- `Light` ∈ `Display`, `LVL0`..`LVL5`.
- `Angle` — one decimal.
- `Sanction` ∈ `OK` (acceptable) / `KO` (not acceptable).
- `Order` — integer; lowest displayed first.
- Extensions: `png`, `bmp`, `jpg`, `jpeg`, `ois`, `vbi`, `otr`.
- Adding a new bank folder requires server restart.

## 13. Project locking

`icad.locked.title=Project locked` — projects are opened for exclusive edit;
`views/cad/cadeditor_locked.jsp` shows the current lock owner. The lock is
released on logout, session timeout, or explicit "unlock" by the same user
or by an administrator.

## 14. Validation rules to reproduce in Nieweb

- Panel size mandatory (SizeX, SizeY > 0).
- Exactly one panel and ≥ 1 subpanel pattern required to export.
- Fiducials required (≥ 3 recommended) for machine alignment.
- Skip mark size 2 ≤ side ≤ 15 mm to be exportable to PAD.
- Panel dimensions must fit into the target PI capacity
  (`WEB-INF/classes/PI-Capacity/pi-conf.xml`, see `sigmalink-legacy` §6).
- Program variants: every variadic component must have ≥ 1 part number per
  variant.
- Glue deposits without a linked component are exportable but produce
  incomplete Analyse correlation.
- Project name unique (case-insensitive) across the Sigmalink server.

## 15. Modernization notes for Nieweb

- **Kill the JNLP JavaFX applet.** Ship a browser-native CAD editor
  (SVG or WebGL). All the jsweet output already targets the browser and can
  seed the port; the domain model in `deepblue-icadmodule-domain` is the
  right blueprint.
- **PI Capacity check** — expose it as a first-class rule so users can't
  publish an over-sized program.
- **`.project` round-trip** — worth keeping as a portable format; contents
  should be JSON in Nieweb.
- **Parsing profiles** — store as records in the Nieweb DB (per-user or
  per-customer) rather than as anonymous XML snippets.
- **Variants** — align with the same model used by the Sigmalink Analyse
  `Version` and `Variant` reporting so a single edit propagates cleanly.
