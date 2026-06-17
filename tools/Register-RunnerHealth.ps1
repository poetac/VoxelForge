# Register-RunnerHealth.ps1 -- registers a Windows Task Scheduler entry that
# runs tools\RunnerHealth.ps1 on the configured interval.
#
# Must be run from an elevated (admin) PowerShell session because the task is
# registered under SYSTEM so it survives user logouts.
#
# Usage:
#   .\tools\Register-RunnerHealth.ps1                    # register, default 60-minute interval
#   .\tools\Register-RunnerHealth.ps1 -IntervalMinutes 30
#   .\tools\Register-RunnerHealth.ps1 -Unregister        # remove the task
#   .\tools\Register-RunnerHealth.ps1 -WhatIf            # show planned change without applying

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [int]$IntervalMinutes = 60,

    [string]$TaskName = "voxelforge-runner-health",

    [switch]$Unregister
)

$ErrorActionPreference = "Stop"

$isAdmin = ([System.Security.Principal.WindowsPrincipal] `
    [System.Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [System.Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Register-RunnerHealth.ps1 must be run from an elevated PowerShell session."
}

if ($Unregister) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        if ($PSCmdlet.ShouldProcess($TaskName, "Unregister scheduled task")) {
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
            Write-Host "Removed scheduled task '$TaskName'."
        }
    } else {
        Write-Host "Scheduled task '$TaskName' not found; nothing to remove."
    }
    return
}

if ($IntervalMinutes -lt 5 -or $IntervalMinutes -gt 1440) {
    throw "IntervalMinutes $IntervalMinutes outside supported range [5, 1440]."
}

$scriptPath = Join-Path $PSScriptRoot "RunnerHealth.ps1"
if (-not (Test-Path $scriptPath)) {
    throw "Could not find RunnerHealth.ps1 next to this registrar at '$scriptPath'."
}

$action = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -Quiet"

$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) `
    -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes)

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 5) `
    -MultipleInstances IgnoreNew

$principal = New-ScheduledTaskPrincipal `
    -UserId "SYSTEM" `
    -LogonType ServiceAccount `
    -RunLevel Highest

if ($PSCmdlet.ShouldProcess($TaskName, "Register scheduled task (interval $IntervalMinutes min)")) {
    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Settings $settings `
        -Principal $principal `
        -Description "voxelforge: probe self-hosted Actions runners; alert if any are stopped." `
        -Force | Out-Null
    Write-Host "Registered scheduled task '$TaskName' (interval: $IntervalMinutes min, runs as SYSTEM)."
    Write-Host "Inspect via: Get-ScheduledTask -TaskName '$TaskName' | Get-ScheduledTaskInfo"
}
