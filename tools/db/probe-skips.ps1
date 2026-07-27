<#
.SYNOPSIS
    Read-only skip / X-OUT verification probes against a Superviseur AOI
    catalogue (default: the frozen HLYAOI archive, so the live line is
    never touched). Confirms how automatic (machine) skips, disabled-skip
    "missing" pollution, and manual X-OUT repair comments are recorded.

    ALL queries are SELECT-only, WITH (NOLOCK), READ UNCOMMITTED, time-
    boxed and TOP-capped. Nothing is ever written.

.EXAMPLE
    PS> .\tools\db\probe-skips.ps1 -Database HLYAOI | Tee-Object out.txt
#>
[CmdletBinding()]
param(
    [string]$Database   = 'HLYAOI',
    [string]$Prefix     = 'AOI_POSTREFLOW_',
    [string]$FreezeUtc  = '2025-11-14T22:32:51',
    [int]$WindowHours   = 6,
    [int]$HeavyHours    = 2
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$envPath  = Join-Path $repoRoot '.env'
if (-not (Test-Path -LiteralPath $envPath)) { throw "Missing $envPath" }
$envVars = @{}
Get-Content -LiteralPath $envPath | ForEach-Object {
    $l = $_.Trim()
    if ($l -and -not $l.StartsWith('#') -and $l -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
        $envVars[$Matches[1]] = $Matches[2].Trim('"').Trim("'")
    }
}
Add-Type -AssemblyName 'System.Data'
$forbidden = '\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE|EXEC|EXECUTE|GRANT|REVOKE|CREATE)\b'

$server = $envVars["${Prefix}SERVER"]
$user   = $envVars["${Prefix}USER"]
$pass   = $envVars["${Prefix}PASSWORD"]
$cs = "Server=$server;Database=$Database;User Id=$user;Password=$pass;" +
      "Application Name=Nieweb-probe-skips;Connect Timeout=15;TrustServerCertificate=True;Encrypt=False;"
$conn = New-Object System.Data.SqlClient.SqlConnection $cs
$conn.Open()

function Run($label, $sql) {
    if ($sql -match $forbidden) { throw "Refusing forbidden keyword in: $label" }
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SET NOCOUNT ON; " + $sql
    $cmd.CommandTimeout = 180
    $a  = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
    $dt = New-Object System.Data.DataTable
    [void]$a.Fill($dt)
    Write-Output ""
    Write-Output "===== $label ====="
    ($dt | Format-Table -AutoSize | Out-String).TrimEnd() | Write-Output
}

$win  = "DECLARE @to int = DATEDIFF(SECOND,'1970-01-01','$FreezeUtc'); DECLARE @from int = @to - $WindowHours*3600;"
$heavy = "DECLARE @to int = DATEDIFF(SECOND,'1970-01-01','$FreezeUtc'); DECLARE @from int = @to - $HeavyHours*3600;"

Write-Output "DB=$Database  server=$server  freeze=$FreezeUtc  window=${WindowHours}h  heavy=${HeavyHours}h"

Run 'E0 sanity: rows in window' "$win
SELECT
  (SELECT COUNT(*) FROM PANELS        p WITH (NOLOCK) WHERE p.Panel_Numeric_Date BETWEEN @from AND @to) AS panels,
  (SELECT COUNT(*) FROM PANELS p WITH (NOLOCK) JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
     WHERE p.Panel_Numeric_Date BETWEEN @from AND @to) AS cards,
  (SELECT COUNT(*) FROM PANELS p WITH (NOLOCK) JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
     JOIN TESTED_OBJECT t WITH (NOLOCK) ON t.Card_Id=c.Card_Id
     WHERE p.Panel_Numeric_Date BETWEEN @from AND @to) AS tested_objects;"

Run 'META: CARDS columns (component-count candidates)' "
SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.CARDS')
  AND (name LIKE '%Component%' OR name LIKE '%Nb_Of%' OR name LIKE '%Test%' OR name LIKE '%Object%')
ORDER BY name;"

Run 'E1 distinct Repair_Button_Comment (find X-OUT)' "$win
SELECT TOP (100) t.Repair_Button_Comment, COUNT(*) AS n
FROM PANELS p WITH (NOLOCK)
JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
JOIN TESTED_OBJECT t WITH (NOLOCK) ON t.Card_Id=c.Card_Id
WHERE p.Panel_Numeric_Date BETWEEN @from AND @to AND p.Has_Been_Reviewed<>255
  AND t.Repair_Button_Comment IS NOT NULL AND t.Repair_Button_Comment<>''
GROUP BY t.Repair_Button_Comment ORDER BY n DESC;"

Run 'E2 machine-skipped cards (Anomaly bit 9) + row shape' "$win
SELECT TOP (20) p.Panel_Bar_Code, c.Card_Number, c.Card_Status, c.Anomaly_AR, c.Number_Of_Component,
  (SELECT COUNT(*) FROM TESTED_OBJECT t WITH (NOLOCK) WHERE t.Card_Id=c.Card_Id) AS to_rows
FROM PANELS p WITH (NOLOCK)
JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
WHERE p.Panel_Numeric_Date BETWEEN @from AND @to AND p.Has_Been_Reviewed<>255
  AND (c.Anomaly_AR & 256)=256;"

Run 'E4 Not_Inspected_Cause distribution' "$win
SELECT t.Not_Inspected_Cause, COUNT(*) AS n
FROM PANELS p WITH (NOLOCK)
JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
JOIN TESTED_OBJECT t WITH (NOLOCK) ON t.Card_Id=c.Card_Id
WHERE p.Panel_Numeric_Date BETWEEN @from AND @to AND p.Has_Been_Reviewed<>255
GROUP BY t.Not_Inspected_Cause ORDER BY t.Not_Inspected_Cause;"

Run 'E5 is TESTED_OBJECT flagged-only? TO rows vs Number_Of_Component' "$win
SELECT TOP (20) c.Card_Id, c.Number_Of_Component,
  (SELECT COUNT(*) FROM TESTED_OBJECT t WITH (NOLOCK) WHERE t.Card_Id=c.Card_Id) AS to_rows,
  (SELECT COUNT(*) FROM TESTED_OBJECT t WITH (NOLOCK) WHERE t.Card_Id=c.Card_Id AND (t.Error_Table & 1)=1) AS to_missing
FROM PANELS p WITH (NOLOCK)
JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
WHERE p.Panel_Numeric_Date BETWEEN @from AND @to AND p.Has_Been_Reviewed<>255
ORDER BY to_rows DESC;"

Run 'E3 disabled-skip suspects: >=50% of components missing (heavy)' "$heavy
;WITH card AS (
  SELECT c.Card_Id, c.Number_Of_Component AS comp_n,
    (SELECT COUNT(*) FROM TESTED_OBJECT t WITH (NOLOCK) WHERE t.Card_Id=c.Card_Id AND (t.Error_Table & 1)=1) AS missing_n
  FROM PANELS p WITH (NOLOCK)
  JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
  WHERE p.Panel_Numeric_Date BETWEEN @from AND @to AND p.Has_Been_Reviewed<>255
    AND (c.Anomaly_AR & 1024)=0   -- exclude overflow cards
)
SELECT TOP (50) Card_Id, comp_n, missing_n,
  CAST(missing_n AS float)/NULLIF(comp_n,0) AS missing_ratio
FROM card
WHERE comp_n>0 AND CAST(missing_n AS float)/comp_n >= 0.5
ORDER BY missing_ratio DESC;"

Run 'E6 X-OUT sanction mapping (Repair_State_result)' "$win
SELECT t.Repair_State_result, COUNT(*) AS n
FROM PANELS p WITH (NOLOCK)
JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
JOIN TESTED_OBJECT t WITH (NOLOCK) ON t.Card_Id=c.Card_Id
WHERE p.Panel_Numeric_Date BETWEEN @from AND @to AND p.Has_Been_Reviewed<>255
  AND t.Repair_Button_Comment='X-OUT'
GROUP BY t.Repair_State_result ORDER BY n DESC;"

Run 'E7 Card_Status for cards that contain an X-OUT component' "$win
SELECT c.Card_Status, COUNT(DISTINCT c.Card_Id) AS cards
FROM PANELS p WITH (NOLOCK)
JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
JOIN TESTED_OBJECT t WITH (NOLOCK) ON t.Card_Id=c.Card_Id
WHERE p.Panel_Numeric_Date BETWEEN @from AND @to AND p.Has_Been_Reviewed<>255
  AND t.Repair_Button_Comment='X-OUT'
GROUP BY c.Card_Status ORDER BY cards DESC;"

$conn.Close()
Write-Output ""
Write-Output "done."
