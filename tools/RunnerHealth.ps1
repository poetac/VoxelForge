# RunnerHealth.ps1 -- probes the self-hosted GitHub Actions runner services and
# surfaces a notification if any are not RUNNING.
#
# Failure mode this guards against: after a clean service exit the runners stay
# stopped until reboot or a manual Start-Service. A queued workflow appears to
# be running for the full timeout (~30 min) before failing -- the runner-down
# state is invisible until then. This script makes it loud.
#
# Usage:
#   .\tools\RunnerHealth.ps1                         # check both runners
#   .\tools\RunnerHealth.ps1 -RunnerService A        # check runner A only
#   .\tools\RunnerHealth.ps1 -Quiet                  # suppress console output
#   .\tools\RunnerHealth.ps1 -NoToast                # do not raise a toast on failure
#
# Exit codes:
#   0 - all checked runners are RUNNING
#   1 - one or more runners are not RUNNING (or service missing)
#   2 - script-level failure (bad arguments, write to log failed, etc.)
#
# Wire into Task Scheduler via .\tools\Register-RunnerHealth.ps1 (see README).

[CmdletBinding()]
param(
    [ValidateSet("A", "B", "Both")]
    [string]$RunnerService = "Both",

    # Windows service names of the two self-hosted Actions runners. The default
    # pattern is "actions.runner.<repo-slug>.<runner-name>"; override these to
    # match your own runner registration. Inspect installed services with:
    #   Get-Service "actions.runner.*"
    [string]$RunnerServiceA = "actions.runner.poetac-voxelforge.<RUNNER_NAME_A>",
    [string]$RunnerServiceB = "actions.runner.poetac-voxelforge.<RUNNER_NAME_B>",

    [string]$LogDir = (Join-Path $env:LOCALAPPDATA "voxelforge\runner-health"),

    [int]$LogRetentionDays = 30,

    [switch]$Quiet,

    [switch]$NoToast
)

$ErrorActionPreference = "Stop"

$runners = [ordered]@{
    A = $RunnerServiceA
    B = $RunnerServiceB
}

$targets = switch ($RunnerService) {
    "A"    { @("A") }
    "B"    { @("B") }
    "Both" { @("A", "B") }
}

function Write-Console {
    param([string]$Message, [string]$Level = "info")
    if ($Quiet) { return }
    switch ($Level) {
        "warn" { Write-Warning $Message }
        "err"  { Write-Host $Message -ForegroundColor Red }
        default { Write-Host $Message }
    }
}

if (-not (Test-Path $LogDir)) {
    try {
        New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
    } catch {
        Write-Console "Could not create log directory '$LogDir': $_" "err"
        exit 2
    }
}

# Rotation: drop log files older than $LogRetentionDays. One file per day keeps
# rotation trivial -- no read/write lock juggling, no max-size accounting.
$cutoff = (Get-Date).AddDays(-$LogRetentionDays)
Get-ChildItem -Path $LogDir -Filter "runner-health-*.log" -File -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt $cutoff } |
    Remove-Item -Force -ErrorAction SilentlyContinue

$logFile = Join-Path $LogDir ("runner-health-{0}.log" -f (Get-Date -Format "yyyy-MM-dd"))
$timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ssK"

$results = New-Object System.Collections.Generic.List[pscustomobject]
foreach ($key in $targets) {
    $serviceName = $runners[$key]
    $svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        $results.Add([pscustomobject]@{
            Key     = $key
            Service = $serviceName
            Status  = "Missing"
            Healthy = $false
        })
        continue
    }
    $healthy = $svc.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running
    $results.Add([pscustomobject]@{
        Key     = $key
        Service = $serviceName
        Status  = $svc.Status.ToString()
        Healthy = $healthy
    })
}

$lines = foreach ($r in $results) {
    "{0} runner={1} service={2} status={3} healthy={4}" -f $timestamp, $r.Key, $r.Service, $r.Status, $r.Healthy
}

try {
    Add-Content -Path $logFile -Value $lines -Encoding UTF8
} catch {
    Write-Console "Failed to append to log '$logFile': $_" "err"
    exit 2
}

$unhealthy = @($results | Where-Object { -not $_.Healthy })

foreach ($r in $results) {
    $level = if ($r.Healthy) { "info" } else { "warn" }
    Write-Console ("Runner {0} ({1}): {2}" -f $r.Key, $r.Service, $r.Status) $level
}

if ($unhealthy.Count -gt 0 -and -not $NoToast) {
    $summary = ($unhealthy | ForEach-Object { "$($_.Key)=$($_.Status)" }) -join ", "
    $body = "Self-hosted runner(s) not RUNNING: $summary. Restart from an admin shell via <RUNNER_DIR>\manage-runners.ps1 -Command start."

    $toasted = $false
    if (Get-Module -ListAvailable -Name BurntToast) {
        try {
            Import-Module BurntToast -ErrorAction Stop
            New-BurntToastNotification -Text "voxelforge: runner down", $body | Out-Null
            $toasted = $true
        } catch {
            Write-Console "BurntToast notification failed: $_" "warn"
        }
    }
    if (-not $toasted) {
        # Fallback: msg.exe is present on every Windows install and surfaces a
        # blocking dialog on the current session. Better than silent failure.
        try {
            $msgExe = Join-Path $env:SystemRoot "System32\msg.exe"
            if (Test-Path $msgExe) {
                & $msgExe "*" /TIME:60 "voxelforge runner down: $summary" 2>$null
            }
        } catch {
            Write-Console "Fallback msg.exe notification failed: $_" "warn"
        }
    }
}

if ($unhealthy.Count -gt 0) { exit 1 } else { exit 0 }
