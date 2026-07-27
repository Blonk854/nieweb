<#
.SYNOPSIS
    Read-only diagnostic for the skip missing-ratio discrepancy: shows the
    real top-missing cards, totals, and a specific card lookup, so we can
    tell whether the HeuristicMissing default is genuinely inactive in the
    window or whether the missing-count logic is wrong.
    SELECT-only, NOLOCK, READ UNCOMMITTED, time-boxed.
#>
[CmdletBinding()]
param(
    [string]$Database  = 'HLYAOI',
    [string]$Prefix    = 'AOI_POSTREFLOW_',
    [string]$FreezeUtc = '2025-11-14T22:32:51',
    [int]$WindowHours  = 24,
    [long]$CardId      = 107564636
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$envVars = @{}
Get-Content -LiteralPath (Join-Path $repoRoot '.env') | ForEach-Object {
    $l = $_.Trim()
    if ($l -and -not $l.StartsWith('#') -and $l -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
        $envVars[$Matches[1]] = $Matches[2].Trim('"').Trim("'")
    }
}
Add-Type -AssemblyName 'System.Data'
$forbidden = '\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE|EXEC|EXECUTE|GRANT|REVOKE|CREATE)\b'
$cs = "Server=$($envVars["${Prefix}SERVER"]);Database=$Database;User Id=$($envVars["${Prefix}USER"]);Password=$($envVars["${Prefix}PASSWORD"]);Application Name=Nieweb-probe-skipdiag;Connect Timeout=15;TrustServerCertificate=True;Encrypt=False;"
$conn = New-Object System.Data.SqlClient.SqlConnection $cs
$conn.Open()
function Run($label, $sql) {
    if ($sql -match $forbidden) { throw "forbidden: $label" }
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SET NOCOUNT ON; " + $sql
    $cmd.CommandTimeout = 180
    $a = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
    $dt = New-Object System.Data.DataTable
    [void]$a.Fill($dt)
    Write-Output ""; Write-Output "===== $label ====="
    ($dt | Format-Table -AutoSize | Out-String).TrimEnd() | Write-Output
}
$win = "DECLARE @to int = DATEDIFF(SECOND,'1970-01-01','$FreezeUtc'); DECLARE @from int = @to - $WindowHours*3600;"
Write-Output "DB=$Database  window=${WindowHours}h"

Run 'D1 total TESTED_OBJECT with Error_Table bit1 (missing) in window' "$win
SELECT COUNT(*) AS total_missing_objects
FROM PANELS p WITH (NOLOCK)
JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
JOIN TESTED_OBJECT t WITH (NOLOCK) ON t.Card_Id=c.Card_Id
WHERE p.Panel_Numeric_Date BETWEEN @from AND @to AND (CONVERT(bigint,t.Error_Table) & 1)=1;"

Run 'D2 top 15 cards by missing count (Error_Table bit1) in window' "$win
SELECT TOP 15 c.Card_Id, c.Number_Of_Component AS comp,
  SUM(CASE WHEN (CONVERT(bigint,t.Error_Table) & 1)=1 THEN 1 ELSE 0 END) AS missing_br,
  SUM(CASE WHEN (CONVERT(bigint,t.Error_Table_AR) & 1)=1 THEN 1 ELSE 0 END) AS missing_ar,
  COUNT(t.Tested_Object_Id) AS to_rows
FROM PANELS p WITH (NOLOCK)
JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
LEFT JOIN TESTED_OBJECT t WITH (NOLOCK) ON t.Card_Id=c.Card_Id
WHERE p.Panel_Numeric_Date BETWEEN @from AND @to
GROUP BY c.Card_Id, c.Number_Of_Component
ORDER BY SUM(CASE WHEN (CONVERT(bigint,t.Error_Table) & 1)=1 THEN 1 ELSE 0 END) DESC;"

Run 'D3 specific card lookup (any window)' "
SELECT c.Card_Id, c.Number_Of_Component AS comp, c.Anomaly_AR AS aar,
  p.Panel_Numeric_Date,
  DATEADD(SECOND, p.Panel_Numeric_Date, '1970-01-01') AS panel_utc,
  SUM(CASE WHEN (CONVERT(bigint,t.Error_Table) & 1)=1 THEN 1 ELSE 0 END) AS missing_br,
  SUM(CASE WHEN (CONVERT(bigint,t.Error_Table_AR) & 1)=1 THEN 1 ELSE 0 END) AS missing_ar,
  COUNT(t.Tested_Object_Id) AS to_rows
FROM CARDS c WITH (NOLOCK)
JOIN PANELS p WITH (NOLOCK) ON p.Panel_Id=c.Panel_Id
LEFT JOIN TESTED_OBJECT t WITH (NOLOCK) ON t.Card_Id=c.Card_Id
WHERE c.Card_Id = $CardId
GROUP BY c.Card_Id, c.Number_Of_Component, c.Anomaly_AR, p.Panel_Numeric_Date;"

Run 'D4 heuristic candidates (>=50% missing, comp>=8, not overflow) split by X-OUT flag' "$win
WITH agg AS (
  SELECT c.Card_Id, c.Number_Of_Component AS comp, c.Anomaly_AR AS aar, p.Has_Been_Reviewed AS reviewed,
    SUM(CASE WHEN (CONVERT(bigint,t.Error_Table)&1)=1 THEN 1 ELSE 0 END) AS missing,
    MAX(CASE WHEN t.Repair_Button_Comment='X-OUT' THEN 1 ELSE 0 END) AS xout
  FROM PANELS p WITH (NOLOCK)
  JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
  LEFT JOIN TESTED_OBJECT t WITH (NOLOCK) ON t.Card_Id=c.Card_Id
  WHERE p.Panel_Numeric_Date BETWEEN @from AND @to
  GROUP BY c.Card_Id, c.Number_Of_Component, c.Anomaly_AR, p.Has_Been_Reviewed
)
SELECT xout, COUNT(*) AS cards, MIN(comp) AS min_comp, MIN(missing) AS min_missing, MAX(reviewed) AS max_reviewed
FROM agg
WHERE comp>=8 AND (aar & 1024)=0 AND CAST(missing AS float)/NULLIF(comp,0) >= 0.50
GROUP BY xout;"

$conn.Close(); Write-Output ""; Write-Output "done."
