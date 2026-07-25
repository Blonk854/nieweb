<#
.SYNOPSIS
    Ad-hoc read of PANELS.Anomaly_BR/AR + CARDS.Anomaly_BR/AR for a
    given barcode + subpanel index, on both post- and pre-reflow DBs.
    Used to verify "Skipped sub-panel" bit 9 (256) is set on a
    subpanel the placement gear intentionally skipped.

.EXAMPLE
    PS> .\tools\db\peek-anomaly-bits.ps1 -Barcode 43262045659200 -CardIdOnPanel 6
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Barcode,
    [Parameter(Mandatory)][int]$CardIdOnPanel
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$envPath  = Join-Path $repoRoot '.env'
if (-not (Test-Path -LiteralPath $envPath)) { throw "Missing $envPath" }

$envVars = @{}
Get-Content -LiteralPath $envPath | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
        $envVars[$Matches[1]] = $Matches[2].Trim('"').Trim("'")
    }
}
$connTO  = if ($envVars.ContainsKey('AOI_CONNECT_TIMEOUT')) { [int]$envVars['AOI_CONNECT_TIMEOUT'] } else { 15 }
$queryTO = if ($envVars.ContainsKey('AOI_QUERY_TIMEOUT'))   { [int]$envVars['AOI_QUERY_TIMEOUT']   } else { 60 }

$forbidden = '\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE|EXEC|EXECUTE|GRANT|REVOKE|CREATE)\b'
Add-Type -AssemblyName 'System.Data'

function Get-ConnString {
    param([string]$Prefix, [string]$Tag)
    foreach ($k in 'SERVER','DATABASE','USER','PASSWORD') {
        if (-not $envVars.ContainsKey("$Prefix$k") -or [string]::IsNullOrWhiteSpace($envVars["$Prefix$k"])) {
            throw "Missing $Prefix$k in .env"
        }
    }
    return ("Server={0};Database={1};User Id={2};Password={3};" +
            "Application Name=Nieweb-peek-anomaly-{4};Connect Timeout={5};" +
            "TrustServerCertificate=True;Encrypt=False;") -f
        $envVars["${Prefix}SERVER"], $envVars["${Prefix}DATABASE"],
        $envVars["${Prefix}USER"],   $envVars["${Prefix}PASSWORD"], $Tag, $connTO
}

function Invoke-ReadOnlyQuery {
    param([string]$ConnString, [string]$Sql)
    if ($Sql -match $forbidden) { throw "Refusing forbidden keyword." }
    $prelude = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;`nSET NOCOUNT ON;`n"
    $conn = New-Object System.Data.SqlClient.SqlConnection $ConnString
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $prelude + $Sql
        $cmd.CommandTimeout = $queryTO
        [void]$cmd.Parameters.AddWithValue('@barcode', $Barcode)
        [void]$cmd.Parameters.AddWithValue('@cardId', $CardIdOnPanel)
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
        $dt = New-Object System.Data.DataTable
        [void]$adapter.Fill($dt)
        $cols = $dt.Columns | ForEach-Object { $_.ColumnName }
        $out = foreach ($row in $dt.Rows) {
            $obj = [ordered]@{}
            foreach ($c in $cols) {
                $v = $row[$c]
                if ($v -is [DBNull]) { $v = $null }
                $obj[$c] = $v
            }
            [pscustomobject]$obj
        }
        return ,$out
    } finally { $conn.Close(); $conn.Dispose() }
}

$sql = @"
SELECT
  p.Panel_Id,
  p.Panel_Bar_Code,
  p.Face_Number,
  p.Panel_Numeric_Date,
  p.Panel_Status         AS panel_status,
  p.Anomaly_BR           AS panel_anomaly_br,
  p.Anomaly_AR           AS panel_anomaly_ar,
  c.Card_Number          AS card_id_on_panel,
  c.Card_Status          AS card_status,
  c.Anomaly_BR           AS card_anomaly_br,
  c.Anomaly_AR           AS card_anomaly_ar
FROM dbo.PANELS p WITH (NOLOCK)
INNER JOIN dbo.CARDS c WITH (NOLOCK)
  ON c.Panel_Id = p.Panel_Id AND c.Card_Number = @cardId
WHERE p.Panel_Bar_Code = @barcode
ORDER BY p.Face_Number, p.Panel_Numeric_Date DESC;
"@

function Decode-CardAnomaly {
    param([long]$Value)
    $meanings = @()
    $bits = @{
        1    = 'bit1: Fiducial error'
        8    = 'bit4: Ejected by review'
        16   = 'bit5: Washed by review'
        32   = 'bit6: One or more defects'
        128  = 'bit8: Axis error'
        256  = 'bit9: Skipped sub-panel'
        512  = 'bit10: Invalidated by review'
        1024 = 'bit11: Too many defects (overflow)'
        2048 = 'bit12: NOT inspected marker'
    }
    foreach ($mask in ($bits.Keys | Sort-Object)) {
        if (($Value -band $mask) -ne 0) { $meanings += $bits[$mask] }
    }
    if ($meanings.Count -eq 0) { return '(no bits set)' }
    return $meanings -join '; '
}

foreach ($stage in @(
    @{ Prefix='AOI_POSTREFLOW_'; Tag='postreflow' },
    @{ Prefix='AOI_PREREFLOW_';  Tag='prereflow'  })) {
    Write-Host ("--- {0} ---" -f $stage.Tag.ToUpper()) -ForegroundColor Cyan
    try {
        $rows = Invoke-ReadOnlyQuery -ConnString (Get-ConnString -Prefix $stage.Prefix -Tag $stage.Tag) -Sql $sql
        if ($rows.Count -eq 0) {
            Write-Host "  no match" -ForegroundColor DarkGray
            continue
        }
        foreach ($r in $rows) {
            Write-Host ("  panel_id={0}  face={1}  ts={2}" -f $r.Panel_Id, $r.Face_Number, $r.Panel_Numeric_Date) -ForegroundColor Yellow
            Write-Host ("    panel_status = {0}" -f $r.panel_status)
            Write-Host ("    panel_anomaly_br = {0}   {1}" -f $r.panel_anomaly_br, (Decode-CardAnomaly ([int64]$r.panel_anomaly_br)))
            Write-Host ("    panel_anomaly_ar = {0}   {1}" -f $r.panel_anomaly_ar, (Decode-CardAnomaly ([int64]$r.panel_anomaly_ar)))
            Write-Host ("    card_id_on_panel = {0}" -f $r.card_id_on_panel)
            Write-Host ("    card_status  = {0}" -f $r.card_status)
            Write-Host ("    card_anomaly_br  = {0}   {1}" -f $r.card_anomaly_br, (Decode-CardAnomaly ([int64]$r.card_anomaly_br)))
            Write-Host ("    card_anomaly_ar  = {0}   {1}" -f $r.card_anomaly_ar, (Decode-CardAnomaly ([int64]$r.card_anomaly_ar)))
        }
    } catch {
        Write-Host ("  ERROR: {0}" -f $_.Exception.Message) -ForegroundColor Red
    }
}
