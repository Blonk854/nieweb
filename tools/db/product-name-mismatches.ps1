<#
.SYNOPSIS
    List product-name mismatches between the pre-reflow (MEAOI) and
    post-reflow (HLYAOI2024) AOI databases so the SMT-line technician
    can rename the offending machine programs to line them up.

.DESCRIPTION
    A single physical PCB shares the same laser-etched barcode on both
    sides, but the AOI program names differ between the two lines. In
    the common case the pre-reflow name carries a `_PreReflow` suffix
    (`HA013682402_1st_PreReflow`) while the post-reflow name does not
    (`HA013682402_1st`). This script normalises both names by
    stripping that suffix, joins the two `PRODUCT` catalogues on the
    normalised value, and emits every pair whose raw names don't
    already line up.

    Output is a side-by-side CSV plus a Format-Table preview on the
    console, sorted by the pre-reflow AOI machine name so the tech
    can walk down the fix-list one machine at a time.

    Follows the same read-only discipline as tools/db/probe-schema.ps1:
    loads credentials from ../../.env, refuses statements containing
    INSERT / UPDATE / DELETE / DROP / ALTER / TRUNCATE / MERGE / EXEC /
    GRANT / REVOKE / CREATE, prefixes every batch with
    `SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SET NOCOUNT ON;`,
    uses WITH (NOLOCK) on every scan, and tags Application Name so
    DBAs can identify our sessions.

.PARAMETER OutputDir
    Where to write the CSV. Defaults to
    `<repo>/tools/db/out/mismatches/`.

.EXAMPLE
    PS> .\tools\db\product-name-mismatches.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $OutputDir) {
    $OutputDir = Join-Path $PSScriptRoot (Join-Path 'out' 'mismatches')
}

# --- 1. Locate and load .env -------------------------------------------------
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$envPath  = Join-Path $repoRoot '.env'

if (-not (Test-Path -LiteralPath $envPath)) {
    throw "Missing $envPath - copy .env.example to .env and fill in AOI_POSTREFLOW_* / AOI_PREREFLOW_* keys."
}

$envVars = @{}
Get-Content -LiteralPath $envPath | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
        $envVars[$Matches[1]] = $Matches[2].Trim('"').Trim("'")
    }
}

$connTO  = if ($envVars.ContainsKey('AOI_CONNECT_TIMEOUT')) { [int]$envVars['AOI_CONNECT_TIMEOUT'] } else { 15 }
$queryTO = if ($envVars.ContainsKey('AOI_QUERY_TIMEOUT'))   { [int]$envVars['AOI_QUERY_TIMEOUT']   } else { 60 }

function Get-ConnString {
    param(
        [Parameter(Mandatory)][string]$Prefix,
        [Parameter(Mandatory)][string]$SourceTag
    )
    foreach ($suffix in 'SERVER','DATABASE','USER','PASSWORD') {
        $key = "${Prefix}${suffix}"
        if (-not $envVars.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($envVars[$key])) {
            throw "Missing or empty $key in $envPath"
        }
    }
    return ("Server={0};Database={1};User Id={2};Password={3};" +
            "Application Name=Nieweb-product-name-mismatches-{4};" +
            "Connect Timeout={5};TrustServerCertificate=True;Encrypt=False;") -f
        $envVars["${Prefix}SERVER"], $envVars["${Prefix}DATABASE"],
        $envVars["${Prefix}USER"],   $envVars["${Prefix}PASSWORD"],
        $SourceTag, $connTO
}

# --- 2. Read-only guard ------------------------------------------------------
$forbidden = '\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE|EXEC|EXECUTE|GRANT|REVOKE|CREATE)\b'

Add-Type -AssemblyName 'System.Data'

function Invoke-ReadOnlyQuery {
    param(
        [Parameter(Mandatory)][string]$ConnString,
        [Parameter(Mandatory)][string]$Sql
    )
    if ($Sql -match $forbidden) {
        throw "Refusing to run - statement contains a forbidden keyword. This script is read-only."
    }
    $prelude = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;`nSET NOCOUNT ON;`n"

    $conn = New-Object System.Data.SqlClient.SqlConnection $ConnString
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
            foreach ($c in $columns) {
                $val = $row[$c]
                if ($val -is [DBNull]) { $val = $null }
                $obj[$c] = $val
            }
            [pscustomobject]$obj
        }
        return ,$out
    }
    finally {
        $conn.Close()
        $conn.Dispose()
    }
}

# --- 3. One query per DB: product + most-recent AOI machine ------------------
# ROW_NUMBER() picks the panel with the largest Panel_Numeric_Date per
# product so the "machine" column reflects the current line assignment.
# Products with no panels appear once with a null machine (LEFT JOIN).
$sqlProductsWithMachine = @"
;WITH latest AS (
    SELECT Product_Id, Machine_Id,
           ROW_NUMBER() OVER (
               PARTITION BY Product_Id
               ORDER BY Panel_Numeric_Date DESC, Panel_Id DESC
           ) AS rn
    FROM dbo.PANELS WITH (NOLOCK)
)
SELECT pr.Product_Id, pr.Product_Name, m.Machine_Name
FROM dbo.PRODUCT pr WITH (NOLOCK)
LEFT JOIN latest l ON l.Product_Id = pr.Product_Id AND l.rn = 1
LEFT JOIN dbo.MACHINE m WITH (NOLOCK) ON m.Machine_Id = l.Machine_Id
ORDER BY m.Machine_Name, pr.Product_Name;
"@

Write-Host 'Querying post-reflow (HLYAOI2024)...' -ForegroundColor Cyan
$post = Invoke-ReadOnlyQuery -ConnString (Get-ConnString -Prefix 'AOI_POSTREFLOW_' -SourceTag 'postreflow') -Sql $sqlProductsWithMachine

Write-Host 'Querying pre-reflow (MEAOI)...' -ForegroundColor Cyan
$pre  = Invoke-ReadOnlyQuery -ConnString (Get-ConnString -Prefix 'AOI_PREREFLOW_'  -SourceTag 'prereflow')  -Sql $sqlProductsWithMachine

Write-Host ("  post-reflow rows: {0}" -f $post.Count) -ForegroundColor DarkGray
Write-Host ("  pre-reflow  rows: {0}" -f $pre.Count)  -ForegroundColor DarkGray

# --- 4. Normalise + join on the normalised key -------------------------------
# Strip a trailing `_PreReflow` (or `-PreReflow`), whitespace-tolerant,
# case-insensitive. Matches TraceabilityReport.NormalizeProductSvgKey
# on the server so we don't drift between the two rules.
function Get-NormalisedKey {
    param([Parameter(Mandatory)][AllowEmptyString()][AllowNull()][string]$Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return $null }
    $trimmed = $Name.Trim()
    $stripped = [regex]::Replace($trimmed, '[_\-]?PreReflow\s*$', '', 'IgnoreCase')
    if ([string]::IsNullOrWhiteSpace($stripped)) { return $trimmed }
    return $stripped
}

# Bucket both sides by their normalised key (lower-cased so the join is
# case-insensitive too, which is what SQL Server's default collation
# does anyway).
$preByKey  = @{}
foreach ($row in $pre) {
    $key = (Get-NormalisedKey -Name $row.Product_Name)
    if ($null -eq $key) { continue }
    $lower = $key.ToLowerInvariant()
    if (-not $preByKey.ContainsKey($lower))  { $preByKey[$lower]  = @() }
    $preByKey[$lower]  += $row
}
$postByKey = @{}
foreach ($row in $post) {
    $key = (Get-NormalisedKey -Name $row.Product_Name)
    if ($null -eq $key) { continue }
    $lower = $key.ToLowerInvariant()
    if (-not $postByKey.ContainsKey($lower)) { $postByKey[$lower] = @() }
    $postByKey[$lower] += $row
}

# --- 5. Emit mismatched pairs + orphans --------------------------------------
$results = New-Object System.Collections.Generic.List[object]

# Pass 1: every pre-reflow product, matched against its post-reflow twin.
foreach ($lower in $preByKey.Keys) {
    foreach ($preRow in $preByKey[$lower]) {
        $preName    = $preRow.Product_Name
        $preMachine = $preRow.Machine_Name
        if ($postByKey.ContainsKey($lower)) {
            foreach ($postRow in $postByKey[$lower]) {
                $postName    = $postRow.Product_Name
                $postMachine = $postRow.Machine_Name
                # Literal match on the raw names? No fix needed.
                if ($preName -ceq $postName) { continue }
                $results.Add([pscustomobject]@{
                    Kind             = 'MismatchedNames'
                    PreReflowMachine = $preMachine
                    PreReflowName    = $preName
                    PostReflowName   = $postName
                    PostReflowMachine= $postMachine
                    NormalisedKey    = $preName # display the pre name as the key
                })
            }
        } else {
            $results.Add([pscustomobject]@{
                Kind             = 'PreOnly'
                PreReflowMachine = $preMachine
                PreReflowName    = $preName
                PostReflowName   = $null
                PostReflowMachine= $null
                NormalisedKey    = $preName
            })
        }
    }
}

# Pass 2: post-reflow products that have no pre-reflow twin.
foreach ($lower in $postByKey.Keys) {
    if ($preByKey.ContainsKey($lower)) { continue }
    foreach ($postRow in $postByKey[$lower]) {
        $results.Add([pscustomobject]@{
            Kind             = 'PostOnly'
            PreReflowMachine = $null
            PreReflowName    = $null
            PostReflowName   = $postRow.Product_Name
            PostReflowMachine= $postRow.Machine_Name
            NormalisedKey    = $postRow.Product_Name
        })
    }
}

# Sort: by PreReflowMachine first (blank machines at the bottom), then
# by PreReflowName. Post-only rows (no pre machine) fall to the tail.
$sorted = $results | Sort-Object `
    @{Expression = { if ([string]::IsNullOrEmpty($_.PreReflowMachine)) { 1 } else { 0 } }},
    @{Expression = { $_.PreReflowMachine }},
    @{Expression = { $_.PreReflowName }},
    @{Expression = { $_.PostReflowMachine }},
    @{Expression = { $_.PostReflowName }}

# --- 6. Write output ---------------------------------------------------------
if (-not (Test-Path -LiteralPath $OutputDir)) {
    [void](New-Item -ItemType Directory -Path $OutputDir -Force)
}
$stamp   = (Get-Date).ToString('yyyyMMdd-HHmmss')
$csvPath = Join-Path $OutputDir ("product-name-mismatches-{0}.csv" -f $stamp)
$sorted | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8

Write-Host ""
Write-Host ("Total mismatches / orphans: {0}" -f $sorted.Count) -ForegroundColor Yellow
Write-Host ("  MismatchedNames: {0}" -f ($sorted | Where-Object Kind -eq 'MismatchedNames').Count) -ForegroundColor DarkYellow
Write-Host ("  PreOnly (no post twin):   {0}" -f ($sorted | Where-Object Kind -eq 'PreOnly').Count)  -ForegroundColor DarkYellow
Write-Host ("  PostOnly (no pre twin):   {0}" -f ($sorted | Where-Object Kind -eq 'PostOnly').Count) -ForegroundColor DarkYellow
Write-Host ("Wrote CSV: {0}" -f $csvPath) -ForegroundColor Cyan
Write-Host ""

if ($sorted.Count -gt 0) {
    Write-Host "Preview (first 50 rows):" -ForegroundColor Cyan
    $sorted |
        Select-Object -First 50 -Property Kind, PreReflowMachine, PreReflowName, PostReflowName, PostReflowMachine |
        Format-Table -AutoSize -Wrap
}
