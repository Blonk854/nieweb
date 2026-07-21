# Nieweb — production deployment guide

This is the operator's runbook for installing Nieweb on a Windows box on
the SMT-line network. It assumes a clean Windows Server 2019+ / Windows
10+ target with network access to the two AOI Superviseur SQL Server
instances and to whichever host runs the Nieweb internal database
(PostgreSQL in production, SQLite for a single-machine pilot).

Nothing in this guide requires internet access on the target machine —
publish artifacts and the .NET runtime installer are copied over from a
build workstation.

---

## 1. Target-machine prerequisites

Install once per target:

- **.NET 10 ASP.NET Core Runtime (x64)** — matching the SDK Nieweb was
  built against. The publish artifact is `--self-contained false`, so
  the framework must be on the box.
  Verify: `dotnet --list-runtimes` should show
  `Microsoft.AspNetCore.App 10.0.x`.
- **PowerShell 5.1+** (ships with Windows) or PowerShell 7. Both
  scripts under `tools/deploy/` target 5.1 syntax.
- **Network reachability** to:
  - The post-reflow AOI SQL Server (`HLYMSSQL2`, database `HLYAOI2024`).
  - The pre-reflow AOI SQL Server (`HLYMSSQL1`, database `MEAOI`).
  - The Nieweb internal PostgreSQL host (if `Nieweb:Db:Provider =
    Npgsql`). Not required for the SQLite pilot.
- **Windows Firewall inbound rule** for the port Nieweb will bind
  (`5000` HTTP by default; front with IIS / a reverse proxy for TLS).
- **Service account** with:
  - Read-only rights on both AOI databases (see the read-only discipline
    section below).
  - Write access to the install folder + log folder.

---

## 2. Build the publish artifact

On the developer workstation, from the repo root:

```powershell
dotnet publish src\Nieweb.Api\Nieweb.Api.csproj `
    -c Release -r win-x64 --self-contained false `
    -o artifacts\publish
```

This produces `artifacts\publish\` with:

- `Nieweb.Api.exe` + all managed DLLs.
- `wwwroot\app\` — the built React SPA (Vite output). The
  `BuildNiewebSpa` MSBuild target runs `npm ci` + `npm run build`
  automatically. Requires Node 24 on the build machine; pass
  `-p:BuildNiewebSpaOnPublish=false` on a Node-less machine and
  copy the pre-built `wwwroot/app/` folder in separately.
- `appsettings.json` (defaults) and `appsettings.Development.json`
  (dev override, safe to leave in place — it only applies when
  `ASPNETCORE_ENVIRONMENT=Development`).
- `web.config` (only used if IIS ends up hosting the process — the
  Windows-service path ignores it).

Copy the whole folder to the target machine, e.g.
`C:\Program Files\Nieweb\`.

---

## 3. Configure

Create `C:\Program Files\Nieweb\appsettings.Production.json` beside the
exe. Values you **must** set:

```jsonc
{
    "Nieweb": {
        "Db": { "Provider": "Npgsql" },
        "Auth": {
            "Jwt": {
                "Issuer": "https://nieweb.example.corp",
                "Audience": "nieweb-api",
                "SigningKey": "REPLACE-with-at-least-32-utf8-bytes-of-random"
            }
        }
    },
    "ConnectionStrings": {
        "NiewebDb": "Host=pgsql.corp;Database=nieweb;Username=nieweb_app;Password=REPLACE"
    }
}
```

- `Nieweb:Db:Provider` — `Sqlite` (single-file, `Data Source=` in the
  connection string) or `Npgsql` (PostgreSQL). Choose one and stick
  with it — changing providers requires re-running migrations.
- `Nieweb:Auth:Jwt:SigningKey` — at least 32 UTF-8 bytes. Generate
  once per environment and store in a secret manager, not in git.
- `ConnectionStrings:NiewebDb` — connection string for the internal
  DB (users, roles, saved views, audit log). Never point this at an
  AOI Superviseur database.

**AOI Superviseur credentials** live in a git-ignored `.env` beside the
exe (same schema as `.env.example` in the repo root):

```dotenv
AOI_POSTREFLOW_SERVER=HLYMSSQL2
AOI_POSTREFLOW_DATABASE=HLYAOI2024
AOI_POSTREFLOW_USER=svc_nieweb_ro
AOI_POSTREFLOW_PASSWORD=***

AOI_PREREFLOW_SERVER=HLYMSSQL1
AOI_PREREFLOW_DATABASE=MEAOI
AOI_PREREFLOW_USER=meaoiprodinq
AOI_PREREFLOW_PASSWORD=***

AOI_CONNECT_TIMEOUT=15
AOI_QUERY_TIMEOUT=30
```

Alternatively pass any of these as environment variables under the
service (`sc.exe config Nieweb env=...` or a wrapper script). Never
paste passwords into chat, into `appsettings.json`, or into any file
that gets committed.

Any AOI source whose credentials are missing is treated as "not
available on this host" — the API starts fine and reports its
availability through `/api/sources`.

---

## 4. Initialize the internal database

From an elevated PowerShell in the publish folder:

**SQLite pilot** — the DB file is created automatically on first
launch. Nothing to do beyond ensuring the service account can write to
the folder that holds the `.db` file.

**PostgreSQL** — create the empty database + role on the PG host, then
apply migrations from a developer machine that has the repo:

```powershell
cd C:\repos\Nieweb
$env:ConnectionStrings__NiewebDb = 'Host=pgsql.corp;Database=nieweb;Username=nieweb_app;Password=***'
dotnet ef database update `
    --project src\Nieweb.Data.Migrations.Npgsql `
    --startup-project src\Nieweb.Api
```

Migrations are idempotent — re-running is safe. Bootstrap the first
admin user via the Identity CLI (or, until that ships, by inserting a
row directly with a bcrypt/Argon2id hash generated locally).

### 4.1. First-boot bootstrap admin

Nieweb.Api will seed a single administrator on first launch **only**
when the users table is empty. The seed is opt-in via configuration:

```jsonc
"Nieweb": {
    "Bootstrap": {
        "Admin": {
            "Email":                 "admin@nieweb.corp",
            "Password":              "REPLACE-with-a-single-use-value",
            "DisplayName":           "Initial administrator",
            "MustRotatePassword":    true
        }
    }
}
```

Behavior:

- If `Email` or `Password` is missing/blank, the seeder does nothing
  and logs a warning — first-run provisioning must then happen via
  another route (`dotnet ef` insert, sidecar CLI, etc.).
- `MustRotatePassword` defaults to `true` in every host. Leave it
  true for production so the seeded credential is discarded at first
  sign-in via `POST /auth/change-password`. Set it to `false`
  **only** for automated harnesses (the Playwright E2E does this) —
  never for a human-facing deployment.
- After the first successful boot, remove `Nieweb:Bootstrap:Admin:*`
  from configuration so a compromised or leaked file cannot re-seed
  the admin.

---

## 5. Install the Windows service

From an elevated PowerShell on the target box:

```powershell
cd C:\repos\Nieweb\tools\deploy  # or wherever the scripts landed
.\install-service.ps1 `
    -BinPath 'C:\Program Files\Nieweb\Nieweb.Api.exe' `
    -Account 'CORP\svc_nieweb'
# Prompts for the account's password as a SecureString.
Start-Service Nieweb
Get-Service Nieweb
```

Defaults:

- **Service name**: `Nieweb` (override with `-ServiceName`).
- **Display name**: *Nieweb - AOI reporting*.
- **Start type**: `delayed-auto` — Windows brings up SQL Server / ETW
  before Nieweb tries to connect.
- **Recovery**: restart after 5 s, then 5 s, then no further action;
  counter resets after 24 h.
- **Account**: `NT AUTHORITY\NetworkService` if omitted. Domain
  accounts and `LocalSystem` are supported; built-in accounts don't
  need a password.

The host detects the SCM launch via
`builder.Host.UseWindowsService()` and routes log records to the
Windows Event Log in addition to the Serilog file/console sinks.

To upgrade:

```powershell
Stop-Service Nieweb
Copy-Item -Recurse -Force artifacts\publish\* 'C:\Program Files\Nieweb\'
Start-Service Nieweb
```

To remove:

```powershell
.\uninstall-service.ps1
```

---

## 6. Verify

From the target box or any host that can reach the service port:

```powershell
Invoke-WebRequest http://localhost:5000/health/live   -UseBasicParsing
Invoke-WebRequest http://localhost:5000/health/ready  -UseBasicParsing
Invoke-WebRequest http://localhost:5000/health/db     -UseBasicParsing
```

All three must return HTTP 200 with a JSON body whose `status` is
`Healthy`. Expected checks:

| Endpoint         | Checks that must be Healthy       |
| ---------------- | --------------------------------- |
| `/health/live`   | `self`                            |
| `/health/ready`  | `self`, `nieweb-db`               |
| `/health/db`     | `nieweb-db`                       |

Then open the SPA in a browser: `http://<host>:5000/` → redirects to
`/app/` and loads the login page. Log in with the bootstrap admin
account.

---

## 7. Logs & diagnostics

- **Serilog file sink**: `logs\nieweb-YYYYMMDD.log` under the working
  directory (i.e. the install folder). Compact JSON format, 100 MB
  cap per file, 31 daily files retained.
- **Console sink**: only visible when running the exe manually. The
  Windows service also emits key lifecycle events to the Windows
  Event Log (source: `Nieweb`).
- **OpenTelemetry**: traces + metrics currently export to the
  console sink only. Wire an OTLP endpoint in a future release when
  a collector is available.
- **Audit trail**: every admin action (user create / edit-role /
  disable) writes an `AuditEvent` row in the Nieweb internal DB.

---

## 8. AOI Superviseur read-only discipline

Every code path that touches either AOI database must (and does):

- Refuse write DDL/DML keywords via `SqlGuards` — inspection is
  regex-based, applied before the query hits ADO.NET.
- Prefix every batch with `SET TRANSACTION ISOLATION LEVEL READ
  UNCOMMITTED; SET NOCOUNT ON;` and use `WITH (NOLOCK)` on every
  production-shaped table.
- Set `ApplicationName='Nieweb-<sourceTag>-<database>'` on the
  connection so DBAs can identify sessions.
- Time-window every query on `PANELS`, `CARDS`, `TESTED_OBJECT`,
  `PIN`, `PIN_MEASURE`, and `*_HISTO`. Never a bare `SELECT *`.
- Cap the SQL command timeout at 30 s (default).

**Never write** to either DB. Never mix them in a single query. The
`.env` credentials for the post-reflow account currently have write
permissions because a read-only login is not yet provisioned — the
code guards prevent accidental writes regardless.

---

## 9. Troubleshooting

| Symptom                                                                   | Likely cause + fix                                                                                     |
| ------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `install-service.ps1: must run from an elevated PowerShell session`       | Open PowerShell "Run as administrator" and re-run.                                                     |
| Service starts then stops immediately (Event Log: `Application error`).   | Check `logs\nieweb-YYYYMMDD.log` — usually a missing `SigningKey` or unreachable `NiewebDb`.           |
| `/health/db` returns 503 `Unhealthy`.                                     | Internal DB unreachable. Verify `ConnectionStrings:NiewebDb` and network / firewall to the PG / SQLite file. |
| `/api/sources` returns fewer than two entries.                            | AOI credentials missing / wrong for that source. Check `.env` and re-run — the API restart is not required, but the source list is cached at startup. |
| SPA loads but every API call returns 401.                                 | JWT signing-key mismatch between environment (`appsettings.Production.json`) and the browser session. Restart the service, re-log-in. |
| Report request takes minutes on the SMT-line box.                         | Widen the time window or add filters — every AOI query is bounded, but a large window with no product filter can still scan a lot of rows. |

---

## 10. Running the end-to-end smoke locally

The Playwright harness under `src/Nieweb.Web/e2e/` boots Nieweb.Api
with a fresh SQLite file (`nieweb-e2e.db` in the Nieweb.Api folder),
seeds a bootstrap admin with `MustRotatePassword=false`, and enables
the in-memory `FakeAoiSource` (`Nieweb:Aoi:Fake:Enabled=true`) so no
real AOI connection is required.

```powershell
cd src\Nieweb.Web
npm ci  # first run only
npx playwright install chromium  # first run only
npm run test:e2e
```
