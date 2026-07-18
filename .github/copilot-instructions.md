# Nieweb project instructions

Nieweb is a modernized re-implementation of two legacy **ViTechnology (VIT)**
reporting/inspection webapps that both consume the VIT **Superviseur**
production database (Vision3D CR4 / Vision20 CR5 schema):

1. **Vieweb 1.6.2** – original reporting webapp (Struts 1.2, Java 7, MySQL
   internal DB). Nieweb replaces its features first.
2. **Sigmalink 1.6.5** (a.k.a. σLink / Deep Blue) – newer client/server
   suite (Spring 5, Hibernate 5, embedded Jetty 9, HSQLDB/PostgreSQL) with
   Data Import (CAD editor), Review (inline/offline/remote/repair/dual-lane),
   Analyse (Live / Line Performance / Product / Panel / Cp-Cpk), Sigma
   Connect (AMQP), SigmaLine feedforward, and companion apps VIT_Analyse
   and DBQuery-PI-updater. Nieweb should adopt selected Sigmalink features
   (modular UI, CAD-first data model, configurable review, Analyse
   dashboards, PI-capacity guard, 5-language i18n).

## Repository layout

> **Note on the legacy install trees.** `VIT_Vieweb/` and `VIT_Sigmalink/`
> together are ~4.5 GB and are **git-ignored** (see `.gitignore`). They live
> on disk in the workspace so the AI and developers can inspect raw JSPs,
> XML configs, JARs, and the original PDFs, but they are **not versioned**.
> The distilled facts we do version are in `.github/skills/*/SKILL.md` and
> in `pdf_text/`. If either tree is missing from a fresh clone, copy it
> back from the archive before asking questions that need raw file lookups.

- `VIT_Vieweb/` – archived legacy Vieweb install (read-only reference,
  git-ignored). Do **not** edit files under this folder; treat it as the
  golden source of the old Vieweb behavior.
  - `app/VIEWEB/WEB-INF/` – Struts 1.2 / Tiles / Hibernate 2 configuration
  - `app/VIEWEB/WEB-INF/classes/*.properties` – i18n resources describing the
    UI (EN/FR)
  - `app/VIEWEB/WEB-INF/classes/sqlscript/drop_and_create.sql` – Vieweb's
    *internal* MySQL schema (reports, users, filters, groups…)
  - `tools/db/create.sql`, `init.sql` – bootstrap of Vieweb's internal DB
  - `server/apache-tomcat-7.0.35/` – bundled Tomcat 7 (Java 7 / 32-bit)
  - `logs/` – historical batch/tomcat logs
- `VIT_Sigmalink/` – archived legacy Sigmalink install (read-only reference,
  git-ignored). Do **not** edit files under this folder; same golden-source
  rule as Vieweb.
  - `VIT_Sigmalink/app/sigmalink/WEB-INF/` – Spring 5 / Hibernate 5 config
    (`web.xml`, `webapp-context.xml`, `classes/META-INF/spring/*.xml`).
  - `VIT_Sigmalink/app/sigmalink/WEB-INF/classes/hibernate/*.hbm.xml` – the
    six internal entities (Customer, Product, UserEntity, ParameterEntity,
    FunctionalLogEntity, SmartPurgeEntity).
  - `VIT_Sigmalink/app/sigmalink/WEB-INF/classes/sql/default_data.sql` –
    seed users, authorities, ref_functional_level, parameters.
  - `VIT_Sigmalink/app/sigmalink/WEB-INF/classes/i18n/messages*_{en,fr,de,es,zh}.properties`
    – 5-language i18n bundles.
  - `VIT_Sigmalink/app/sigmalink/WEB-INF/classes/PI-Capacity/pi-conf.xml`
    – physical inspection window per Pi model.
  - `VIT_Sigmalink/app/sigmalink/WEB-INF/views/{admin,analyze,cad,configure,
    data,home,i18n,monitor,review,services,startprocess,tools}/*.jsp` – UI.
  - `VIT_Sigmalink/conf/` – runtime XML config (see the `sigmalink-review`
    and `sigmalink-analyse` skills for a per-file map).
  - `VIT_Analyse/app/analyse.war` – companion Analyse WAR (embedded Jetty,
    port 8082, URL `/analyse`).
  - `1.6.5/DBQuery-PI-updater-V1.6.5/` – installer for the DBQuery Pi
    upgrade on SPI Pi machines.
- `pdf_text/` – text extracted from all project PDFs (Vieweb + Sigmalink +
  Analyse). **Versioned.** This is the primary source of truth for PDF
  content since the PDFs themselves live under the git-ignored install
  trees. Regenerate via `extract_pdfs.py` / `extract_sigmalink_pdfs.py`.
- Original PDFs (on disk, git-ignored except the one at workspace root):
  - Workspace root (versioned): `Database fields and constants (Vision3D CR4).pdf`.
  - `VIT_Vieweb/Manuals/`: `Vieweb-user-guide-V1.6.2.pdf`,
    `Vieweb-install-note-V1.6.2.pdf`, `Vieweb-release-note-V1.6.2.pdf`.
  - `VIT_Sigmalink/1.6.5/`: `Sigmalink-user-guide-V1.6.5.pdf`,
    `Sigmalink-release-note-V1.6.5.pdf`, `Analyse-user-guide-V1.6.5.pdf`,
    `Analyse-release-note-V1.6.5.pdf`.

## Databases – do not confuse them

1. **Vieweb internal DB** (MySQL, schema in `VIT_Vieweb/tools/db/create.sql`)
   – reports, entities, filters, users, groups, automatic treatments, shifts,
   production lines, MSA limits. Nieweb owns this.
2. **Sigmalink internal DB** (HSQLDB by default at
   `D:/Sigmalink/data/VIT_Sigmalink/data/db/sigmalink`, or PostgreSQL; schema
   in `VIT_Sigmalink/app/sigmalink/WEB-INF/classes/hibernate/*.hbm.xml`) –
   users, authorities, customer, product, parameter, functional_log,
   ref_functional_level, smart_purge. Nieweb owns this if we absorb
   Sigmalink features.
3. **VIT Superviseur / AOI production DB** (SQL Server / Oracle / Access;
   Vision3D CR4 schema documented in
   `Database fields and constants (Vision3D CR4).pdf`)
   – read-only source of `PANELS`, `CARDS`, `TESTED_OBJECT`, `PIN`,
   `PIN_MEASURE`, `MACHINE`, `PRODUCT`, `RECIPE`, `LIBRARY`, `OPERATOR`,
   `TOLERANCE`, `PART_NUMBER`, `JEDEC`, `FEEDER`, `OBJECT_TYPE`, and the
   `*_HISTO` tables. Nieweb consumes it. **Never write** to this DB.

VIT explicitly warns that heavy or slow queries against the production DB can
stall the inspection cycle time and stop the SMT line — every query Nieweb
issues against it must be reviewed for performance impact.

## Where domain knowledge lives (skills)

On-demand expert knowledge is packaged as skills under `.github/skills/`:

- **`vieweb-legacy`** – legacy Vieweb 1.6.2 codebase, features, known bugs,
  install/run procedure, i18n keys, Struts action map, entity types.
- **`sigmalink-legacy`** – Sigmalink 1.6.5 stack, roles, modules, on-disk XML
  config map, internal DB tables (HSQLDB/PostgreSQL), i18n bundles, license
  tokens, and known behaviours worth fixing in Nieweb.
- **`sigmalink-cad-import`** – Sigmalink Data Import (iCAD) module: CSV/
  Gerber/JPSys imports, coordinate conversion, variant management, VIS/PAD/
  .project export, reference-image bank naming.
- **`sigmalink-review`** – Sigmalink Review module: embedded/inline/dual-lane/
  offline/remote/repair modes, XML configuration files, defect status
  constants (PANEL_/SUBPANEL_/COMPO_/TERMINAL_/PAD_/PADS_), custom messages,
  OIS export, printer/conveyor.
- **`sigmalink-analyse`** – Sigmalink Analyse + companion VIT_Analyse:
  Live / Line Performance / Product / Panel / Cp-Cpk dashboards, DBQuery Pi/K,
  defect ordering, panel-side mapping, `analyse_layout.xml` widget grid.
- **`vit-aoi-database`** – Vision3D CR4/CR5 schema (tables, columns, bit-flag
  encodings for `Anomaly_*`, `Error_Table*`, `*_Status`, `Not_Inspected_Cause`,
  `Repair_State_result`, `Object_Type_Id`, `Measure_Type`, maintenance rules).
- **`aoi-quality-metrics`** – formulas and interpretation for FPY, DPMO, PPM,
  Cp, Cpk, EV, %EV, GR&R, MSA, panel vs board analysis.

Load a skill via `/` (slash) in chat or ask a question whose description
matches — the skill descriptions are keyword-rich for discovery.

## Custom agents

- **`nieweb-architect`** – for design decisions, tech-stack trade-offs, and
  planning how a legacy Vieweb feature should be reimplemented in Nieweb.
- **`aoi-domain-expert`** – for Q&A on the AOI process, the Superviseur schema,
  KPI definitions, and how to write correct, safe SQL against the production DB.
- **`sigmalink-domain-expert`** – for Q&A on Sigmalink modules (Data Import,
  Review, Analyse, Configure, Monitor, Feedforward), its XML configuration
  files, defect-status constants, DBQuery Pi/K topology, and how to port a
  Sigmalink feature into Nieweb.

## Working ground rules

- Preserve the semantic meaning of every legacy feature before rewriting it.
  If you cannot find the corresponding legacy code/behavior, ask before
  designing new behavior.
- Use the **English** i18n resources (`*_en.properties`) as the canonical name
  for a legacy feature; French keys exist for parity.
- All bit-flag masks (`Anomaly_BR/AR`, `Error_Table`, `Error_Table_AR`) are
  documented in the AOI database skill – reproduce them exactly.
- KPI formulas are defined by VIT (see `aoi-quality-metrics` skill). Do **not**
  invent alternative definitions — line engineers rely on numeric parity with
  Vieweb 1.6.
- All timestamps in the Superviseur DB (`Panel_Numeric_Date`,
  `Repair_Numeric_Date_Hour`, `File_Date`, `Library_Date`) are ANSI `time_t`
  seconds since 1970-01-01 UTC. `Create_Date` on some legacy tables is a
  human-readable float (e.g. `2.0050128183733` = 2005-01-28 18:37:33).
- Legacy Vieweb’s **known open bugs** (from release notes) that Nieweb must
  fix:
  - #9699  – "Send Reports by email" fails in some cases
  - #12421 – Weekly report totals differ from daily totals
  - #11211 – Wrong defect displayed
  - #18915 – Cannot export more than 250 columns

## Sigmalink-specific ground rules

- `VIT_Sigmalink/` and `VIT_Analyse/` are read-only golden references — do
  not edit anything under them.
- Default admin credentials `admin/admin` and SHA-1 password hashes must be
  rotated / migrated (bcrypt or argon2) before Nieweb ships. Same for the
  hard-coded `admin/admin` AMQP broker credentials in Sigma Connect.
- The JNLP / JavaFX 8 CAD Editor applet must be replaced with a browser-native
  editor (WebGL / Canvas). Do not attempt to keep the applet alive.
- The **PI capacity guard** in
  `VIT_Sigmalink/app/sigmalink/WEB-INF/classes/PI-Capacity/pi-conf.xml`
  is mandatory — Nieweb must enforce it before triggering DBQuery Pi
  requests so that inspection cycle time is not degraded.
- The Sigmalink licence file (`conf/sigmalink.licence`) gates per-module
  access. Preserve the token model in Nieweb even if licensing is
  simplified.
- **Defect status constants** (`PANEL_*`, `SUBPANEL_*`, `COMPO_*`,
  `TERMINAL_*`, `PAD_*`, `PADS_*`, `UNSUPPORTED_DEFECT`) must be preserved
  character-for-character across products (Vieweb, Sigmalink, Nieweb) so
  historical rows remain queryable.
- **Defect ordering** (`dbqueryK/defectOrders`, `dbqueryPI/defectOrders` in
  `sigmalink_configuration.xml`) and **`panelSideMapping`** must be
  configurable at runtime, not hard-coded.
- KPI parity: Sigmalink Analyse, Vieweb reports, and Nieweb must agree
  numerically for FPY / DPMO / Cp / Cpk / GR&R over the same time window.
  Reuse the `aoi-quality-metrics` formulas exactly.
- Never write to the VIT Superviseur DB from any Sigmalink or Nieweb code
  path — the same performance warning as Vieweb applies.

## Development databases (live AOI Superviseur DBs)

Nieweb reads from two live AOI Superviseur databases — one per reflow
stage. Both are SQL Server 2022 Enterprise and both follow the Vision3D
CR4 / Vision20 CR5 shape documented in the `vit-aoi-database` skill, but
the pre-reflow instance is on an older schema revision and is missing
several tables.

- **Post-reflow (Phase 1) — `HLYMSSQL2 / HLYAOI`.** Schema `5.0`,
  DATABASEID 1762100668. Contains `PIN`, `PIN_MEASURE`, all four
  `*_HISTO` tables, and the `Barcode_Product` view. `Panel_Status`
  values `{-2,-1,0,1,2}`. Login `svc_hlyaoiprod` currently has
  **write** access because a read-only account was not yet provisioned
  — read-only discipline is enforced in code (see below).
- **Pre-reflow (Phase 2) — `HLYMSSQL1 / MEAOI`.** Schema `4.3.1`,
  DATABASEID 1783421400. **Missing entirely:** `PIN`, `PIN_MEASURE`,
  `CARDS_HISTO`, `PANELS_HISTO`, `PIN_HISTO`, `TESTED_OBJECT_HISTO`,
  `Barcode_Product` (+ related views). Adds paste-print / stencil
  columns to `PANELS` and `CARDS` (`PastePads_*`, `Stencil_D*`,
  `Number_Of_Pads`, `Nb_Of_Tests_On_Pads`). Lacks post-reflow-only
  columns `IS_LAST_INSPECTION`, `IPC610_INSPECTION_CLASS`, the
  `CONVEYING_TIME_S` / `BUY_SELL_PANEL_TIME_S` / `WAITING_REVIEW_TIME_S`
  timing fields on `PANELS`; the `DPMO_*_DEFECT_NB` helpers and
  `OPERATOR_ID` on `CARDS`; and the `ERROR_TABLE_AR`,
  `NOT_INSPECTED_CAUSE`, `MES_TILT_UM`, `MEASURES`, `EXPECTED_POS*_UM`,
  `EXPECTED_ANGLE_DG` columns on `TESTED_OBJECT`. `RECIPE` lacks
  `VARIANT_NAME`. `OBJECT_TYPE` lacks `FOREIGN_MATERIAL` (33554432).
  `Panel_Status` includes an extra value `3`. Login `meaoiprodinq` is
  properly read-only. Only the pre-reflow DB has meaningful `FEEDER`
  data (594 rows vs 3 stubs on post-reflow).

**Never write** to either DB. Never mix them in a single query. Any
Nieweb feature that needs pin-level data, review audit trail, barcode-
to-product lookup, or `IS_LAST_INSPECTION` filtering is post-reflow
only; any feature that needs paste-print / stencil metrics or per-
machine feeder analytics is pre-reflow only. The data-adapter layer must
expose these capability flags rather than assume a single unified schema.

**Credentials.** SQL Server auth. Both connections' credentials live in
a git-ignored `.env` at the repo root under two prefixes — see
`.env.example`:

- `AOI_POSTREFLOW_SERVER`, `AOI_POSTREFLOW_DATABASE`,
  `AOI_POSTREFLOW_USER`, `AOI_POSTREFLOW_PASSWORD`
- `AOI_PREREFLOW_SERVER`, `AOI_PREREFLOW_DATABASE`,
  `AOI_PREREFLOW_USER`, `AOI_PREREFLOW_PASSWORD`
- Shared: `AOI_CONNECT_TIMEOUT`, `AOI_QUERY_TIMEOUT`.

Never paste any password into chat, into a commit message, or into any
file that isn't `.env`.

**Read-only discipline (mandatory for both DBs).** Every code path that
touches either Superviseur DB must:

- Refuse to issue `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `TRUNCATE`,
  `MERGE`, `EXEC`, `GRANT`, `REVOKE`, `CREATE`. The reference guard is in
  `tools/db/probe-schema.ps1` (regex-based statement inspector).
- Prefix every query batch with:
  `SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SET NOCOUNT ON;`
- Use `WITH (NOLOCK)` on `SELECT` from production-shaped tables so we
  can't block or be blocked.
- Set `ApplicationName='Nieweb-<script-name>-<source>'` on connections
  (e.g. `Nieweb-probe-schema-postreflow`) so DBAs can identify our
  sessions.
- Time-window filter every query on the large tables (`PANELS`, `CARDS`,
  `TESTED_OBJECT`, `PIN`, `PIN_MEASURE`, `*_HISTO`) — never a bare
  `SELECT * FROM …`.

**Reference tooling.** `tools/db/probe-schema.ps1` and
`tools/db/probe-schema-extra.ps1` take a `-Prefix` parameter
(`AOI_POSTREFLOW_` default, or `AOI_PREREFLOW_`) and write per-source
CSVs into `tools/db/out/<sourceTag>/` (git-ignored). They enforce the
guards above; any new dev script must adopt the same pattern (load
`.env`, refuse write keywords, prefix isolation level, tag
`ApplicationName`, per-source output directory).
