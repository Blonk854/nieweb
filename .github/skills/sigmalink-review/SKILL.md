---
name: sigmalink-review
description: |
  Deep expertise on Sigmalink's Review module — inline, offline, remote,
  repair, dual-lane and embedded review of AOI/SPI panels after inspection.
  Use this skill whenever the question or task touches Sigma Review,
  sigmareview, deepblue-reviewmodule-*, review_lines.xml, review_layout.xml,
  review_actions.xml, review_defects.xml, review_comments.xml,
  review_custom_messages.xml, review_policy.xml, review_plugins.xml,
  review_inline.xml, review_offline.xml, review_repair.xml, review.xml,
  OIS export / OISPlugin.xml / avision_connector_offline.exe, iCAD project
  assignment, defect classification / sanction, false call / true call /
  repaired / scrap, KO_OPERATOR / OK_OPERATOR, panel/subpanel/component
  faulty, defect list widget, defect image widget, localization widget,
  reference image widget, SPI image widget, shortcuts widget,
  bi-prod review, dual-lane conveyor / IO board, Zebra printer,
  supervisor profile, PRE_REFLOW / POST_REFLOW / SPI equipment,
  session timeout inline (720) vs offline (30), FIFO mode,
  review folder expiration, PANEL_/SUBPANEL_/COMPO_/TERMINAL_/PAD_/PADS_
  defect status constants, or reimplementing any review workflow in Nieweb.
---

# Sigmalink Review — Legacy Expert Skill

Authoritative reference for `deepblue-reviewmodule-*` (avision, common-webapp,
configuration, dataprovider, domain, octopus, offline, plugin-exportois,
repair, services). Companion to `sigmalink-legacy`. Access requires
`ROLE_REVIEWER`; configuration additionally requires `ROLE_CONFIGURER`
or "review manager" flag.

## 1. Review modes

| Mode           | Where installed                             | Trigger              | Notes                                     |
|----------------|---------------------------------------------|----------------------|-------------------------------------------|
| **Embedded**   | On the K-machine's Vision box               | After inspection     | Blocks the next inspection — rare in prod |
| **Inline**     | Dedicated inline PC per line (VIT Vi200/Vi220) | Conveyor IO signal | Uses `C:\VIT_SigmaLink_Inline` installer |
| **Dual-lane inline** | Two inline PCs per Vi24x dual-lane line | Conveyor IO per lane| Requires main SigmaLink server           |
| **Offline**    | Any client                                  | Manual panel search or FIFO | Standard mode is VIT-recommended     |
| **Remote**     | Any client, over network                    | Manual               | JSP `views/review/remote/modal-remote-startup.jsp` |
| **Repair**     | Any client                                  | Manual               | Subpanels can be reviewed independently  |

Session timeouts (from `conf/review/review.xml`):
`REVIEW_SESSION_TIMEOUT_INLINE=720` min, `REVIEW_SESSION_TIMEOUT_OFFLINE=30` min,
`EXPIRATION_TIME_REVIEW_FOLDER=15` days (default; shipped file overrides to `1`).
Review cache lives at `work/review/` and is purged by the smart-purge job
(`smart_purge` table, `SmartPurgeEntity`).

## 2. Configuration file map (all under `c:/VIT_Sigmalink/conf/`)

| File                        | Owns                                                                   |
|-----------------------------|------------------------------------------------------------------------|
| `review.xml`                | Global timeouts, colors, lighting defaults, 3D SPI resolution          |
| `review_lines.xml`          | Datasources, supervisor profiles, lines, equipment (SPI/AOI/Review), conveyor + printer per line |
| `review_layout.xml`         | Screen layouts (`C2_1-1`, `C2_2-1`, `C3_1-1-1`, `C3_1H-1-1-1`, `C3_1H-1-1-1-1H`, `C3_2-1-1`, `C4_1-1-1-1`, `C4_1-2-1-1`, `C4_1-2-1-1-1H`, `C5_1-1-2-1-1`) per review type |
| `review_actions.xml`        | Sanction & tool & custom buttons: label, DB comment, keyboard shortcut, classification, status category, comment-prompt flag |
| `review_defects.xml`        | Defect list: label, icon, visibility, warning message, "acquit" modal flag |
| `review_comments.xml`       | Predefined comment lists per category (Panel, Scrap, Component repaired, Panel repaired, Release, Defect) |
| `review_custom_messages.xml`| Contextual defect messages, triggered by criteria (DefectType, Product, RefDesignator, JEDEC, PartNumber). Empty criteria always match. Importable from a DefectViewer .ini. |
| `review_policy.xml`         | Per-role action enable flags (Simple user vs Review manager)           |
| `review_plugins.xml`        | Plugin registry                                                        |
| `review_inline.xml`         | Inline-mode option overrides (Sigmalink host, auto-release delays)     |
| `review_offline.xml`        | Offline-mode options (FIFO enable, sort order, display angle)          |
| `review_repair.xml`         | Repair-mode options                                                    |
| `conf/plugins/OISPlugin.xml`| OIS export paths per review status                                     |

Global colours (from shipped `review.xml`):
```
NO_STATUS_COLOR = #DFE7E8      RESOLUTION_3D_SPI = 45 (µm/px)
FALSE_CALL_COLOR = #3BB54A     TRUE_CALL_COLOR   = #EC1C24
```
Default lighting IDs the review pre-registers: `C1`, `Color-1`, `C2`, `Color-2`,
`L1`, `LVL1`, `L2`, `LVL2`, `L3`, `LVL3`, `L4`, `LVL4`, `L5`, `LVL5`.

## 3. `review_lines.xml` shape

Three sections (see the shipped file for a real 8-supervisor example):

```xml
<lines>
  <datasources default="<uuid>">
    <datasource uuid="..." type="SQLSERVER" model="AVISION" name="db"
                server="..." port="1433" instance="..."
                user="..." password="..." fromSigmalink="false"/>
    ...
  </datasources>
  <profiles>
    <supervisor_profile id="0" name="Magna1" supervisor="10.75.32.79"
                        databaseUUID="..." invalid="false">
      <parameters>
        <parameter key="platform" value="64bits"/> <!-- or 32bits -->
      </parameters>
    </supervisor_profile>
    ...
  </profiles>
  <line num="1" name="L1" uid="..." trigger="0" active="true">
    <equipment uuid="..." active="true" brand="VIT" model="K"
               name="L1PSTAOI" supervisorProfileId="0" type="POST_REFLOW">
      <parameters>
        <parameter key="prog_matcher" value="(.+)(\.tst)"/>
        <parameter key="prog_value"   value="$1"/>
        <parameter key="is_dual_lane" value="false"/>
        <parameter key="lane_type"    value="0"/>
        <parameter key="ois_folder_path" value="\\host\share\SupervisorImageStorage"/>
        <parameter key="tst_folder_path" value="\\host\share\Data"/>
      </parameters>
    </equipment>
    <review host="..." port="8081" mode="INLINE"
            useBarcodePrinter="false" sigmaConnectProfileId="-1" uuid="...">
      <parameters>
        <parameter key="reset_state_signal_after_unloading" value="false"/>
      </parameters>
    </review>
  </line>
  ...
</lines>
```

`equipment/@type` ∈ `SPI | PRE_REFLOW | POST_REFLOW`.
`equipment/@model` ∈ `K | PI`.
`review/@mode` ∈ `INLINE | OFFLINE | REMOTE | REPAIR | EMBEDDED`.

Ping test: the configuration GUI has a "ping" button per equipment that
colours the field green (reachable) or red (not).

## 4. Widgets

Two are mandatory in every review layout:

- **Defect list widget** — tree of components → body/bridges/connector →
  detailed defects. Colours: deep-red / deep-green = explicit sanction;
  light-red / light-green = computed / inherited. Random access only when
  `Allow random access = true`.
- **Defect image widget** — OIS image with pin-error highlight. `+` zooms
  to next pin in error, `-` un-zooms. Light selector (C1/C2/L1..L5/3D).

Optional widgets:

- **Localization widget** — grid (`columns × rows`) of panel/subpanel views
  showing where the current component sits. Uses reference image from
  Data Import project if available, else generates from TST via
  `deepblue-reviewmodule-avision`, else generates from iCAD synthetic view.
  Needs `tst_folder_path` set on the equipment when no iCAD project is
  linked to the current product.
- **Reference image widget** — draws from the OIS reference bank (see
  `sigmalink-cad-import` §12). Filters "Only same light", "Only same angle",
  "Stretch to widget", show OK/KO/both.
- **SPI image widget** — 3D view from PI machine. Requires traceability
  link (id-code), pad file, and PI kept the good-component images.
- **Shortcuts widget** — draws every enabled action button on-screen.

## 5. Sanctions (canonical defaults)

| Category   | Action                     | Meaning                                            |
|------------|----------------------------|----------------------------------------------------|
| False call | `Acceptable`               | Defect is real but tolerated                       |
|            | `False call`               | Inspection error, no actual defect                 |
|            | `Panel acceptable`         | Set all defects on the panel to acceptable         |
|            | `Subpanel acceptable`      | Same for one subpanel                              |
| True call  | `Not good`                 | Real defect; panel to scrap/repair                 |
|            | `Panel faulty`             | Whole panel true-call                              |
|            | `Subpanel faulty`          | Subpanel true-call                                 |
| Repaired   | `Repaired` (prompted)      | Prompts for comment                                |
|            | `Fast repaired`            | Uses default comment                               |

Custom actions are defined in `review_actions.xml`. Each defines
`label`, `DB button comment`, keyboard shortcut, `classification`,
`status category` ∈ `OK_OPERATOR | REPAIRED | KO_OPERATOR`, and a
`comment prompt` flag.

## 6. Defect Status List (canonical, from user-guide appendix)

Panel-scope:
`PANEL_WARPED, PANEL_STENCIL_OFFSET, PANEL_FOREIGN_MATERIAL, PANEL_PCB_DAMAGED,`
`PANEL_TOO_MANY_DEFECTS, PANEL_BAD_PROGRAM, PANEL_IDENTIFICATION_ERROR,`
`PANEL_INSPECTION_ERROR, PANEL_SKIP_NOT_READ, PANEL_SOLDER_BALL_SPLASH,`
`PANEL_FIDUCIAL_ERROR, PANEL_NOT_INSPECTED, PANEL_EJECT_ERROR, PANEL_VISION_ERROR`

Subpanel-scope:
`SUBPANEL_IDENTIFICATION_ERROR, SUBPANEL_SKIP_NOT_READ, SUBPANEL_FIDUCIAL_ERROR,`
`SUBPANEL_TOO_MANY_DEFECTS, SUBPANEL_SKIPPED, SUBPANEL_VISION_ERROR`

Component-scope:
`COMPO_POSITION_OFFSET, COMPO_POSITION_X, COMPO_POSITION_Y, COMPO_POSITION_Z,`
`COMPO_POSITION_THETA, COMPO_TILTED, COMPO_TOMBSTONE, COMPO_POLARITY,`
`COMPO_IDENTIFICATION, COMPO_EXCESS_COMPONENT, COMPO_BIL_BOARD_UPSIDE_DOWN,`
`COMPO_WRONG_COMPONENT, COMPO_THERMAL_DAMAGE, COMPO_MECHANICAL_DAMAGE,`
`COMPO_INSPECTION_ERROR, COMPO_MISSING, COMPO_VISION_ERROR`

Terminal / lead-scope:
`TERMINAL_WICKING_UP, TERMINAL_BGA_HEAD_IN_PILLOW, TERMINAL_INCOMPLETE_REFLOW,`
`TERMINAL_BROKEN_SOLDER, TERMINAL_DISTURBED_SOLDER, TERMINAL_LIFTED_LEAD,`
`TERMINAL_VOID, TERMINAL_SOLDER_PASTE_EXCESS, TERMINAL_SOLDER_PASTE_INSUFFICIENT,`
`TERMINAL_DEWATTING_UNWETTING, TERMINAL_MISSING, TERMINAL_BAD_PIN_ALIGNEMENT,`
`TERMINAL_BAD_PIN_ALIGNEMENT_X, TERMINAL_BAD_PIN_ALIGNEMENT_Y, TERMINAL_JOIN,`
`TERMINAL_JOIN_BRIDGE, TERMINAL_VISION_ERROR`

Pad-scope:
`PAD_MISSING, PAD_POSITION_X, PAD_POSITION_Y, PAD_VOLUME, PAD_AREA,`
`PAD_HEIGHT, PAD_SHAPE2D, PAD_SHAPE3D, PAD_CUSTOM, PAD_VISION_ERROR`

Pads (pair)-scope:
`PADS_BRIDGE, PADS_TOMBSTONE, PADS_VISION_ERROR`

Catch-all: `UNSUPPORTED_DEFECT`.

Nieweb MUST preserve these exact constant names — Sigmalink stores them in
DB and in exported OIS files; renaming them breaks historical joins.

## 7. Tools available inside a review

- **Classification** — change the defect type + pin + optional comment.
- **Change ID codes** — fix a mis-read barcode at panel or subpanel scope.
- **Add current image as reference** — pushes the current OIS into the
  reference bank (choose bank + OK/KO).
- **Assign Sigma Data Import project to current product** — links product →
  iCAD project so the localization widget can render. Cached per-product.

## 8. Startup screens

- Offline / Repair — filters on the left (line, program, date range, status),
  panel list on the right (default: last 200 not-yet-reviewed). Filters
  persist between panels.
- FIFO — pick inspection machine + program, system auto-picks next.
- Inline — lists last 50 inspected panels; press start when one appears.
  Confirmation of "Send result before unloading" toggles whether the AVision
  supervisor gets the result before the conveyor eject signal.

## 9. Options exposed in the review-configuration GUI

Global (from `review.xml` and general configuration):
`Review folder expiration period`, `Inactivity timeout (min)`,
`Logout on exit review`, `Inline review session duration (min)`,
`Offline review session duration (min)` (hardcoded minimum: 1 min).

Per-mode overrides:
`Allow FIFO`, `Allow reviewed`, `Allow random access`,
`Display panels inspected as good`, `Automatic release Good panels delay (s)`,
`Confirm sanction update`, `Allow same sanction`, `Confirm end review`,
`Sort order` ∈ SubPanel|Jedec|Partnumber, `Display at` (rotation angle),
`Send result before unloading`, `Autorotate 3D`, `Start on ID code match`,
`Automatic release after (s)`.

Layout errors that block save:
- missing Defect list or Defect image widget,
- widget instantiated twice,
- widget requires a license not present.

## 10. Custom messages

Triggered when a defect is selected. Displayed as a modal (`To confirm=true`)
or as a bottom drawer (default). Criteria are ANDed within a set; multiple
sets are ORed. Any set with all inputs empty is warning-flagged and always
fires. Import from a DefectViewer `.ini` via the "Import" toolbar button.

## 11. OIS export plugin

Configured via the OIS Export tab and stored in `OISPlugin.xml`. Enable via
`active=true`. Separate destination path per review status (OK_OPERATOR,
KO_OPERATOR, ACCEPTABLE, REPAIRED) or a single root when
"same root folder = true". Optional per-line and per-equipment subfolders.

## 12. Bi-prod & dual-lane specifics

- Bi-prod requires "bi production" configured in Vision. Only single-lane.
- One panel review chains automatically to the other side. If one side is
  GOOD, a dialog auto-closes after `Automatic release Good panels after`
  (if > 0); otherwise `Automatic release after` applies (the smaller
  non-zero wins).
- Dual-lane inline: two inline installations per line, each connected to a
  separate IO board channel; they share the main SigmaLink server for
  reference data.

## 13. Printer configuration (Zebra™)

Per-line in `review_lines.xml`: `activate printer` bool, `printer name`
(Windows), `printer type`, `printer language`, position (X,Y dots from
top-left), angle (0/90/180/270°), bar-code format ∈ code39 / code93 /
code128 / LOGMARS, bar width (narrow), bar ratio (wide), bar height,
human-readable flag, "Prompt user when print done", "Print OK-operator
panels".

## 14. Modernization notes for Nieweb

- Move all `review_*.xml` into first-class Postgres tables so the current
  file-locking hazards go away and the Configuration GUI becomes trivial.
- Keep the two mandatory widgets rule, but ship a modern widget SDK so
  customers can add their own without recompiling.
- Preserve every defect-status constant character-for-character to keep
  history joinable across Vieweb / Sigmalink / Nieweb.
- The OIS reference bank layout (see `sigmalink-cad-import` §12) is a
  filesystem convention shared with the reference-image widget — make it
  Nieweb's canonical layout too.
- The AMQP `Sigma Connect` pipeline (see `sigmalink-legacy` §3) is how
  review results flow between the review PC and the AVision supervisor —
  worth reproducing rather than reinventing.
- Sigmalink's session token model (per-module license) is arguably
  outdated; concurrent-user licensing at the app layer is a better fit
  for Nieweb.
