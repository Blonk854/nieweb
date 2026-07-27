<#
.SYNOPSIS
    Read-only DPMO ground-truth probe. Computes component DPMO the CANONICAL
    way (defects / SUM(CARDS.Nb_Of_Tests_On_Comp)) using the AOI's own
    precomputed per-card counts, and contrasts it with the WRONG denominator
    Nieweb uses today (COUNT of component TESTED_OBJECT rows, which is
    defect-only in production). Gives a golden target for the fix.

    SELECT-only, WITH (NOLOCK), READ UNCOMMITTED, time-boxed.

.EXAMPLE
    PS> .\tools\db\probe-dpmo-truth.ps1 -Database HLYAOI | Tee-Object out.txt
#>
[CmdletBinding()]
param(
    [string]$Database  = 'HLYAOI',
    [string]$Prefix    = 'AOI_POSTREFLOW_',
    [string]$FreezeUtc = '2025-11-14T22:32:51',
    [int]$WindowHours  = 24
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
$cs = "Server=$($envVars["${Prefix}SERVER"]);Database=$Database;User Id=$($envVars["${Prefix}USER"]);Password=$($envVars["${Prefix}PASSWORD"]);Application Name=Nieweb-probe-dpmo;Connect Timeout=15;TrustServerCertificate=True;Encrypt=False;"
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
    Write-Output ""
    Write-Output "===== $label ====="
    ($dt | Format-Table -AutoSize | Out-String).TrimEnd() | Write-Output
}
$win = "DECLARE @to int = DATEDIFF(SECOND,'1970-01-01','$FreezeUtc'); DECLARE @from int = @to - $WindowHours*3600;"
Write-Output "DB=$Database  freeze=$FreezeUtc  window=${WindowHours}h"

Run 'D1 CANONICAL component DPMO (AOI precomputed: defects / Nb_Of_Tests_On_Comp)' "$win
SELECT
  SUM(CAST(c.Nb_Of_Tests_On_Comp AS bigint))       AS comp_tests,
  SUM(CAST(c.DPMO_COMPONENT_DEFECT_NB AS bigint))  AS comp_defects,
  CAST(1000000.0 * SUM(CAST(c.DPMO_COMPONENT_DEFECT_NB AS float))
       / NULLIF(SUM(CAST(c.Nb_Of_Tests_On_Comp AS float)),0) AS decimal(12,2)) AS dpmo_correct,
  COUNT(*) AS cards
FROM PANELS p WITH (NOLOCK)
JOIN CARDS  c WITH (NOLOCK) ON c.Panel_Id = p.Panel_Id
WHERE p.Panel_Numeric_Date BETWEEN @from AND @to AND p.Has_Been_Reviewed<>255;"

Run 'D2 WRONG denominator Nieweb uses today (count of component TESTED_OBJECT rows)' "$win
SELECT COUNT(*) AS to_component_opportunities
FROM PANELS        p WITH (NOLOCK)
JOIN CARDS         c WITH (NOLOCK) ON c.Panel_Id = p.Panel_Id
JOIN TESTED_OBJECT t WITH (NOLOCK) ON t.Card_Id  = c.Card_Id
JOIN OBJECT_TYPE   o WITH (NOLOCK) ON o.Object_Type_Id = t.Object_Type_Id
WHERE p.Panel_Numeric_Date BETWEEN @from AND @to AND p.Has_Been_Reviewed<>255
  AND (o.Object_Type_Id & 1) = 1;"

Run 'D3 real component defect BITS (popcount of Error_Table_AR) + both DPMOs' "$win
;WITH bits AS (SELECT TOP (25) n = CAST(ROW_NUMBER() OVER (ORDER BY (SELECT 1)) - 1 AS int) FROM sys.all_objects),
defects AS (
  SELECT SUM((CONVERT(bigint, t.Error_Table_AR) / POWER(CAST(2 AS bigint), b.n)) % 2) AS real_comp_defect_bits,
         COUNT(DISTINCT t.Tested_Object_Id) AS defective_comp_objects
  FROM PANELS p WITH (NOLOCK)
  JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
  JOIN TESTED_OBJECT t WITH (NOLOCK) ON t.Card_Id=c.Card_Id
  JOIN OBJECT_TYPE o WITH (NOLOCK) ON o.Object_Type_Id=t.Object_Type_Id
  CROSS JOIN bits b
  WHERE p.Panel_Numeric_Date BETWEEN @from AND @to AND p.Has_Been_Reviewed<>255
    AND (o.Object_Type_Id & 1)=1 AND t.Error_Table_AR<>0
),
tests AS (
  SELECT SUM(CAST(c.Nb_Of_Tests_On_Comp AS float)) AS comp_tests,
         (SELECT COUNT(*) FROM PANELS p2 WITH (NOLOCK)
            JOIN CARDS c2 WITH (NOLOCK) ON c2.Panel_Id=p2.Panel_Id
            JOIN TESTED_OBJECT t2 WITH (NOLOCK) ON t2.Card_Id=c2.Card_Id
            JOIN OBJECT_TYPE o2 WITH (NOLOCK) ON o2.Object_Type_Id=t2.Object_Type_Id
            WHERE p2.Panel_Numeric_Date BETWEEN @from AND @to AND p2.Has_Been_Reviewed<>255
              AND (o2.Object_Type_Id & 1)=1) AS wrong_denom
  FROM PANELS p WITH (NOLOCK) JOIN CARDS c WITH (NOLOCK) ON c.Panel_Id=p.Panel_Id
  WHERE p.Panel_Numeric_Date BETWEEN @from AND @to AND p.Has_Been_Reviewed<>255
)
SELECT d.real_comp_defect_bits, t.comp_tests, t.wrong_denom,
  CAST(1000000.0*d.real_comp_defect_bits/NULLIF(t.comp_tests,0)  AS decimal(12,2)) AS dpmo_correct,
  CAST(1000000.0*d.real_comp_defect_bits/NULLIF(t.wrong_denom,0) AS decimal(12,2)) AS dpmo_current_wrong
FROM defects d CROSS JOIN tests t;"

$conn.Close()
Write-Output ""
Write-Output "done. (dpmo_wrong = 1e6 * comp_defects / to_component_opportunities)"
