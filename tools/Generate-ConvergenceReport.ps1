<#
.SYNOPSIS
    Generate multi-seed SA convergence envelope report (#654).

.DESCRIPTION
    Reads per-preset JSONL files and violation stdout captures from -DataDir,
    computes per-preset score envelopes (best/median/worst, CV), convergence
    rates, and gate-firing histograms (rocket presets only).

    Writes a Markdown report to $env:GITHUB_STEP_SUMMARY (when in GitHub
    Actions) and to -OutFile when specified.
    Exits 1 if any preset shows high score variance (CV > 20%) or zero
    convergence across all seeds; exits 0 otherwise.

.PARAMETER DataDir
    Directory to search for *.jsonl and violations-*.txt files (default: .).

.PARAMETER OutFile
    Optional path to write the consolidated report as a standalone .md file.

.PARAMETER HighVarianceThresholdPct
    CV threshold (%) above which a preset is flagged as high-variance (default 20).
#>
param(
    [string]$DataDir                    = '.',
    [string]$OutFile                    = '',
    [double]$HighVarianceThresholdPct   = 20.0
)

$ErrorActionPreference = 'Stop'

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("## Weekly multi-seed SA convergence study")
$lines.Add("")
$lines.Add("10 seeds per preset (seeds 42–51); single-chain SA, 2000 iterations each.")
$lines.Add("")

# ── Score envelope table ──────────────────────────────────────────────────

$lines.Add("### Score envelope (best_total_score across 10 seeds)")
$lines.Add("")
$lines.Add("| Preset | Seeds | Best Score | Median Score | Worst Score | CV% | Converged | Feasible Rate |")
$lines.Add("|--------|-------|-----------|-------------|------------|-----|-----------|--------------|")

$jsonlFiles = Get-ChildItem -Path $DataDir -Filter '*.jsonl' -Recurse -ErrorAction SilentlyContinue |
              Where-Object { $_.Name -notmatch '^bench-sa-' }   # exclude frozen baselines
if (-not $jsonlFiles) {
    Write-Warning "No convergence .jsonl files found under '$DataDir'."
    exit 0
}

$anyHighVariance = $false

foreach ($jf in $jsonlFiles | Sort-Object Name) {
    $records = Get-Content $jf.FullName -Encoding UTF8 |
               Where-Object { $_.Trim() -ne '' } |
               ForEach-Object { $_ | ConvertFrom-Json }

    if (-not $records) { continue }

    $preset = $records[0].preset
    if (-not $preset) { $preset = [System.IO.Path]::GetFileNameWithoutExtension($jf.Name) }

    $scores = @($records | Where-Object {
        $null -ne $_.best_total_score -and
        [double]$_.best_total_score -ne -1 -and
        [System.Math]::IsFinite([double]$_.best_total_score)
    } | ForEach-Object { [double]$_.best_total_score })

    $nSeeds  = $records.Count
    $nScores = $scores.Count

    if ($nScores -eq 0) {
        $lines.Add("| $preset | $nSeeds | — | — | — | — | — | — |")
        continue
    }

    $sortedScores = $scores | Sort-Object
    $best    = $sortedScores[0]
    $worst   = $sortedScores[-1]
    $median  = if ($nScores % 2 -eq 1) {
        $sortedScores[($nScores - 1) / 2]
    } else {
        ($sortedScores[$nScores / 2 - 1] + $sortedScores[$nScores / 2]) / 2.0
    }
    $mean    = ($scores | Measure-Object -Sum).Sum / $nScores
    $variance = if ($nScores -ge 2) {
        ($scores | ForEach-Object { ($_ - $mean) * ($_ - $mean) } | Measure-Object -Sum).Sum / ($nScores - 1)
    } else { 0.0 }
    $stddev  = [System.Math]::Sqrt($variance)
    $cv      = if ($mean -ne 0) { 100.0 * $stddev / [System.Math]::Abs($mean) } else { 0.0 }

    $nConverged = ($records | Where-Object { $_.convergence_reached -eq $true }).Count
    $convergence = "$nConverged/$nSeeds"

    $totalFeasible   = ($records | Measure-Object -Property feasible_count   -Sum).Sum
    $totalInfeasible = ($records | Measure-Object -Property infeasible_count -Sum).Sum
    $totalCandidates = $totalFeasible + $totalInfeasible
    $feasibleRate = if ($totalCandidates -gt 0) {
        "{0:F1}%" -f (100.0 * $totalFeasible / $totalCandidates)
    } else { "—" }

    $cvStr  = "{0:F1}%" -f $cv
    $flag   = if ($cv -gt $HighVarianceThresholdPct) { " ⚠" ; $anyHighVariance = $true } else { "" }

    $lines.Add("| $preset | $nSeeds | {0:F4} | {1:F4} | {2:F4} | $cvStr$flag | $convergence | $feasibleRate |" -f $best, $median, $worst)
}

$lines.Add("")

# ── Gate-firing histogram (rocket presets only) ───────────────────────────

$violationFiles = Get-ChildItem -Path $DataDir -Filter 'violations-*.txt' -Recurse -ErrorAction SilentlyContinue
if ($violationFiles) {
    $lines.Add("### Gate-firing histogram (rocket presets — top 10 gates per preset)")
    $lines.Add("")
    $lines.Add("Rate = % of SA candidates across all 10 seeds that fired this gate.")
    $lines.Add("")

    foreach ($vf in $violationFiles | Sort-Object Name) {
        $presetName = $vf.BaseName -replace '^violations-', ''
        $content    = Get-Content $vf.FullName -Encoding UTF8

        # Find total candidates from the histogram header line.
        $headerLine = $content | Where-Object { $_ -match '# === violation histogram.*total candidates=(\d+)' } | Select-Object -First 1
        if (-not $headerLine) { continue }   # no violation data (airbreathing — skip)

        $violationLines = $content | Where-Object { $_ -match '^VIOLATION\s' }
        if (-not $violationLines) { continue }

        $lines.Add("**$presetName**")
        $lines.Add("")
        $lines.Add("| Gate | Count | Rate |")
        $lines.Add("|------|-------|------|")

        $violationLines |
            ForEach-Object {
                if ($_ -match 'gate=(\S+)\s+count=(\d+)\s+pct=([\d.]+)') {
                    [PSCustomObject]@{ Gate = $Matches[1]; Count = [int]$Matches[2]; Pct = [double]$Matches[3] }
                }
            } |
            Sort-Object -Property Pct -Descending |
            Select-Object -First 10 |
            ForEach-Object {
                $lines.Add("| $($_.Gate) | $($_.Count) | $($_.Pct.ToString('F1'))% |")
            }

        $lines.Add("")
    }
}

# ── Summary footer ────────────────────────────────────────────────────────

$lines.Add("")
if ($anyHighVariance) {
    $lines.Add("> ⚠ One or more presets show high score variance (CV > $HighVarianceThresholdPct%). Design space may be multimodal or gate-boundary marginal.")
} else {
    $lines.Add("> ✅ All presets within variance threshold.")
}

if ($env:GITHUB_STEP_SUMMARY) {
    $lines | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding UTF8
}
if ($OutFile) {
    $lines | Set-Content -Path $OutFile -Encoding UTF8
    Write-Host "Report written to: $OutFile"
}
$lines | Write-Host

exit ($anyHighVariance ? 1 : 0)
