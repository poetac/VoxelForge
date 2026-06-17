<#
.SYNOPSIS
    Generate a cross-pillar fixture validation status report (#653).

.DESCRIPTION
    Reads TRX test-result files from -TrxDir, extracts per-pillar pass/fail
    counts, and writes a consolidated markdown report to $env:GITHUB_STEP_SUMMARY
    (when running in GitHub Actions) and to -OutFile when specified.
    Exits 1 if any pillar has failures; exits 0 if all pillars are green.

.PARAMETER TrxDir
    Directory to search recursively for *.trx files (default: current dir).

.PARAMETER OutFile
    Optional path to write the consolidated report as a standalone .md file.
#>
param(
    [string]$TrxDir  = '.',
    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'

# Pillar display names keyed by TRX artifact name fragment
$pillarNames = [ordered]@{
    'rocket'       = 'Rocket'
    'airbreathing' = 'Airbreathing'
    'electric'     = 'Electric Propulsion'
    'marine'       = 'Marine'
    'nuclear'      = 'Nuclear'
    'cfd'          = 'CFD'
    'crossfamily'  = 'Cross-Pillar Contract'
}

$trxFiles = Get-ChildItem -Path $TrxDir -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue
if (-not $trxFiles) {
    Write-Warning "No .trx files found under '$TrxDir'."
    exit 0
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("## Cross-pillar fixture validation status")
$lines.Add("")
$lines.Add("| Pillar | Passed | Failed | Skipped | Status |")
$lines.Add("|--------|--------|--------|---------|--------|")

$totalPassed  = 0
$totalFailed  = 0
$totalSkipped = 0
$anyFailure   = $false

foreach ($trx in $trxFiles | Sort-Object Name) {
    [xml]$doc = Get-Content $trx.FullName -Encoding UTF8
    $counters  = $doc.TestRun.ResultSummary.Counters

    $passed   = [int]($counters.passed  ?? 0)
    $failed   = [int]($counters.failed  ?? 0)
    $skipped  = [int]($counters.skipped ?? 0) + [int]($counters.notExecuted ?? 0)
    $errors   = [int]($counters.error   ?? 0)
    $totalFail = $failed + $errors

    $totalPassed  += $passed
    $totalFailed  += $totalFail
    $totalSkipped += $skipped

    # Map TRX filename to pillar display name
    $pillarKey = $pillarNames.Keys | Where-Object { $trx.Name -match $_ } | Select-Object -First 1
    $pillarLabel = if ($pillarKey) { $pillarNames[$pillarKey] } else { [System.IO.Path]::GetFileNameWithoutExtension($trx.Name) }

    $status = if ($totalFail -gt 0) { '❌ FAIL'; $anyFailure = $true } else { '✅ pass' }
    $lines.Add("| $pillarLabel | $passed | $totalFail | $skipped | $status |")
}

$lines.Add("")
$lines.Add("**Total: $totalPassed passed, $totalFailed failed, $totalSkipped skipped**")
$lines.Add("")

if ($anyFailure) {
    $lines.Add("> ⚠ One or more pillars have test failures. Check individual TRX artifacts for details.")
} else {
    $lines.Add("> ✅ All pillars green.")
}

if ($env:GITHUB_STEP_SUMMARY) {
    $lines | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding UTF8
}
if ($OutFile) {
    $lines | Set-Content -Path $OutFile -Encoding UTF8
    Write-Host "Report written to: $OutFile"
}
$lines | Write-Host

exit ($anyFailure ? 1 : 0)
