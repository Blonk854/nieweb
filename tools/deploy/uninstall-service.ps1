<#
.SYNOPSIS
    Stops and removes the Nieweb Windows service registered by
    install-service.ps1.

.PARAMETER ServiceName
    SCM service key. Defaults to 'Nieweb'. Must match what was passed
    to install-service.ps1.

.PARAMETER TimeoutSeconds
    How long to wait for the service to stop gracefully before
    escalating to sc.exe delete. Defaults to 30 seconds.

.EXAMPLE
    .\uninstall-service.ps1

.EXAMPLE
    .\uninstall-service.ps1 -ServiceName Nieweb-Staging

.NOTES
    Run from an elevated PowerShell session.
#>
[CmdletBinding()]
param(
    [string]$ServiceName = 'Nieweb',
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

function Assert-Elevated {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'uninstall-service.ps1 must run from an elevated PowerShell session.'
    }
}

Assert-Elevated

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    Write-Host "Service '$ServiceName' is not installed - nothing to do." -ForegroundColor Yellow
    return
}

if ($svc.Status -ne 'Stopped') {
    Write-Host "Stopping service '$ServiceName'..." -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force -ErrorAction Continue
    try {
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds($TimeoutSeconds))
    } catch [System.ServiceProcess.TimeoutException] {
        Write-Warning "Service '$ServiceName' did not stop within $TimeoutSeconds s; forcing delete."
    }
}

Write-Host "Removing service '$ServiceName'..." -ForegroundColor Cyan
& sc.exe delete $ServiceName | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "sc.exe delete failed with exit code $LASTEXITCODE."
}

Write-Host "Service '$ServiceName' removed." -ForegroundColor Green
