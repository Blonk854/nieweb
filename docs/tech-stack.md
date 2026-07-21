# Nieweb tech-stack proposal

_Author: architecture pairing session, July 2026._
_Status: **SIGNED-OFF 2026-07-20**. All eight open questions resolved (see
§14). Alternatives blocks retained so future revisits have full context._

The goal of this document is to lock in the stack **before** we start
building the first user-facing feature. Every downstream choice (repo
layout, CI, deployment, hiring / onboarding) depends on it.

---

## 1. Scope & non-goals

**In scope**
- Replace the reporting features of legacy **Vieweb 1.6.2** (Java 7 / Struts
  1.2 / Tomcat 7) with a modern web app that reads the same VIT Superviseur
  production DBs.
- Absorb the most valuable features of **Sigmalink 1.6.5** (Analyse
  dashboards, configurable Review UI, PI-capacity guard, 5-language i18n).
- Preserve numeric parity with legacy KPI outputs (FPY, DPMO, Cp/Cpk, GR&R).
- Runtime access to two live Superviseur DBs (HLYAOI post-reflow / MEAOI
  pre-reflow) is **read-only** and guarded (already implemented — see
  `SqlServerAoiSourceBase`).

**Not in scope for Phase 1**
- Re-implementing the Sigmalink CAD editor (JavaFX applet). Deferred to
  Phase 3 at earliest.
- Reviewing / repair workflow (Sigmalink Review). Phase 2.
- Real-time inline feedforward (SigmaLine). Phase 3.
- Writing back to the Superviseur DB. **Never.**

---

## 2. Environment constraints (fixed by the site)

| Constraint | Value | Consequence |
|---|---|---|
| OS on both dev boxes and production | Windows Server / Windows 11 | Prefer cross-platform-but-Windows-first tooling; PowerShell scripts stay ASCII-only. |
| AOI Superviseur DBs | SQL Server 2022 Enterprise | Use `Microsoft.Data.SqlClient`, no schema changes. |
| Corporate identity | on-prem AD / Entra ID hybrid (assumed) | Auth must integrate with AD/Entra without a separate password store. |
| SMT lines | 24/7 | Zero-downtime deploys eventually needed. Health probes + rolling restarts. |
| Legacy user base | line engineers, quality engineers, supervisors | Web UI must be keyboard-friendly, printable, translatable (EN/FR at minimum). |
| Existing archived apps | Vieweb (Tomcat 7), Sigmalink (Jetty 9), Analyse (Jetty 9) | Nieweb takes port `:8080` by default so it can slot in behind the same reverse proxy. |

---

## 3. Backend / server

### 3.1 Language & runtime — **.NET 10 / C# (locked)**

Already committed. All scaffolding, contracts, SQL adapter, and smoke test
are on `net10.0` with `TreatWarningsAsErrors=true` and `Nullable=enable`.

Alternatives considered and rejected:

| Option | Why rejected |
|---|---|
| Java 21 + Spring Boot | Best 1:1 mapping to Vieweb/Sigmalink Java patterns, but no team appetite to stay in the JVM after 20 years of legacy pain; heavier runtime footprint on Windows. |
| Node.js + TypeScript (Nest / Fastify) | Weaker SQL Server ecosystem, poor fit for heavy CPU work (image decoding, statistics). |
| Python + FastAPI | Excellent for one-off analytics; poor fit for a long-running multi-user app with strict typing needs and heavy SQL Server integration. |

### 3.2 Web framework — **ASP.NET Core Minimal API + MVC controllers (recommended)**

- **Minimal API** for the JSON endpoints the frontend consumes (fast,
  low-ceremony, first-class OpenAPI).
- **MVC controllers** kept available for the small number of endpoints that
  need model binding + filter attributes (file uploads, downloads with
  content negotiation).
- **Kestrel** as the HTTP server. **YARP** or IIS as reverse proxy only if
  ops requires it.

### 3.3 Dependency injection & configuration

- Built-in `Microsoft.Extensions.DependencyInjection`.
- Configuration composed from `appsettings.json` + `appsettings.{env}.json`
  + environment variables + a lightweight `.env` loader (no
  `DotNetEnv` package — a 40-line loader like the one already in
  `tools/db-smoke` is enough and avoids the transitive dependency).
- **AOI sources declared in `appsettings.json`** — `id`, `displayName`,
  `envVarPrefix`, `enabled` — and instantiated at startup as
  `IEnumerable<IAoiSource>` behind a keyed DI registration.

### 3.4 Data access

- **Superviseur DBs (read-only, external):** already covered by
  `Nieweb.DataSources.Sql`. No ORM — hand-written SQL through
  `Microsoft.Data.SqlClient` with the read-only guard stack.
- **Nieweb internal DB (own):** **PostgreSQL 16** with **EF Core 10**.
  See §4.

### 3.5 Background jobs — **built-in `IHostedService` + Quartz.NET (recommended)**

- Simple periodic housekeeping (cache warm-ups, freshness pings) as
  `BackgroundService`.
- Cron-scheduled report execution ("automatic treatments" in Vieweb terms) as
  **Quartz.NET** jobs backed by the Nieweb internal DB for persistence and
  clustering. Hangfire was the runner-up but Quartz has better cron
  semantics, doesn't require a separate dashboard, and integrates cleanly
  with our existing DI.

### 3.6 Logging — **Serilog (locked)**

- Structured JSON logs to file (`logs/nieweb-YYYYMMDD.log`), rolling daily,
  30-day retention.
- Console sink in Development.
- Query duration + row count logged for every hit against the Superviseur
  DBs (so we can prove to the SMT team we're not slowing down inspection).

---

## 4. Nieweb internal database

Nieweb needs its own DB for: reports, saved filters, users/roles,
automatic treatments, MSA limits, tolerance intervals, production lines,
shifts, audit trail, saved dashboard layouts.

Recommendation: **PostgreSQL 16** with **EF Core 10** (code-first,
migrations checked into git).

| Option | Verdict |
|---|---|
| **PostgreSQL 16** ✅ | Free, first-class Windows support, great JSONB for flexible filter/layout storage, richer typing than MySQL, no license entanglement. |
| SQL Server 2022 | We already run it on-site — but *for the Superviseur DBs only*. Using it for Nieweb's internal store risks people confusing the two and issuing writes against the wrong instance. Separation of concerns wins here. |
| SQLite | Great for dev / smoke tests. **Recommended as the default in `appsettings.Development.json`** so a fresh clone works with zero infra. Not viable for prod (multi-user writes, backup, clustering). |
| HSQLDB / MySQL | Legacy Sigmalink / Vieweb choices; both retired. |

**Both provider swap** is realistic — EF Core's dual-provider story
(`Npgsql.EntityFrameworkCore.PostgreSQL` in prod, `Microsoft.EntityFrameworkCore.Sqlite` in dev) keeps
migrations sane if we structure entities carefully (no provider-specific
column types).

---

## 5. Frontend

### 5.1 Framework — **React 19 + TypeScript + Vite (recommended)**

- Widest hiring pool, best charting library ecosystem, most mature
  translation tooling.
- **Vite** for the dev server + build. **TanStack Router** for typed
  routes, **TanStack Query** for API state + cache.
- **Zustand** for UI state where it doesn't fit React Query.
- Served as a static bundle from the ASP.NET Core host under `/app`;
  API under `/api`. No SSR (reporting apps don't benefit from it, and it
  simplifies auth).

Alternatives considered:

| Option | Verdict |
|---|---|
| **Blazor United** (Server + WebAssembly hybrid) | Tempting: same C# on both sides, EF Core types shared with the frontend. **Rejected because** the charting story on Blazor is still thin, keyboard shortcuts / accessibility on custom widgets are painful, and hiring a Blazor dev is harder than hiring a React dev. |
| **Vue 3** | Fine framework; no team preference for it and less mature typed router/data-fetching story. |
| **Angular** | Overkill for a reporting app; heavy tooling. |
| **HTMX + server-rendered Razor** | Great for CRUD, terrible for the Analyse-style interactive dashboards we need. |

### 5.2 UI kit — **Mantine v7 (recommended)** or **MUI v6**

Both battle-tested React kits with good data-table + form + modal
coverage. Mantine has a slightly cleaner API and a better date-range
picker (which we'll use a lot); MUI has more third-party plugins. **Pick
before Sprint 1 starts** to avoid churn.

### 5.3 Charts — **Apache ECharts + `echarts-for-react`**

- Battle-tested on manufacturing dashboards. Handles heavy datapoint counts
  (>50k) that Chart.js chokes on.
- Native support for the chart types we need: histogram, Pareto, boxplot,
  heatmap, radar (Cp/Cpk radar in Sigmalink Analyse), scatter with error
  bars, gauge, timeline.
- Alternatives: **Plotly.js** (also good but 3× the bundle size),
  **Highcharts** (commercial), **Chart.js** (too limited for Cp/Cpk radar
  and boxplots).

### 5.4 Internationalization — **`react-i18next`**

- EN + FR mandatory (Vieweb parity).
- DE / ES / ZH nice-to-have (Sigmalink parity — reuse their translated
  strings from `messages*_{en,fr,de,es,zh}.properties` where they apply).
- Backend messages localized via `IStringLocalizer` reading the same JSON
  bundles the frontend uses (single source of truth).

---

## 6. Auth & authorization

**Locked: OpenID Connect against corporate Entra ID / AD FS, with local
username/password as a fallback for machines that live on the line network
without domain access.**

- ASP.NET Core `Microsoft.Identity.Web` package for the OIDC flow.
- Local accounts stored in the internal DB via **ASP.NET Core Identity**
  (Argon2id password hashing — never SHA-1, which is what Sigmalink used).
- Role-based access: `Reader`, `Author`, `Admin` (Vieweb roles are the
  baseline; we'll add `Reviewer` / `Analyzer` / `Programmer` when Phase 2 &
  3 land, matching Sigmalink).
- Cookie auth for the browser session, JWT bearer for API-to-API and
  eventual mobile clients.

**Admin UI — account management.** The `Admin` role gets a full account
management screen in Nieweb itself:

- **Local accounts (full CRUD).** Create user (username, email, initial
  password OR emailed invitation link), reset password, force password
  change on next login, enable / disable, delete, assign & revoke roles,
  view last login + session history.
- **OIDC accounts (Nieweb-side role management only).** Users are
  auto-provisioned as `Reader` on first successful Entra sign-in. The
  admin UI lists all provisioned OIDC users, lets admins assign / revoke
  roles, and lets admins disable an identity within Nieweb (blocks sign-in
  without touching Entra). Password reset, MFA, and account lockout live
  in Entra — Nieweb shows a "Reset your password in Entra ID →" link.
- **Cross-cutting.** Password policy (min length, complexity, optional
  rotation) configurable per install; every account action written to an
  audit log table (`who / what / when / from-IP`); passwords hashed with
  Argon2id and never logged, never emailed in plaintext.

**Explicitly not doing:** carrying over Sigmalink's hard-coded
`admin/admin` default account or its plain-text `parameters.properties`
password file.

---

## 7. Reports, exports, and email

- **CSV** — built-in `System.IO.Pipelines`-based writer; no library needed.
- **Excel** — **ClosedXML** (MIT, good perf on <100k rows). For >100k rows
  we stream directly to `.xlsx` using **OpenXML SDK** (heavier API,
  necessary for big MSA exports).
- **PDF** — **QuestPDF** (fluent C# API, MIT license, prints properly on
  Windows without any GDI+ pitfalls). Legacy Vieweb used iText → we
  avoid iText's dual license.
- **Email** — **MailKit** talking to the site's existing SMTP relay. Fixes
  Vieweb bug #9699 by using MailKit's SMTP client (which is far more
  forgiving of edge cases than Java 7's JavaMail was).
- **Vieweb bug #18915 (250-column export cap)** — solved by design: our
  streaming exporter has no column count limit.

---

## 8. Image pipeline (deferred — sketch coming next)

Nieweb will eventually need to render:

- `.otr` files (VIT AOI region-of-interest snapshots)
- `.ois` files (Sigmalink Review OIS exports)
- Reference-image bank (`Preview_..._H###mm_W###mm_###um.jpg`)
- CAD Editor SVG backgrounds from Sigmalink iCAD project

Provisional choice: **SkiaSharp** for pixel manipulation + PNG output,
served through a caching layer that keys on file hash so line engineers
who reload a Review screen 50 times don't hammer the disk.

This will get its own design doc — see the *sketch .otr/.ois image
pipeline* task in the plan.

---

## 9. Deployment & ops

- **Runtime:** `dotnet publish -c Release -r win-x64 --self-contained false`
  → Kestrel behind IIS on Windows Server. Self-contained deploy also
  supported for line-side devices that lack the shared runtime.
- **Windows service:** register with `sc.exe` using
  `Microsoft.Extensions.Hosting.WindowsServices`.
- **Config:** `appsettings.json` in the install directory + `.env` for
  secrets (git-ignored; managed by ops). Secrets never in git.
- **Static assets** (frontend bundle) served by Kestrel with hashed file
  names and long cache headers.
- **Docker image** built in CI as a secondary artifact so the app can also
  be run on the internal Linux hosts if ops wants.

---

## 10. Observability

- **Serilog** structured logs (see §3.6).
- **OpenTelemetry** traces + metrics — `OTLP` exporter pointed at the
  site's Grafana / Loki / Tempo stack when it exists; local Jaeger for dev.
- **Health endpoints:** `/health/live` and `/health/ready` for the reverse
  proxy. `/health/db` fans out to every registered `IAoiSource` and pings
  `GetLatestPanelUtcAsync` — surfaces the "DB is stale" case we already
  hit with HLYAOI post-reflow.
- **Query auditing:** every SQL statement issued against a Superviseur DB
  is logged with `SourceTag`, elapsed ms, and row count so the SMT team
  can audit what Nieweb is doing to their line.

---

## 11. Testing

- **xUnit** as the test runner (locked).
- **FluentAssertions** for readable asserts.
- **Testcontainers for .NET** for integration tests against ephemeral
  Postgres and SQL Server instances (SQL Server test container is heavy
  but usable; when it's too slow in CI we fall back to a canned
  `.bak` restore).
- **Playwright for .NET** for frontend end-to-end smoke tests.
- **Snapshot tests** for KPI formulas — every KPI (FPY, DPMO, Cp, Cpk,
  GR&R) gets a hand-computed reference fixture drawn from Vieweb's own
  test data so we can prove numeric parity to line engineers.

---

## 12. Repository & workflow

- `main` is protected; changes via short-lived branches + PRs.
- Conventional commits (`feat`, `fix`, `refactor`, `chore`, `docs`, `test`).
  Enforced by a client-side `commit-msg` hook, not a server-side check
  (small team, no need for the overhead).
- Every commit builds green on Windows CI with `dotnet build`,
  `dotnet test`, `npm ci && npm run build`, `npm run lint`.
- `.editorconfig` + `dotnet format` gate on CI.
- `TreatWarningsAsErrors=true` stays on for the whole solution.

---

## 13. Rollout plan

| Phase | Delivers | Length target |
|---|---|---|
| **0 — Foundation** (done) | Read-only SQL adapter, guards, smoke test | ✅ complete |
| **1 — MVP vertical slice** (next) | One report end-to-end: SQL → API → React table + one ECharts chart; auth; two source dropdown; PDF/CSV export; deployed to a real host | ~6 weeks |
| **2 — Vieweb feature parity** | Remaining Vieweb report types (Graph, MSA, Process Capability, Traceability, Test Empty Master); automatic treatments; saved filters; email; production lines & shifts | ~3 months |
| **3 — Sigmalink absorption** | Analyse dashboards (Live / Line Performance / Product / Panel / Cp-Cpk); Review UI (minus CAD editor); PI-capacity guard; DBQuery Pi / K integration; 5-language i18n | ~4 months |
| **4 — Full retirement** | Legacy Vieweb + Sigmalink decommissioned; CAD editor replacement | when Phase 3 is stable |

---

## 14. Resolved decisions (signed off 2026-07-20)

| # | Question | Decision |
|---|---|---|
| 1 | Frontend framework | **React 19 + TypeScript + Vite** with TanStack Router + TanStack Query + Zustand. |
| 2 | UI kit | **Mantine v7**. |
| 3 | Internal DB | **PostgreSQL 16** with EF Core 10 in prod; SQLite in `appsettings.Development.json` for zero-infra dev. |
| 4 | Auth | **OIDC against corporate Entra ID / AD FS**, with local username / password fallback. Full account-management UI for admins (see §6). Ops must provision an Entra app registration before Phase 1 auth work starts. |
| 5 | i18n scope in Phase 1 | **EN + FR** — both scaffolded from day one, EN strings filled first, FR filled iteratively. DE / ES / ZH added in Phase 3 alongside Sigmalink absorption. |
| 6 | Deployment target | **Kestrel as a Windows service** via `Microsoft.Extensions.Hosting.WindowsServices`. IIS reverse-proxy remains available for ops but is not the primary path. |
| 7 | Frontend routing | **Client-side SPA** — single bundle served under `/app`, API under `/api`, no SSR. |
| 8 | Line-engineer design partners | **Recruit 2 now**, before Phase 1 sprint 1. Weekly 30-minute sessions during MVP development. |

Stack is now frozen. Further changes require an explicit revisit note in a
new section, not an in-place edit.
