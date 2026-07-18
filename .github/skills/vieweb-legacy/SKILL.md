---
name: vieweb-legacy
description: 'Expert knowledge of the legacy ViTechnology Vieweb v1.6.2 reporting web-application archived under VIT_Vieweb/. Use when: mapping a legacy feature to a Nieweb design; explaining how a Struts action, JSP, or Hibernate entity worked; locating the correct English i18n key for a UI label; reproducing the entity model (Report, ReportGroup, AbstractEntity, TableEntity, GraphEntity, MSAEntity, ProcessCapabilityEntity, TestEmptyMasterEntity, TracabilityEntity, Comment, Filter, FilterValue); understanding user roles (Reader / Author / Admin), automatic treatments, batch/email, MSA limits, tolerance intervals, production lines, shifts, or the Vieweb internal MySQL schema; diagnosing legacy known bugs (#9699 email, #12421 weekly-vs-daily totals, #11211 wrong defect, #18915 250-column export limit); planning migration paths. Do NOT edit anything under VIT_Vieweb/ — it is the read-only golden reference.'
---

# Vieweb 1.6.2 legacy expertise

This skill packages everything needed to understand the archived Vieweb 1.6.2
application located under `VIT_Vieweb/` so it can be re-implemented as Nieweb
without losing behavior.

## Golden rule

**`VIT_Vieweb/` is read-only.** Never modify it. Always cite legacy behavior
by pointing at the specific file/line under this folder.

## Stack (legacy)

- Java 7 (32-bit JDK 1.7.0_40), Apache Tomcat 7.0.35 (bundled).
- Struts 1.2.7 + Tiles + Struts taglibs (`struts-bean`, `struts-html`,
  `struts-logic`, `struts-nested`, `struts-tiles`, `vieweb.tld`).
- Hibernate 2.1.7 (`net.sf.hibernate`) → MySQL 4/5 internal DB
  (`jdbc/VIEWEB_MySQL`, dialect `MySQLDialect`).
- Charts: `jfreechart-1.0.0-rc1` + `jcommon`. Excel export: `poi-2.5.1` and
  `jxl`. Mail: `mail-1.3.3ea`, `activation-1.0.2`.
- External production DBs supported via `jtds-1.0.3` (SQL Server) and
  `classes12` (Oracle), plus Access via ODBC bridge. Default in
  `web.xml` = `SQLSERVER`.
- Startup script: `VIT_Vieweb/start_server.bat` (requires 32-bit JDK 1.7),
  service scripts under `VIT_Vieweb/tools/`. Server URL:
  `http://<host>:8081/VIEWEB` — default login `admin` / `admin`.

## Java package layout

Root package: `fr.vitechnology.vieweb`

| Package | Role |
|---|---|
| `archi.servlets.InitServlet` | Struts action servlet subclass, entry point |
| `archi.filter.HibernateSessionFilter` | opens/closes Hibernate session per `*.do` |
| `archi.filter.SetCharacterEncodingFilter` | forces `ISO-8859-1` |
| `archi.bean.Parameter` | key/value table `parameter` |
| `admin.bean.*` | `User`, `ProductionLine`, `Machine`, `ShiftUnit`, `Database` (external client DB config), `AutomaticTreatment`, `Email` |
| `admin.struts.form.*` / `admin.struts.action.*` | admin UI (see `struts-admin.xml`) |
| `report.bean.*` | `Report`, `ReportGroup`, `AbstractEntity`, `Filter`, `FilterValue`, and the entity templates |
| `report.struts.form.*` / `report.struts.action.*` | report UI (see `struts-config.xml`, `struts-report.xml`) |

Struts configs to consult:
- `WEB-INF/struts-config.xml` – report actions
- `WEB-INF/struts-admin.xml` – admin actions
- `WEB-INF/struts-report.xml` – Tiles plugin for report views
- `WEB-INF/tiles-report.xml`, `tiles-admin.xml` – Tiles view composition
- `WEB-INF/web.xml` – context params (default language, batch, SMTP, license)

JSP roots:
- `app/VIEWEB/WEB-INF/jsp/admin/` – parameters, users, DB list, productivity,
  production lines, MSA limits, shifts, batch, testGraphique/testExport/testSQLRequest.
- `app/VIEWEB/WEB-INF/jsp/report/` – report list, report properties, entity
  editors (`tableEntity.jsp`, `graphEntity.jsp`, `MSAEntity.jsp`,
  `processCapabilityEntity.jsp`, `TracabilityEntity.jsp`,
  `TestEmptyMasterEntity.jsp`), filters, dynamic params, mail, lock/unlock,
  print & Excel export.

## Domain model – Vieweb *internal* DB (MySQL)

Source: `VIT_Vieweb/tools/db/create.sql` +
`app/VIEWEB/WEB-INF/classes/sqlscript/drop_and_create.sql`.
Never confuse this with the AOI Superviseur DB (see the
`vit-aoi-database` skill).

Core tables (all lowercase; PK columns in `SNAKE_CASE`):

- `user` – login, password (SHA-1 hex), profile (`Admin` | `Author` | `Reader`),
  language (`en` | `fr`), email.
- `clientdatabase` – connection settings for external Superviseur DBs
  (`DATABASE_TYPE` in {`SQLSERVER`, `ORACLE`, `ACCESS`}, plus login/password,
  server, port, SID). Each report entity binds to one client DB via
  `templateEntity.DATABASE_ID`.
- `report` – header/footer/logo, `IS_DYNAMIC`, `REFRESH_FREQUENCY`,
  `IS_ONE_COLUMN`, optional password (`Lock`), `USER_NAME_CREATION`, group FK.
- `reportgroup` – 1:N to `report`.
- `user_report` – many-to-many that drives the per-user "Home page definition".
- `abstractentity` (`ENTITY_ORDER`, `REPORT_ID`) → `templateEntity`
  (`DESCRIPTION`, `TITLE`, `TITLE_AUTO`, `DATABASE_ID`) → one of the
  concrete template tables:
  - `templatetable` – FPY / DPMO tables (`TABLE_TYPE`, `FPY_TABLE`,
    `DPMO_TABLE`, `DPMO_DETAIL`, `DETAIL_AFTER_DIAG`, `JEDEC_DETAIL`,
    `PANEL_FPY_TABLE`, `DEFAULT_TYPE_DETAIL`, `LAST_ANALYZED`,
    `LAST_ANALYZED_CRITERIA`, `DATE_INTERVAL`).
  - `templategraph` – Error/Deviation/Trend charts (`GRAPH_TYPE`,
    `ERROR_GRAPH`, `ANALYZED_BY`, `DIVIDED_BY`, `SCALE`,
    `REPRESENTATION`, `DEVIATION_GRAPH`, `TREND_GRAPH`,
    `TREND_DECOMPOSITION`, `IS_PANEL`, `DATE_INTERVAL`,
    `LAST_ANALYZED*`).
  - `templatemsa` – MSA (`CAPABILITY`, `JEDEC`, `REPETABILITY`,
    `REPRODUCTIBILITY`, `DEV_X`, `DEV_Y`, `DEV_THETA`).
  - `templateprocesscapability` – Process capability
    (`CP_COMPO_LINE`, `CPK_COMPO_LINE`, `CP_PASTE_LINE`,
    `CPK_PASTE_LINE`, `DPMO_LINE`, `FPY_DIAG_LINE`,
    `MACHINE_EFFICIENCY`, `AVG_CYCLE_DURATION`, `NB_INSPECTION`,
    `PANEL_CALCUL`, `PRODUCTION_LINE_ID`, `DATE_INTERVAL`).
  - `templatecomment` – free-text comment entity.
  - Traceability and Test-Empty-Master entities exist as JSPs / form beans
    but reuse the templateEntity + filter mechanism at runtime.
- `filter` (`FILTER_TYPE`, `OPERATOR`, `FROM_VALUE_AS_STRING`,
  `TO_VALUE_AS_STRING`) + subtype tables `ColBinaryFilter`,
  `ColEnumFilter`, `ColStringFilter`, `ColIntFilter`, and
  `filtervalue` for multi-value filters (IN / BETWEEN…).
- `productionline` + `machine` (`MACHINE_ORDER`, `CATEGORY`, `IMAGE`) –
  logical grouping used only by the Process Capability entity.
- `shiftunit` (`SHIFT_UNIT_ORDER`, `HOURS`, `MINUTES`) – shift breakpoints
  on a 24 h cycle; drives the "By shift" analysis axis.
- `automatictreatment` (`FREQUENCY`, `NEXT_TREATMENT`,
  `IS_MAIL_GENERATION`, `IS_FILE_GENERATION`, FK report + user) + `email`.
- `parameter` – runtime key/value store (`PARAM_KEY`, `PARAM_TYPE`,
  `PARAM_VALUE`) used for MSA limits, tolerance intervals, `GR_R`
  constant (default 4.33), and other tunables from
  `ViewebParameters.properties`.
- Bootstrap: `tools/db/init.sql` seeds
  `admin / admin` (SHA-1 `D033E22AE348AEB5660FC2140AEC35850C4DA997`).

## Features (canonical English names)

Source: `Vieweb-user-guide-V1.6.2.pdf` (extracted at
`pdf_text/Vieweb-user-guide-V1.6.2.txt`).

### User profiles
- **Reader** – online analysis + view predefined reports.
- **Author** – Reader + create/modify/duplicate/lock reports, manage report
  groups, define automatic treatments.
- **Admin** – Author + manage databases, users, MSA limits, application
  parameters, production lines, shifts, batch on/off.

### Real Time View entities
1. **Chart** (`templategraph`): three flavors:
   - *Error chart* – Draw type `By day` / `By shift` / `Top 10`; Analyzed by
     board number, defect, P&P machine, P&P sub-element 1-4, inspected object,
     JEDEC, part number, product, repair comment/status, reference designator,
     AOI. Scale = `DPMO` / `PPM` / real values. Representation histogram or
     table.
   - *Deviation chart* – `Average(X|Y|Z|surface|theta)`, `X`, `Y`, `Z`,
     `Surface`, `Theta`; overlays `± tolerance`, average, `±3σ`.
   - *Trend chart* – `Cpk`, `Cp`, `DPMO*`, `FPY*` over decomposition
     1h/3h/6h/12h/shift/day/week/month. Panel vs board.
2. **Table** (`templatetable`): `FPY` (per AOI or per product; panel or
   board) or `DPMO` (per AOI/defect/JEDEC/part number/product/reference
   designator, with optional package / error-type / after-diagnostic detail).
3. **MSA** (`templatemsa`): Capability (Cp, Cpk), Repeatability (EV,
   %EV), Reproducibility (GR&R) on `Reference Designator` or `Package`
   over Deviation X/Y/Theta. Requires a **dedicated** database.
4. **Process Capability** (`templateprocesscapability`): production-line
   dashboard with any of `Cp/Cpk (compo, paste)`, `DPMO`, `FPY_Diag`,
   `Machine efficiency`, `Avg cycle duration`, `Nb inspections`.
   Requires production lines to be defined.
5. **Test Empty Master**: list of models likely to escape defects when a
   product is inspected on an empty (known-good) panel. Requires a
   dedicated DB fed with empty-panel inspections.
6. **Traceability**: per-panel or per-board drill-down (3 tables for
   panels, 2 for boards).

### Filter operators (per filter kind)
See §3.1.2 of user guide. Only these combinations are legal:

- Board number / Panel Bar code / Board ID code / Reference designator /
  Product / JEDEC / P&P machine / P&P sub-element 1-4 / Part number:
  `Equal`, `Different`, `In`, `Not In`, `Like`, `Not Like`
  (Panel Bar code / Board ID code additionally support `Between`,
  `Not Between`, `<=`, `>=`).
- Inspected Object / Repair status / Default: `Equal`, `Different`,
  `In`, `Not In` (no wildcard).
- Repair comment: `Equal`, `Different`, `Like`, `Not Like`, `In`,
  `Not In`.
- Panel status / Board status: `Equal` only (with the fixed enum
  `-2/-1/0/1/2/3`; see `vit-aoi-database` skill).
- AOI: `Equal`, `Different`, `In`, `Not In`, `Like`, `Not Like`.
- `IN` operator on ID codes uses `;` as separator (traceability query).

### Reports & operations
- Home-page = user-picked subset of reports (`user_report`).
- Reports may be **locked** with a password; owner can `Duplicate` a locked
  report to modify a copy.
- Export = Excel (one tab per entity, comment entities skipped).
- Print = server renders a printable page and triggers browser print.
- Mail = SMTP send with Excel attachment; recipients separated by `;`.

### Automatic treatments (batch)
- Frequency: daily / weekly / monthly, with `NEXT_TREATMENT` timestamp.
- Two independent actions: e-mail send and/or file save to
  `batchReportDirectory` (`c:\temp\batchReports` by default; configured in
  `web.xml`).
- Master switch: `Parameters > Batch management` writes into the
  `parameter` table (see `batchIsOn = true` default in
  `ViewebParameters.properties`).
- Batch loop period is `batchRefreshFrequency` minutes (default 1440 =
  24 h), spawned from `InitServlet` at boot.

### Application-level parameters (in `parameter` table + properties)
- **MSA limits**: for each of Deviation X / Y / Theta and each metric
  (`Average`, `Standard deviation`, `6σ`, `Cp`, `GR&R`, `EV`, `%EV`), two
  thresholds (`Acceptable`, `Out`) — defaults per §2.4.1 of user guide.
- **Tolerance intervals**: `ITx`, `ITy`, `ITS` for both paste pads and
  components; `Confidence coefficient` (for EV) and `Tolerance EV`
  (for %EV). `GR_R` default = **4.33**.
- **Header / Footer**: any of `logo`, `title`, `date`, `description`,
  `user`. Defaults: `defaultHeaderLeft=logo`, `defaultHeaderMiddle=title`,
  `defaultHeaderRight=date`, `defaultIsDynamic=false`,
  `defaultIsOneColumn=true`.
- `maxNumberDisplayableFilterValues = 20`.

## i18n resource bundles (`WEB-INF/classes/`)

Canonical labels are in `*_en.properties`; French parity is `*_fr.properties`.
The `*.properties` (no suffix) files are duplicates used as defaults.

| Bundle | Purpose |
|---|---|
| `ApplicationResources_en.properties` | menu, buttons, page titles, general labels |
| `Admin_en.properties` | admin screens (users, DB list, production lines, shifts, MSA limits, batch) |
| `Report_en.properties` | report list & properties |
| `TableEntity_en.properties` | FPY / DPMO tables |
| `GraphEntity_en.properties` | Error / Deviation / Trend charts |
| `MSAEntity_en.properties` | MSA entity |
| `ProcessCapabilityEntity_en.properties` | process-capability tables |
| `TestEmptyMasterEntity_en.properties` | test-empty-master |
| `TracabilityEntity_en.properties` | traceability |
| `CommentEntity_en.properties` | comment entity |
| `enum_en.properties` | enum labels (draw types, scales, deviations…) |
| `error_en.properties` | error messages |

Always look up the English key first, then translate. Any new UI label in
Nieweb must exist in both `_en` and `_fr` bundles.

## Known bugs (from `Vieweb-release-note-V1.6.2.pdf`)

Nieweb **must fix** these (they are open in 1.6.2):

- **#9699** – "Send Reports by email" fails in some cases (SMTP retry
  logic missing).
- **#12421** – Weekly reports totals differ from daily totals (aggregation
  window off-by-one at week boundary).
- **#11211** – Wrong defect displayed (defect look-up joins the wrong
  `Error_Table_AR` bit → mis-labels defect type).
- **#18915** – Cannot export more than 250 columns to Excel (POI 2.5 limit).

Corrected in 1.6.2 (do not reintroduce): #18416 blank screen with 4-user
license; #17966 wrong starting date with "Last n day/month".

## Version endpoint

Legacy exposes `GET /VIEWEB/version` for a plain-text version string.
Nieweb should keep an equivalent endpoint for compatibility with any
existing monitoring probes.

## Where to look for concrete answers

| Question | File under `VIT_Vieweb/` |
|---|---|
| What actions does the "Reports" URL space have? | `app/VIEWEB/WEB-INF/struts-config.xml` |
| What admin actions exist? | `app/VIEWEB/WEB-INF/struts-admin.xml` |
| How is a JSP composed via Tiles? | `WEB-INF/tiles-report.xml`, `tiles-admin.xml` |
| How is the Hibernate SessionFactory built? | `WEB-INF/classes/hibernate.cfg.xml` |
| How does the internal DB look? | `tools/db/create.sql`, `WEB-INF/classes/sqlscript/drop_and_create.sql` |
| Default admin credentials & seed data | `tools/db/init.sql` |
| Startup / Java requirements | `start_server.bat`, `tools/install_http_service.bat` |
| Runtime parameters | `WEB-INF/web.xml`, `WEB-INF/classes/ViewebParameters.properties` |
| Logging config | `WEB-INF/classes/log4j.xml` |
| Historical batch behavior | `logs/vieweb/Vieweb-Batch.log.*` |

## When designing a Nieweb replacement

1. Locate the legacy source of truth (JSP + Struts action + Hibernate bean).
2. Extract the exact SQL / calculation performed (many chart/table entities
   assemble SQL at runtime from the filter DSL — see `vit-aoi-database`
   skill for target tables and `aoi-quality-metrics` for the formulas).
3. Match every label to its English properties key.
4. Preserve the same filter operators and the same enum values so imported
   Vieweb reports (JSON export of the internal DB) can round-trip.
5. Confirm the fix for the known bugs listed above.
