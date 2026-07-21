<#
.SYNOPSIS
    Registers Nieweb.Api as a Windows service using the Service Control
    Manager (sc.exe). Intended to run on the SMT-line box after a
    `dotnet publish` artifact has been copied into place.

.DESCRIPTION
    Nieweb.Api is a self-contained-false publish (.NET 10 runtime must
    be installed on the target machine). This script points the SCM at
    the produced Nieweb.Api.exe. UseWindowsService() in Program.cs
    detects the SCM launch and wires host lifetime to service
    start/stop signals - see Microsoft.Extensions.Hosting.WindowsServices.

    Log destination and appsettings overrides are the responsibility of
    the operator: place appsettings.Production.json alongside
    Nieweb.Api.exe (or use environment variables) so the service picks
    them up. Serilog defaults to logs/nieweb-{Date}.log relative to the
    binary path.

.PARAMETER BinPath
    Absolute path to the published Nieweb.Api.exe (mandatory).

.PARAMETER ServiceName
    SCM service key. Defaults to 'Nieweb'.

.PARAMETER DisplayName
    Human-readable name shown in services.msc.

.PARAMETER Description
    Text shown in the service properties dialog.

.PARAMETER Account
    Service account. Defaults to 'NT AUTHORITY\NetworkService'. Pass
    'LocalSystem' or a domain account (e.g. 'DOMAIN\svc_nieweb') if
    the DB / file-share permissions require it. Domain accounts also
    need -Password.

.PARAMETER Password
    Password for -Account when running under a domain user. Ignored
    for built-in accounts. Prompted securely if omitted with a
    non-built-in account.

.PARAMETER StartMode
    Service start mode ('auto', 'delayed-auto', 'demand', 'disabled').
    Defaults to 'delayed-auto' so Windows lets ETW / SQL come up first.

.EXAMPLE
    .\install-service.ps1 -BinPath 'C:\Program Files\Nieweb\Nieweb.Api.exe'

.EXAMPLE
    .\install-service.ps1 `
        -BinPath 'D:\apps\Nieweb\Nieweb.Api.exe' `
        -Account 'CORP\svc_nieweb'

.NOTES
    Run from an elevated PowerShell session.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BinPath,

    [string]$ServiceName = 'Nieweb',
    [string]$DisplayName = 'Nieweb - AOI reporting',
    [string]$Description = 'Nieweb reporting API and SPA (reads the VIT Superviseur AOI databases).',

    [string]$Account = 'NT AUTHORITY\NetworkService',
    [System.Security.SecureString]$Password,

    [ValidateSet('auto', 'delayed-auto', 'demand', 'disabled')]
    [string]$StartMode = 'delayed-auto'
)

$ErrorActionPreference = 'Stop'

function Assert-Elevated {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'install-service.ps1 must run from an elevated PowerShell session.'
    }
}

Assert-Elevated

if (-not (Test-Path -LiteralPath $BinPath -PathType Leaf)) {
    throw "BinPath '$BinPath' does not exist or is not a file."
}
$fullBinPath = (Resolve-Path -LiteralPath $BinPath).ProviderPath

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    throw "Service '$ServiceName' already exists. Run uninstall-service.ps1 first, or pass a different -ServiceName."
}

# Built-in service accounts do not take a password.
$builtIn = @(
    'LocalSystem',
    'LocalService',
    'NT AUTHORITY\LocalService',
    'NetworkService',
    'NT AUTHORITY\NetworkService'
)
$isBuiltIn = $builtIn -contains $Account

$scArgs = @(
    'create', $ServiceName,
    "binPath= `"$fullBinPath`"",
    "DisplayName= `"$DisplayName`"",
    "start= $StartMode"
)

if ($isBuiltIn) {
    $scArgs += "obj= `"$Account`""
} else {
    if (-not $Password) {
        $Password = Read-Host -AsSecureString -Prompt "Password for $Account"
    }
    $plain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password))
    try {
        $scArgs += "obj= `"$Account`""
        $scArgs += "password= `"$plain`""
    } finally {
        $plain = $null
    }
}

Write-Host "Creating service '$ServiceName' -> $fullBinPath" -ForegroundColor Cyan
& sc.exe @scArgs | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "sc.exe create failed with exit code $LASTEXITCODE."
}

# Description is a separate call.
& sc.exe description $ServiceName $Description | Out-Host

# Recover on crash: restart after 5 s, then 5 s, then no action.
# Nieweb should stay up on the production box; auto-restart avoids
# leaving line engineers without a reporting UI after a transient
# fault.
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/none | Out-Host

Write-Host "Service '$ServiceName' installed. Start it with:" -ForegroundColor Green
Write-Host "    Start-Service $ServiceName" -ForegroundColor Green
Write-Host "or via services.msc." -ForegroundColor Green
