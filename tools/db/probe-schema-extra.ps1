<#
.SYNOPSIS
    Follow-up read-only probe: column layouts + sample rows for the tables not
    covered by probe-schema.ps1.

.DESCRIPTION
    Same guards as probe-schema.ps1 (read-only regex block, READ UNCOMMITTED,
    NOLOCK, tagged ApplicationName). Dumps INFORMATION_SCHEMA.COLUMNS for the
    remaining core tables (PIN, MACHINE, PRODUCT, RECIPE, OBJECT_TYPE) and for
    the extra tables discovered on HLYAOI that our vit-aoi-database skill does
    not yet document (Barcode_Product, LOG_PROD, PIXEL_SIZE, SPC, SPC_OBJECT,
    VERSION). Also dumps full contents of small reference tables (MACHINE,
    OBJECT_TYPE, FEEDER, PIXEL_SIZE, VERSION) and TOP 5 samples of the larger
    unknown tables.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'out')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --- 1. Locate and load .env -------------------------------------------------
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$envPath  = Join-Path $repoRoot '.env'
if (-not (Test-Path -LiteralPath $envPath)) {
    throw "Missing $envPath - copy .env.example to .env first."
}

$envVars = @{}
Get-Content -LiteralPath $envPath | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
        $envVars[$Matches[1]] = $Matches[2].Trim('"').Trim("'")
    }
}
foreach ($key in 'AOI_SERVER','AOI_DATABASE','AOI_USER','AOI_PASSWORD') {
    if (-not $envVars.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($envVars[$key])) {
        throw "Missing or empty $key in $envPath"
    }
}
$server   = $envVars['AOI_SERVER']
$database = $envVars['AOI_DATABASE']
$user     = $envVars['AOI_USER']
$password = $envVars['AOI_PASSWORD']
$connTO   = if ($envVars.ContainsKey('AOI_CONNECT_TIMEOUT')) { [int]$envVars['AOI_CONNECT_TIMEOUT'] } else { 15 }
$queryTO  = if ($envVars.ContainsKey('AOI_QUERY_TIMEOUT'))   { [int]$envVars['AOI_QUERY_TIMEOUT']   } else { 60 }

# --- 2. Read-only guard + SqlClient connection ------------------------------
$forbidden = '\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE|EXEC|EXECUTE|GRANT|REVOKE|CREATE)\b'
$connString = "Server=$server;Database=$database;User Id=$user;Password=$password;" +
              "Application Name=Nieweb-probe-schema-extra;Connect Timeout=$connTO;" +
              "TrustServerCertificate=True;Encrypt=False;"
Add-Type -AssemblyName 'System.Data'

function Invoke-ReadOnlyQuery {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Sql
    )
    if ($Sql -match $forbidden) {
        throw "Refusing to run '$Name' - forbidden keyword found."
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
        $columns = $dt.Columns | ForEach-Object { $_.ColumnName }
        $out = foreach ($row in $dt.Rows) {
            $obj = [ordered]@{}
            foreach ($c in $columns) { $obj[$c] = $row[$c] }
            [pscustomobject]$obj
        }
        return ,$out
    }
    finally {
        $conn.Close(); $conn.Dispose()
    }
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
Write-Host "Probing $server/$database (extra tables, read-only)..." -ForegroundColor Cyan

# --- 3. Column-layout probes for remaining tables ---------------------------
$colProbes = @(
    'PIN','MACHINE','PRODUCT','RECIPE','OBJECT_TYPE',
    'Barcode_Product','LOG_PROD','PIXEL_SIZE','SPC','SPC_OBJECT','VERSION',
    'CARDS_HISTO','PANELS_HISTO','PIN_HISTO','TESTED_OBJECT_HISTO'
)

# --- 4. Full-dump probes for small ref tables --------------------------------
$fullDump = @('MACHINE','OBJECT_TYPE','FEEDER','PIXEL_SIZE','VERSION')

# --- 5. TOP-5 sample probes for larger unknown tables -----------------------
$sample5 = @('Barcode_Product','LOG_PROD','SPC','SPC_OBJECT')

# --- 6. Row-count for the extra tables --------------------------------------
$extraCountSql = @"
SELECT
  t.name AS table_name,
  SUM(p.rows) AS approx_rows
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
WHERE t.name IN ('Barcode_Product','LOG_PROD','PIXEL_SIZE','SPC','SPC_OBJECT','VERSION',
                 'CARDS_HISTO','PANELS_HISTO','PIN_HISTO','TESTED_OBJECT_HISTO')
GROUP BY t.name
ORDER BY approx_rows DESC;
"@

$summary = @()

function Run-And-Save {
    param([string]$Name, [string]$Sql)
    Write-Host "  $Name..." -NoNewline
    try {
        $rows = Invoke-ReadOnlyQuery -Name $Name -Sql $Sql
        $csv  = Join-Path $OutputDir "$Name.csv"
        if ($null -ne $rows) {
            $rows | Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8
        }
        $count = if ($null -eq $rows) { 0 } elseif ($rows -is [array]) { $rows.Count } else { 1 }
        Write-Host "  ok  ($count) -> $csv" -ForegroundColor Green
        return [pscustomobject]@{ Probe = $Name; Status = 'ok'; Rows = $count; Error = '' }
    }
    catch {
        Write-Host "  FAIL: $($_.Exception.Message)" -ForegroundColor Red
        return [pscustomobject]@{ Probe = $Name; Status = 'fail'; Rows = 0; Error = $_.Exception.Message }
    }
}

# 3. Column layouts
foreach ($t in $colProbes) {
    $sql = @"
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = '$t'
ORDER BY ORDINAL_POSITION;
"@
    $summary += Run-And-Save "10_columns_${t}" $sql
}

# 4. Full dumps of small ref tables
foreach ($t in $fullDump) {
    $sql = "SELECT * FROM [$t] WITH (NOLOCK);"
    $summary += Run-And-Save "20_full_${t}" $sql
}

# 5. TOP 5 samples of larger unknown tables
foreach ($t in $sample5) {
    $sql = "SELECT TOP 5 * FROM [$t] WITH (NOLOCK);"
    $summary += Run-And-Save "30_sample_${t}" $sql
}

# 6. Row counts for extras
$summary += Run-And-Save '40_extra_row_counts' $extraCountSql

$summary | Format-Table -AutoSize
$summary | Export-Csv -Path (Join-Path $OutputDir '_summary_extra.csv') -NoTypeInformation -Encoding UTF8
Write-Host "`nDone. CSVs in $OutputDir" -ForegroundColor Cyan
