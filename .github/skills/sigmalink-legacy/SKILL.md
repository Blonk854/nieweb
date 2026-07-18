---
name: sigmalink-legacy
description: |
  Deep expertise on the legacy VIT Sigmalink 1.6.5 (a.k.a. σLink / Sigma / Deep Blue)
  reporting + CAD + review web application shipped by ViTechnology (VIT).
  Use this skill whenever a question, task, or design decision touches Sigmalink,
  sigmalink, Sigma Link, σLink, Deep Blue, deepblue-*, Sigma Data Import,
  Sigma Review, Sigma Analysis, Sigma Line, Sigma Connect, SigmaLink server,
  SigmaLink inline, inline review, offline review, Sigmalink Analyse,
  DBQuery Pi, DBQuery K, Sigmalink CAD editor, iCAD project, PAD file,
  VIS file, .project file, JPSys, autopanelization, autonaming, pad2component,
  fiducial edit, glue deposit import, program variant, SigmaLine SPI-to-AOI
  feedforward, PI-Capacity, Zebra bar code printer, sigmalink.licence,
  parameters.properties, review_lines.xml, review_actions.xml,
  review_defects.xml, review_comments.xml, review_layout.xml,
  review_policy.xml, review_inline.xml, review_offline.xml, review_repair.xml,
  review_custom_messages.xml, review_plugins.xml, ROLE_PROGRAMMER,
  ROLE_REVIEWER, ROLE_ANALYZER, ROLE_ADMINISTRATOR, ROLE_CONFIGURER,
  ROLE_MONITORER, functional_log, smart_purge, hsqldb sigmalink.script,
  fr.vit.deepblue, Sirona monitoring, Vision3D/Vision20/Vision64 integration,
  PCB Image Recorder, image_storage_path, publish_folder_path, wro4j,
  jsweet (Java→TypeScript), qpid/amqp Sigma Connect broker, JavaFX applet,
  bi-prod review, dual-lane inline, or when reimplementing any Sigmalink
  feature in Nieweb. Do not confuse with the older Vieweb 1.6.2 skill
  `vieweb-legacy` — Sigmalink is a completely separate, newer webapp with
  a different stack and different modules.
---

# Sigmalink 1.6.5 (VIT σLink / Deep Blue) — Legacy Expert Skill

Authoritative reference for the Sigmalink 1.6.5 legacy code base under
`VIT_Sigmalink/`, its four sibling PDFs (Sigmalink user guide, Sigmalink
release/install note, Sigma Analysis user guide, Sigma Analysis release note),
and the two companion apps (`VIT_Analyse/analyse.war`, DBQuery-PI-updater).

Sigmalink is **not** the same product as Vieweb. It's the second-generation
VIT tool: a client/server SPI+AOI programming, review and analysis suite
positioned between the CAD design step and the shop-floor inspection.

## 1. Product family (three installers, one code base)

| Installer               | Purpose                                                     | Default path              |
|-------------------------|-------------------------------------------------------------|---------------------------|
| **SigmaLink server**    | Data import, offline review, repair, remote review, config  | `C:\VIT_SigmaLink`        |
| **Inline station**      | One per SMT line, drives conveyor via IO board              | `C:\VIT_SigmaLink_Inline` |
| **Analysis server**     | DBQuery Pi/K live/line/product/panel/Cp-Cpk dashboards      | `C:\VIT_Analyse`          |

The server WAR is `app/sigmalink` (deployed by embedded Jetty 9.4.12 with a
bundled Apache Tomcat 7.0.55 fallback used by the Windows service scripts
`install_http_service.bat` / `install_http_scheduled_task.bat`). Analysis
runs a separate WAR `app/analyse/analyse.war` on **port 8082** with URL
`http://<host>:8082/analyse` (health-check: `/analyse/version`).

Sigmalink server defaults to **port 8080**, URL `http://<host>:8080/sigmalink`
(or `/SigmaLink`). Shutdown port defaults to **8005**. Both can be overridden
in `conf/parameters.properties` (`server.port`, `server.shutdown.port`) if it
must coexist on the same box as legacy Vieweb (which uses 8081).

## 2. Tech stack (very different from Vieweb)

- **Java 7 minimum** for client (JavaFX applet for the CAD editor);
  **Java 8** for server. 32-bit and 64-bit installers.
- **Spring 5.0.4** (`spring-context/mvc/security/orm/jdbc/webmvc`) —
  DispatcherServlet mapped at `/`. Config:
  - `WEB-INF/web.xml`
  - `WEB-INF/webapp-context.xml` (MVC, view resolver, multipart)
  - `WEB-INF/classes/META-INF/spring/application-context-webapp.xml`
  - `WEB-INF/classes/META-INF/spring/persistence.xml`
  - `WEB-INF/classes/META-INF/spring/security-context.xml`
- **Spring Security 5.0.3** (form login, roles listed below).
- **Hibernate 5.3.0.CR1** with HBM XML mappings (see §5). Default DB is
  **HSQLDB** file-based at `D:/Sigmalink/data/VIT_Sigmalink/data/db/sigmalink`
  (`org.hsqldb.jdbc.JDBCDriver`, dialect `org.hibernate.dialect.HSQLDialect`,
  user `sa`, empty password). PostgreSQL is a supported alternative and its
  connection is commented out in `web.xml` for reference:
  `jdbc:postgresql://localhost:5432/DEV_DEEP_BLUE_V0.1`, user `deepblue_user`,
  password `dpbl_user`, dialect `PostgreSQLDialect`.
- **Jetty 9.4.12** as embedded server (`start_server.bat` sets
  `MAX_HEAP_SIZE` / `MAX_HEAP_SIZE_32`).
- **wro4j 1.8** (`fr.vit.deepblue.wro.DeepblueWroFilter` on `/static/*`) for
  JS/CSS bundling; config `WEB-INF/wro.xml`.
- **jsweet-core 6.0.2** — the CAD editor front-end is compiled from Java to
  TypeScript/JS with `jsweet-candy-{awt,common,j4ts-file}` runtime.
- **JavaFX 8** — the standalone CAD editor and offline review client
  (`deepblue-icadmodule-cadeditorfx`, `avision_connector_offline.exe`).
- **AMQP messaging** — Sigma Connect uses `qpid-broker-core 7.1.4`,
  `qpid-jms-client 0.45`, `vertx-amqp-bridge 3.8`, `proton-j 0.33`
  (deepblue-connect-{broker-management, client, common, domain, embedded-server}).
- **Log4j2 2.11** (`log4j2.xml`), functional log stored in DB (see §5).
- Windows integration: `jPowerShell`, `jProcesses`, `WMI4Java`.
- **Report/chart output** — server-side charts as PNG/SVG/PDF/JPEG via
  jFreeChart family; front-end charts as SVG in the browser.

## 3. VIT proprietary module map (`fr.vit.deepblue.*`, all `1.6.5`)

All shipped as `deepblue-*-1.6.5.jar` in `WEB-INF/lib`:

| JAR family              | Contents                                                              |
|-------------------------|-----------------------------------------------------------------------|
| `deepblue-common-*`     | charts, configuration, domain, io, ipccamx, k-domain, parser,        |
|                         | pi-domain, svg, webapp                                                |
| `deepblue-icadmodule-*` | cadeditorfx, csv, domain, extension-pad, extension-visgerber, icad,   |
|                         | imagelab, integr-algo, json, pad, services, vis, webapp               |
| `deepblue-reviewmodule-*` | avision, common-webapp, configuration, dataprovider, domain,        |
|                         | octopus, offline, plugin-exportois, repair, services                  |
| `deepblue-connect-*`    | broker-management, client, common, domain, embedded-server (AMQP)     |
| `deepblue-jsweet-*`     | candy-awt, candy-common, candy-j4ts-file                              |
| `dbquery-pi-client-ipccamx` | Analyse ↔ SPI Pi bridge (IPC-CAMX)                                |

Package roots on the server:
```
fr.vit.deepblue.configuration.piconfiguration
fr.vit.deepblue.domain[.review[.codec]]
fr.vit.deepblue.mvc.controller[.configure|.image|.review]
fr.vit.deepblue.mvc.model[.admin|.analyze|.configure|.icad|.review]
fr.vit.deepblue.security
fr.vit.deepblue.service[.dump|.inlineproxy]
fr.vit.deepblue.web  (SystemPropertiesHelper, CleanupListener, LicenceFilter,
                     ParameterFilter, HTTPSessionFilter)
fr.vit.deepblue.wro  (DeepblueWroFilter)
```

Servlet filters chain (in order, from `web.xml`):
`HTTPSessionFilter` → `CharacterEncodingFilter` (only `/configure/review/*`) →
`WebResourceOptimizer` (only `/static/*`) → `ParameterFilter` →
`springSecurityFilterChain` → `licenceFilterChain`.

Error mapping: `418`→`/418`, `402`→`/402` (out of licence tokens),
`404` and `500`→`/redirectIndex`.

## 4. User modules & roles

Sigmalink is **modular by licence**. The set of visible modules depends on
license tokens; the set of enabled actions inside a module depends on the
user's Spring Security roles.

| Role                    | Grants access to                                        |
|-------------------------|---------------------------------------------------------|
| `ROLE_USER`             | Baseline login                                          |
| `ROLE_PROGRAMMER`       | **Data import** module (CAD editor)                     |
| `ROLE_REVIEWER`         | **Review** module (inline / offline / remote / repair)  |
| `ROLE_ANALYZER`         | **Analysis** Live + Cp/Cpk                              |
| Advanced Analyzer *     | Analysis Line Performance / Product / Panel             |
| `ROLE_CONFIGURER`       | **Configure** module (lines, parameters, review conf)   |
| `ROLE_ADMINISTRATOR`    | User & license administration                           |
| `ROLE_MONITORER`        | Monitor (Sirona-style metrics, disabled by default)     |

* "Advanced Analyzer" is a functional role gated by license, not a distinct
  DB `authorities.authority` value; the fine-grained analysis widgets appear
  automatically when the Advanced Analysis license token is present.

Default user (from `WEB-INF/classes/sql/default_data.sql`):
```
admin / admin (SHA-1 stored hash: d033e22ae348aeb5660fc2140aec35850c4da997)
Roles: USER, PROGRAMMER, REVIEWER, ANALYZER, ADMINISTRATOR, CONFIGURER, MONITORER
```
Rename or disable this account before any production deployment — Sigmalink
guide explicitly forbids working with the shared admin account.

Module URL prefixes (all under DispatcherServlet `/`):
`/admin/**`, `/analyze/**`, `/icad/**`, `/review/**`, `/configure/**`,
`/monitor/**`, `/tools/**`, `/centralRepo/**`, `/dump/**`, `/about/**`,
`/image/**`, `/v20/**`, `/public/**`, `/feedforward/**`, `/services/**`,
`/startprocess/**`, `/data/**`, `/home/**`, `/i18n/**`.

JSPs live under `WEB-INF/views/{admin,analyze,cad,configure,data,home,i18n,
monitor,review,services,startprocess,tools}/`. Notable subtrees:
- `cad/modal/` — ~25 dialog fragments for the CAD editor (fiducial editor,
  pad2component, propagation, subpanel-pattern delete confirmation, variants,
  autonaming, autopanelization result, JPSys import, VIS+Gerber merge, etc.).
- `configure/factory/` — line/islet/AOI/SPI/AXI/reflow/review equipment editors.
- `configure/general/` — general/dbquery-K/dbquery-Pi/feedforward/connect.
- `configure/parameters/configure-parameter.jsp` — read-only param display.
- `configure/review/` — subfolders `modal/`, `actions/` for the review
  configuration GUI (single JSP that replaces manual XML editing of the
  `review_*.xml` files in earlier versions).
- `review/offline/`, `review/remote/` — 8 startup modals: standard, FIFO,
  Lots, Panels, reloadConf, plus offline client `redirect.jsp`.

## 5. Internal database (Sigmalink DB, HSQLDB or PostgreSQL)

The Sigmalink internal DB is deliberately tiny — most operational data lives
in the read-only VIT Superviseur DB (see `vit-aoi-database` skill) or in files
under `c:/VIT_Sigmalink/data/`. Only these Hibernate entities exist:

| HBM file                     | Table            | Columns                                                                     |
|------------------------------|------------------|------------------------------------------------------------------------------|
| `UserEntity.hbm.xml`         | `users`          | `username` (PK), `password` (SHA-1), `enabled`, `firstname` def `'John'`, `lastname` def `'Doe'` |
| (default_data)               | `authorities`    | `username`, `authority` (composite PK, FK → users)                           |
| `Customer.hbm.xml`           | `customer`       | Customer master data                                                        |
| `Product.hbm.xml`            | `product`        | Product master data                                                         |
| `ParameterEntity.hbm.xml`    | `parameter`      | `key` (PK), `value` — mirrors `parameters.properties` at runtime            |
| `FunctionalLogEntity.hbm.xml`| `functional_log` | `id` (auto), `author`, `date`, `module`, `submodule`, `functional_level_id`, |
|                              |                  | `message`, `message_en`, `args_quantity`, `arg1..arg15`                     |
| (default_data)               | `ref_functional_level` | `0=INFORMATIONAL, 1=DEBUG, 2=ERROR, 3=KNOWLEDGE, 4=WARN`              |
| `SmartPurgeEntity.hbm.xml`   | `smart_purge`    | `id` (PK), `last_execution` — bookkeeping for background purge jobs         |

Seeded `parameter` rows (from `default_data.sql`):
```
DUMP_STORAGE                     = c:/VIT_Sigmalink/data/dumps/
TECHNICAL_LOG_STORAGE            = c:/VIT_Sigmalink/logs/
DEFAULT_PAGE_SIZE_IN_PAGINATION  = 10
MAX_FUNCTIONAL_LOG_FOR_DUMP      = 100
DEFAULT_SUGGESTION_SIZE          = 60
MAX_OPEN_ICAD_PROJECT_FILTER     = 200
ICAD_PERSIST_FOLDER              = c:/VIT_Sigmalink/data/icad/
```

## 6. Configuration files on disk (edited outside the DB)

Everything else — lines, review setup, defects, actions, comments, plugins,
policy, sigma-connect, feedforward, PI capacity, glue deposits, skip
templates — lives as XML on disk. This is the biggest structural difference
vs Vieweb: **most Sigmalink "config" is file-backed, not DB-backed**.

| File                                          | Purpose                                             |
|-----------------------------------------------|-----------------------------------------------------|
| `conf/parameters.properties`                  | Startup parameters (server.port, image_storage_path, publish_folder_path, log level, MAX_HEAP_SIZE) |
| `conf/sigmalink.licence`                      | Node-locked licence (MAC-address based)             |
| `conf/global/sigmalink_configuration.xml`     | Sigma Connect broker + endpoints, Analyse host, DBQuery K/Pi defect orders, feedforward, panel-side mapping |
| `conf/review/review.xml`                      | Global review params (session timeouts, lighting)   |
| `conf/review/review_lines.xml`                | Datasources, supervisor profiles, lines, equipment  |
| `conf/review/review_actions.xml`              | Custom review buttons + codes + shortcuts           |
| `conf/review/review_comments.xml`             | Predefined comments per category                    |
| `conf/review/review_custom_messages.xml`      | Contextual defect messages (triggered by criteria)  |
| `conf/review/review_defects.xml`              | Defect label / icon / visibility / warning message  |
| `conf/review/review_layout.xml`               | Widget layout for Inline/Offline/Remote/Repair      |
| `conf/review/review_offline.xml`, `_inline.xml`, `_repair.xml` | Per-mode option overrides          |
| `conf/review/review_plugins.xml`              | Plugin registry (e.g. OIS export)                   |
| `conf/review/review_policy.xml`               | Per-role action enable flags                        |
| `conf/plugins/OISPlugin.xml`                  | OIS export path templates per review status         |
| `conf/data_import/glue_deposits.xml`          | Default glue-deposit family diameters (µm)          |
| `conf/data_import/skips.xml`                  | Skip mark templates                                 |
| `conf/statistics/analyse_layout.xml`          | Analyse **Live** module widget grid                 |
| `WEB-INF/classes/PI-Capacity/pi-conf.xml`     | Physical inspection window per PI model             |
| `WEB-INF/classes/graphs/home-fpy-gauge.json`  | Home FPY gauge definition                           |
| `WEB-INF/classes/themes/theme-{light,dark}.properties` | UI themes                                  |

### PI capacity table (`pi-conf.xml`, all mm)

| Model         | xMin | xMax   | yMin | yMax   |
|---------------|------|--------|------|--------|
| PICO_S        | 25   | 355.6  | 25   | 534    |
| PICO_M        | 25   | 533.4  | 25   | 534    |
| PRIMO_S       | 25   | 350.52 | 25   | 534    |
| PRIMO_M       | 25   | 533.4  | 25   | 534    |
| PRIMO_L       | 25   | 609.6  | 25   | 534    |
| PRIMO_L_WIDE  | 25   | 609.6  | 25   | 558.8  |
| PRIMO_XL      | 25   | 762    | 25   | 534    |

## 7. External DB access (Superviseur / Vision3D CR4 / Vision20 CR5)

Sigmalink reads (never writes) the same VIT Superviseur DB documented in the
`vit-aoi-database` skill. Connection is per-line via `datasources` /
`supervisor_profile` entries in `review_lines.xml`. Supported drivers:

- **SQL Server** — `mssql-jdbc 6.2.2` (`type="SQLSERVER"`, port 1433,
  optional `instance`). This is the modern default.
- **Oracle** — `ojdbc8 12.2` (`type="ORACLE"`, port 1521).
- **Access** — via ODBC (`type="ACCESS"` + `MDB path` UNC).
- **PostgreSQL 42.1.4** — only for Sigmalink's own internal DB.
- **HSQLDB 2.4** — internal DB default.

Each `<supervisor_profile>` carries `platform=32bits|64bits` (used to launch
the correct `avision_connector_offline.exe`). Each `<equipment>` carries
`type=SPI|PRE_REFLOW|POST_REFLOW`, `brand=VIT`, `model=K|PI`, and
per-equipment parameters such as `ois_folder_path`, `tst_folder_path`,
`prog_matcher` regex, `prog_value` regex substitution, `is_dual_lane`,
`lane_type`, `sigmaConnectProfileId`, `useBarcodePrinter`.

## 8. Users, workflow and data flow (canonical VIT diagram)

```
CAD (Gerber / CSV / JPSys)
        │
        ▼
Data Import (ROLE_PROGRAMMER)  ──► exports  .vis (Kseries CI), .pad (PI SPI),
        │                                   .project (Sigmalink round-trip),
        │                                   reference images (OIS bank)
        ▼
Publish (data/publish, network share)
        │
        ▼
Inspection machines (SPI Pi + AOI Kseries) ──► results into Superviseur DB
        │                                              │
        │                                              ├─► Sigma Connect (AMQP)
        │                                              │
        ▼                                              ▼
Inline Review (per line)                       Sigma Analysis (port 8082)
Offline / Remote / Repair Review               Live · Line Perf · Product · Panel · Cp/Cpk
(ROLE_REVIEWER)                                (ROLE_ANALYZER + Advanced)
        │
        ▼
Sanction → conveyor IO board / Zebra bar-code printer / OIS re-export
```

Key concepts to preserve in Nieweb:

- **Panel → Subpanel pattern → Subpanel layout → Fiducials / Id codes /
  Skips / Zones / Components / Pads / Glue deposits** — the core CAD model.
- **Program variant** — a variadic or optional set of components on the
  same physical program (`review_lines.xml` and `variants.xml` per project).
- **Feedforward (SigmaLine S500 licence)** — SPI defects injected into AOI
  inspection coverage via the K module on the AVision box. Rules configured
  via `feedforward` node in `sigmalink_configuration.xml`.
- **Bi-prod review** — automatically chains review of side 1 then side 2 of
  the same panel; only single-lane.
- **FIFO offline review** — server picks the next panel in inspector sort
  order automatically; VIT recommends the Standard mode instead.
- **Repair review** — subpanels can be reviewed independently, unlike
  standard offline where the whole panel is closed at once.

## 9. Session, security, printing

- Web session default 60 min (`<session-timeout>` in `web.xml`), overridden
  per-mode by `HTTPSessionFilter` and `review.xml`
  (`REVIEW_SESSION_TIMEOUT_INLINE=720`, `_OFFLINE=30`).
- Licence tokens are **per module**, not per user. On overflow the browser
  gets HTTP 402 (`/402`).
- Zebra bar-code printing (offline & inline review) uses codes 39/93/128/LOGMARS;
  configured in `review_lines.xml` per line (`printer_type`, `printer_language`,
  X/Y position, angle, bar width/ratio, bar height, human-readable flag).

## 10. Known behaviours to reproduce / fix in Nieweb

- **JSweet applet ↔ browser CAD editor** loads via JNLP under
  `/jnlp/*.{jnlp,jar,jfx}` — modern browsers no longer support this;
  Nieweb must ship a pure-web CAD editor.
- **Confidential embedded broker credentials** — `sigmalink_configuration.xml`
  ships `admin/admin` for the Sigma Connect AMQP broker (`vhost=SigmaConnect`,
  `mechanism=CRAM-MD5`). Rotate these before use.
- **Legacy weak password storage** — `users.password` is raw SHA-1 (unsalted).
  Nieweb must migrate to a modern hash (bcrypt / argon2).
- **`ROLE_MONITORER` + Sirona** — commented out in `web.xml`; the Monitor
  module screen exists (`views/monitor/monitor.jsp`) but is not wired.
- **Analyse module needs external DBQuery Pi & K servers** (per-Pi / per-K
  machine). Health-check URLs: `/analyse/version`, `/dbquery/version`.
- Analyse's "Live" widget grid (`analyse_layout.xml`) supports only two
  layouts (`3H_3H`, `2H_2H`) and a fixed set of widget names — see the
  `sigmalink-analyse` skill for the enumeration.
- **Session dumps** live at `c:/VIT_Sigmalink/data/dumps/`, capped by
  `MAX_FUNCTIONAL_LOG_FOR_DUMP` (100 by default, 2000 in `web.xml`
  `max_func_log_for_dump`).
- **`fr.vit.deepblue.web.LicenceFilter`** short-circuits every request when
  the licence is missing or expired — mirror this hook cleanly in Nieweb so
  users see a helpful page instead of a stack trace.

## 11. i18n

Five languages: `en`, `fr`, `de`, `es`, `zh` (Spanish & Chinese are marked
"incomplete" in the user guide). Four bundles under `WEB-INF/classes/i18n/`:

- `messages_<lang>.properties` — shell, home, modules, dialogs, CAD editor.
- `messages-analyze_<lang>.properties` — Analyse dashboards.
- `messages-configure_<lang>.properties` — Configure module.
- `messages-review_<lang>.properties` — Review module + widgets.

Locale change interceptor listens on the `lang` query parameter
(`LocaleChangeInterceptor` in `webapp-context.xml`). Some sample keys
(canonical English text is authoritative):

```
customer=Customer                       product=Product
side=Side                               module_icad=Data import
module_review=Review                    module_review.offline=Offline review
module_review.remote=Remote review      module_review.repair=Repair
module_analyze=Analyse (Product analysis)
module_conf=Configure                   module_admin=Admin
module_tools=Tools                      module_databrowsing=Data center
home.feedforward=Feed-Forward           home.icon.AOI-Pre=PRE REFLOW
home.icon.AOI-Post=POST REFLOW          title.welcome=Welcome to σLink !
tab.panel=Panel   tab.image=Image   tab.pattern=Subpanel pattern
tab.layout=Subpanel layout   tab.import=Data import   tab.browse=Browse object
icad.locked.title=Project locked
```

## 12. What Nieweb can lift from Sigmalink

The user has explicitly asked which Sigmalink features to bring into Nieweb.
Candidate scope for the modernization plan:

1. **Modern per-role modular UI** (Configure / Import / Review / Analyse /
   Admin / Monitor / Tools) — replaces Vieweb's flat menu.
2. **CAD-first data model** (Panel → Subpanel pattern → Layout → Pads /
   Components / Fiducials / Skips / Zones / Glue deposits) so the same
   project drives both AOI (VIS) and SPI (PAD) programming — see the
   `sigmalink-cad-import` skill.
3. **Configurable review** (widgets, actions, defects, comments, custom
   messages, layout, per-role policy) — currently XML-file-backed, would
   fit naturally into a Nieweb Postgres schema. See `sigmalink-review`.
4. **Analyse dashboards** (Live, Line Performance, Product, Panel, Cp/Cpk)
   with the KPI definitions already documented in `aoi-quality-metrics`.
   See `sigmalink-analyse`.
5. **Feedforward (SPI → AOI defect injection)** — S500-licensed. Requires
   the SigmaLine K module and Sigma Connect AMQP wiring.
6. **Bi-prod, dual-lane, FIFO** review modes.
7. **PI capacity check** at import time (`pi-conf.xml`) so operators cannot
   publish a panel that will not fit the physical inspection window.
8. **5-language i18n** (en/fr/de/es/zh) — carry over the German/Spanish/
   Chinese bundles for free vs Vieweb's en/fr only.

## 13. Ground rules for this skill

- **`VIT_Sigmalink/` is read-only reference** — never edit files under it.
- Sigmalink and Vieweb both read the same Superviseur DB — the
  `vit-aoi-database` skill applies unchanged. Do **not** issue writes.
- KPI formulas (FPY, DPMO, Cp, Cpk, MSA, GR&R) remain governed by the
  `aoi-quality-metrics` skill; Sigmalink defines the same metrics and adds
  new grouping dimensions (per-line, per-JEDEC top-N) but never redefines
  the math.
- The Analyse module has its own release note (`Analyse-release-note-V1.6.5.pdf`)
  and user guide (`Analyse-user-guide-V1.6.5.pdf`) — see `sigmalink-analyse`.
- When in doubt about a Sigmalink feature, consult (in this order):
  1. `pdf_text/Sigmalink-user-guide-V1.6.5.txt`
  2. `pdf_text/Sigmalink-release-note-V1.6.5.txt`
  3. the JSP under `VIT_Sigmalink/VIT_Sigmalink/app/sigmalink/WEB-INF/views/`
  4. the matching XML in `VIT_Sigmalink/VIT_Sigmalink/conf/`
