<#
.SYNOPSIS
    Read-only skip-classification probe. Classifies every CARDS row in a
    window using the SAME rules as SkipClassifier (ManualSkip >
    MachineFlagged > HeuristicMissing, with the default floors) and
    reports the class distribution + sample HeuristicMissing cards, so
    the default thresholds can be validated against real data.

    SELECT-only, WITH (NOLOCK), READ UNCOMMITTED, time-boxed.

.EXAMPLE
    PS> .\tools\db\probe-skip-classify.ps1 -Database HLYAOI | Tee-Object out.txt
#>
[CmdletBinding()]
param(
    [string]$Database   = 'HLYAOI',
    [string]$Prefix     = 'AOI_POSTREFLOW_',
    [string]$FreezeUtc  = '2025-11-14T22:32:51',
    [int]$WindowHours   = 24,
    [double]$Ratio      = 0.50,
    [int]$MinComp       = 8,
    [int]$MinMissing    = 4
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
$cs = "Server=$($envVars["${Prefix}SERVER"]);Database=$Database;User Id=$($envVars["${Prefix}USER"]);Password=$($envVars["${Prefix}PASSWORD"]);Application Name=Nieweb-probe-skipclass;Connect Timeout=15;TrustServerCertificate=True;Encrypt=False;"
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

# Shared per-card aggregate + classification CTE (matches SkipClassifier).
$cte = @"
DECLARE @to int = DATEDIFF(SECOND,'1970-01-01','$FreezeUtc');
DECLARE @from int = @to - $WindowHours*3600;
WITH card_agg AS (
  SELECT c.Card_Id, c.Number_Of_Component AS comp, c.Anomaly_AR AS aar,
         p.Has_Been_Reviewed AS reviewed,
         SUM(CASE WHEN (CONVERT(bigint,t.Error_Table) & 1) = 1 THEN 1 ELSE 0 END) AS missing,
         MAX(CASE WHEN t.Repair_Button_Comment = 'X-OUT' THEN 1 ELSE 0 END) AS xout
  FROM PANELS p WITH (NOLOCK)
  JOIN CARDS  c WITH (NOLOCK) ON c.Panel_Id = p.Panel_Id
  LEFT JOIN TESTED_OBJECT t WITH (NOLOCK) ON t.Card_Id = c.Card_Id
  WHERE p.Panel_Numeric_Date BETWEEN @from AND @to
  GROUP BY c.Card_Id, c.Number_Of_Component, c.Anomaly_AR, p.Has_Been_Reviewed
),
classified AS (
  SELECT *,
    CASE
      WHEN reviewed <> 0 AND xout = 1 THEN 'ManualSkip'
      WHEN (aar & 256) <> 0 THEN 'MachineFlagged'
      WHEN (aar & 1024) = 0 AND comp >= $MinComp AND missing >= $MinMissing
           AND CAST(missing AS float)/NULLIF(comp,0) >= $Ratio THEN 'HeuristicMissing'
      ELSE 'None'
    END AS skip_class
  FROM card_agg
)
"@

Write-Output "DB=$Database  freeze=$FreezeUtc  window=${WindowHours}h  ratio=$Ratio minComp=$MinComp minMissing=$MinMissing"

Run 'S1 class distribution (cards + components + % of cards)' "$cte
SELECT skip_class,
  COUNT(*) AS cards,
  SUM(comp) AS components,
  CAST(100.0*COUNT(*)/SUM(COUNT(*)) OVER () AS decimal(6,2)) AS pct_cards
FROM classified GROUP BY skip_class ORDER BY cards DESC;"

Run 'S2 sample HeuristicMissing cards (worst 10 by missing ratio)' "$cte
SELECT TOP 10 Card_Id, comp, missing,
  CAST(100.0*missing/NULLIF(comp,0) AS decimal(6,2)) AS missing_pct, aar
FROM classified WHERE skip_class='HeuristicMissing'
ORDER BY CAST(missing AS float)/NULLIF(comp,0) DESC;"

Run 'S3 near-threshold sensitivity (cards with ratio in 0.30..0.70, not otherwise skipped)' "$cte
SELECT
  CASE
    WHEN CAST(missing AS float)/NULLIF(comp,0) >= 0.60 THEN '0.60-0.70+'
    WHEN CAST(missing AS float)/NULLIF(comp,0) >= 0.50 THEN '0.50-0.60'
    WHEN CAST(missing AS float)/NULLIF(comp,0) >= 0.40 THEN '0.40-0.50'
    ELSE '0.30-0.40'
  END AS ratio_band,
  COUNT(*) AS cards
FROM classified
WHERE xout=0 AND (aar & 256)=0 AND (aar & 1024)=0 AND comp >= $MinComp
  AND CAST(missing AS float)/NULLIF(comp,0) >= 0.30
  AND CAST(missing AS float)/NULLIF(comp,0) < 0.70
GROUP BY CASE
    WHEN CAST(missing AS float)/NULLIF(comp,0) >= 0.60 THEN '0.60-0.70+'
    WHEN CAST(missing AS float)/NULLIF(comp,0) >= 0.50 THEN '0.50-0.60'
    WHEN CAST(missing AS float)/NULLIF(comp,0) >= 0.40 THEN '0.40-0.50'
    ELSE '0.30-0.40'
  END
ORDER BY ratio_band DESC;"

$conn.Close()
Write-Output ""
Write-Output "done."
