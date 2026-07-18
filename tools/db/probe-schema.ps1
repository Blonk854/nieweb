<#
.SYNOPSIS
    Read-only probe of the archived VIT AOI database.

.DESCRIPTION
    Loads credentials from ../../.env (NEVER hard-coded here), connects with
    READ UNCOMMITTED isolation, and runs a short battery of INFORMATION_SCHEMA
    queries to verify that the running database matches the schema documented
    in the `vit-aoi-database` skill.

    Any statement string containing INSERT / UPDATE / DELETE / DROP / ALTER /
    TRUNCATE / MERGE / EXEC / GRANT / REVOKE / CREATE (case-insensitive) will
    refuse to execute -- this is a defensive guard, not a substitute for a
    read-only DB account.

.PARAMETER Prefix
    Environment-variable prefix identifying which AOI credential set to use.
    Defaults to `AOI_POSTREFLOW_` (Phase 1 HLYAOI). Use `AOI_PREREFLOW_` for
    the Phase 2 pre-reflow Mycronic database.

.PARAMETER OutputDir
    Directory to write probe result CSVs into. Defaults to
    `<repo>/tools/db/out/<source>/` where <source> is derived from `-Prefix`
    (postreflow / prereflow).

.EXAMPLE
    PS> .\tools\db\probe-schema.ps1                           # post-reflow
    PS> .\tools\db\probe-schema.ps1 -Prefix AOI_PREREFLOW_    # pre-reflow
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Z][A-Z0-9_]*_$')]
    [string]$Prefix = 'AOI_POSTREFLOW_',
    [string]$OutputDir
)

# Derive a short source tag ("postreflow", "prereflow", ...) from the prefix.
$sourceTag = ($Prefix -replace '^AOI_', '' -replace '_$', '').ToLower()
if (-not $OutputDir) { $OutputDir = Join-Path $PSScriptRoot (Join-Path 'out' $sourceTag) }

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --- 1. Locate and load .env -------------------------------------------------
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$envPath  = Join-Path $repoRoot '.env'

if (-not (Test-Path -LiteralPath $envPath)) {
    throw "Missing $envPath - copy .env.example to .env and fill in ${Prefix}USER / ${Prefix}PASSWORD."
}

$envVars = @{}
Get-Content -LiteralPath $envPath | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
        $envVars[$Matches[1]] = $Matches[2].Trim('"').Trim("'")
    }
}

foreach ($suffix in 'SERVER','DATABASE','USER','PASSWORD') {
    $key = "${Prefix}${suffix}"
    if (-not $envVars.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($envVars[$key])) {
        throw "Missing or empty $key in $envPath"
    }
}

$server   = $envVars["${Prefix}SERVER"]
$database = $envVars["${Prefix}DATABASE"]
$user     = $envVars["${Prefix}USER"]
$password = $envVars["${Prefix}PASSWORD"]
$connTO   = if ($envVars.ContainsKey('AOI_CONNECT_TIMEOUT')) { [int]$envVars['AOI_CONNECT_TIMEOUT'] } else { 15 }
$queryTO  = if ($envVars.ContainsKey('AOI_QUERY_TIMEOUT'))   { [int]$envVars['AOI_QUERY_TIMEOUT']   } else { 60 }

# --- 2. Read-only guard ------------------------------------------------------
$forbidden = '\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE|EXEC|EXECUTE|GRANT|REVOKE|CREATE)\b'

# Build a connection string with Application Name so DBAs can identify us.
# TrustServerCertificate=True because older archived servers often use self-signed certs.
$connString = "Server=$server;Database=$database;User Id=$user;Password=$password;" +
              "Application Name=Nieweb-probe-schema-$sourceTag;Connect Timeout=$connTO;" +
              "TrustServerCertificate=True;Encrypt=False;"

Add-Type -AssemblyName 'System.Data'

function Invoke-ReadOnlyQuery {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Sql
    )
    if ($Sql -match $forbidden) {
        throw "Refusing to run '$Name' - statement contains a forbidden keyword. This script is read-only."
    }
    $prelude = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;`nSET NOCOUNT ON;`n"

    $conn = New-Object System.Data.SqlClient.SqlConnection $connString
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText    = $prelude + $Sql
        $cmd.CommandTimeout = $queryTO
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
        $dt = New-Object System.Data.DataTable
        [void]$adapter.Fill($dt)
        # Convert DataRows to PSCustomObjects so Export-Csv/Format-Table work naturally.
        $columns = $dt.Columns | ForEach-Object { $_.ColumnName }
        $out = foreach ($row in $dt.Rows) {
            $obj = [ordered]@{}
            foreach ($c in $columns) { $obj[$c] = $row[$c] }
            [pscustomobject]$obj
        }
        return ,$out
    }
    finally {
        $conn.Close()
        $conn.Dispose()
    }
}

# --- 3. (SqlServer module optional; not required with SqlClient) -------------

# --- 4. Ensure output dir ----------------------------------------------------
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host "Probing $server/$database as $user (source=$sourceTag, read-only)..." -ForegroundColor Cyan

# --- 5. Probes ---------------------------------------------------------------
$probes = [ordered]@{
    '00_server_info' = @"
SELECT
  @@SERVERNAME       AS server_name,
  DB_NAME()          AS database_name,
  SUSER_SNAME()      AS login_name,
  @@VERSION          AS sql_version;
"@

    '01_all_tables' = @"
SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE
FROM INFORMATION_SCHEMA.TABLES
ORDER BY TABLE_SCHEMA, TABLE_NAME;
"@

    '02_expected_core_tables' = @"
;WITH expected(name) AS (
  SELECT 'PANELS'         UNION ALL SELECT 'CARDS'
  UNION ALL SELECT 'TESTED_OBJECT' UNION ALL SELECT 'PIN'
  UNION ALL SELECT 'PIN_MEASURE'   UNION ALL SELECT 'MACHINE'
  UNION ALL SELECT 'PRODUCT'       UNION ALL SELECT 'RECIPE'
  UNION ALL SELECT 'LIBRARY'       UNION ALL SELECT 'OPERATOR'
  UNION ALL SELECT 'TOLERANCE'     UNION ALL SELECT 'PART_NUMBER'
  UNION ALL SELECT 'JEDEC'         UNION ALL SELECT 'FEEDER'
  UNION ALL SELECT 'OBJECT_TYPE'
)
SELECT e.name AS expected_table,
       CASE WHEN t.TABLE_NAME IS NULL THEN 'MISSING' ELSE 'present' END AS status,
       t.TABLE_SCHEMA
FROM expected e
LEFT JOIN INFORMATION_SCHEMA.TABLES t
       ON UPPER(t.TABLE_NAME) = e.name
ORDER BY status DESC, e.name;
"@

    '03_panels_columns' = @"
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'PANELS'
ORDER BY ORDINAL_POSITION;
"@

    '04_cards_columns' = @"
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'CARDS'
ORDER BY ORDINAL_POSITION;
"@

    '05_tested_object_columns' = @"
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'TESTED_OBJECT'
ORDER BY ORDINAL_POSITION;
"@

    '06_pin_measure_columns' = @"
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'PIN_MEASURE'
ORDER BY ORDINAL_POSITION;
"@

    '07_row_counts' = @"
SELECT
  t.name AS table_name,
  SUM(p.rows) AS approx_rows
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
WHERE t.name IN ('PANELS','CARDS','TESTED_OBJECT','PIN','PIN_MEASURE',
                 'MACHINE','PRODUCT','RECIPE','LIBRARY','OPERATOR',
                 'TOLERANCE','PART_NUMBER','JEDEC','FEEDER','OBJECT_TYPE')
GROUP BY t.name
ORDER BY approx_rows DESC;
"@

    '08_panels_date_range' = @"
SELECT TOP 1
  MIN(Panel_Numeric_Date) AS min_epoch,
  MAX(Panel_Numeric_Date) AS max_epoch,
  DATEADD(SECOND, MIN(Panel_Numeric_Date), '1970-01-01') AS min_utc,
  DATEADD(SECOND, MAX(Panel_Numeric_Date), '1970-01-01') AS max_utc,
  COUNT_BIG(*) AS panel_rows
FROM PANELS WITH (NOLOCK);
"@

    '09_panel_status_distribution' = @"
SELECT TOP 20 Panel_Status, COUNT_BIG(*) AS n
FROM PANELS WITH (NOLOCK)
GROUP BY Panel_Status
ORDER BY n DESC;
"@
}

$summary = @()
foreach ($name in $probes.Keys) {
    Write-Host "  running $name..." -NoNewline
    try {
        $rows = Invoke-ReadOnlyQuery -Name $name -Sql $probes[$name]
        $csv  = Join-Path $OutputDir "$name.csv"
        if ($null -ne $rows) {
            $rows | Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8
        }
        $count = if ($null -eq $rows) { 0 } elseif ($rows -is [array]) { $rows.Count } else { 1 }
        Write-Host "  ok  ($count row(s)) -> $csv" -ForegroundColor Green
        $summary += [pscustomobject]@{ Probe = $name; Status = 'ok'; Rows = $count; Error = '' }
    }
    catch {
        Write-Host "  FAIL: $($_.Exception.Message)" -ForegroundColor Red
        $summary += [pscustomobject]@{ Probe = $name; Status = 'fail'; Rows = 0; Error = $_.Exception.Message }
    }
}

$summary | Format-Table -AutoSize
$summary | Export-Csv -Path (Join-Path $OutputDir '_summary.csv') -NoTypeInformation -Encoding UTF8
Write-Host "`nDone. CSVs written to $OutputDir" -ForegroundColor Cyan
