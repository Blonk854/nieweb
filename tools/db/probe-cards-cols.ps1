<#
.SYNOPSIS
    Read-only column-existence probe for the CARDS DPMO denominators.
    Confirms Nb_Of_Tests_On_Comp / Nb_Of_Tests_On_Pads / Number_Of_Component
    / Number_Of_Pads presence + SQL type on a given DB so the adapter can
    capability-gate correctly.
#>
[CmdletBinding()]
param(
    [string]$Database = 'HLYAOI',
    [string]$Prefix   = 'AOI_POSTREFLOW_'
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
$cs = "Server=$($envVars["${Prefix}SERVER"]);Database=$Database;User Id=$($envVars["${Prefix}USER"]);Password=$($envVars["${Prefix}PASSWORD"]);Application Name=Nieweb-probe-cardscols;Connect Timeout=15;TrustServerCertificate=True;Encrypt=False;"
$conn = New-Object System.Data.SqlClient.SqlConnection $cs
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SET NOCOUNT ON;
SELECT c.name AS column_name, t.name AS sql_type
FROM sys.columns c WITH (NOLOCK)
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.CARDS')
  AND c.name IN ('Nb_Of_Tests_On_Comp','Nb_Of_Tests_On_Pads','Number_Of_Component','Number_Of_Pads')
ORDER BY c.name;
"@
$a = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
$dt = New-Object System.Data.DataTable
[void]$a.Fill($dt)
Write-Output "DB=$Database"
($dt | Format-Table -AutoSize | Out-String).TrimEnd() | Write-Output
$conn.Close()
