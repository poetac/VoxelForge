<#
.SYNOPSIS
    Generate weekly Pareto frontier characterization report (#655).

.DESCRIPTION
    Reads per-preset JSONL files from -DataDir, groups by preset and
    objective pair, computes the feasible Pareto front size, objective
    ranges (min/max per axis), and the 2D hypervolume indicator (HVI)
    relative to a fixed nadir reference point per pair.

    Writes a Markdown report to $env:GITHUB_STEP_SUMMARY (when in
    GitHub Actions) and to -OutFile when specified.

    Exits 1 if any preset has zero feasible Pareto points across both
    pairs; exits 0 otherwise.

.PARAMETER DataDir
    Directory to search for *.jsonl files (default: .).

.PARAMETER OutFile
    Optional path to write the consolidated report as a standalone .md file.

.PARAMETER NadirIspMass_Obj1
    Nadir reference point for Pair A objective 1 (−Isp_s). Default -200
    (corresponds to 200 s Isp — well below any feasible rocket).

.PARAMETER NadirIspMass_Obj2
    Nadir reference point for Pair A objective 2 (Mass_g). Default 500000
    (500 kg — well above any feasible chamber).

.PARAMETER NadirCostMass_Obj1
    Nadir reference point for Pair B objective 1 (Cost_USD). Default 2000000
    (USD 2 M — above any single-chamber cost).

.PARAMETER NadirCostMass_Obj2
    Nadir reference point for Pair B objective 2 (Mass_g). Default 500000.
#>
param(
    [string]$DataDir                = '.',
    [string]$OutFile                = '',
    [double]$NadirIspMass_Obj1     = -200.0,
    [double]$NadirIspMass_Obj2     = 500000.0,
    [double]$NadirCostMass_Obj1    = 2000000.0,
    [double]$NadirCostMass_Obj2    = 500000.0
)

$ErrorActionPreference = 'Stop'

# ── 2D hypervolume via sweep-line ─────────────────────────────────────────────
# Points must already form the Pareto front (non-dominated); reference nadir
# is the upper-right corner both objectives are minimised against.
# Algorithm: sort by obj1 ascending; sweep obj2, accumulate rectangles.
function Compute-HV2D {
    param(
        [object[]]$Points,     # each has .Obj1 and .Obj2
        [double]$NadirObj1,
        [double]$NadirObj2
    )
    if (-not $Points -or $Points.Count -eq 0) { return 0.0 }

    # Filter points strictly dominated by nadir (must be < nadir on both axes).
    $valid = @($Points | Where-Object { $_.Obj1 -lt $NadirObj1 -and $_.Obj2 -lt $NadirObj2 })
    if ($valid.Count -eq 0) { return 0.0 }

    # Sort ascending by Obj1.
    $sorted = @($valid | Sort-Object -Property Obj1)

    $hv = 0.0
    $prevObj2 = $NadirObj2   # right boundary starts at nadir

    foreach ($p in $sorted) {
        $width  = $NadirObj1 - $p.Obj1
        $height = $prevObj2  - $p.Obj2
        if ($width -gt 0 -and $height -gt 0) {
            $hv += $width * $height
        }
        if ($p.Obj2 -lt $prevObj2) { $prevObj2 = $p.Obj2 }
    }
    return $hv
}

# ── Build report ──────────────────────────────────────────────────────────────

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("## Weekly Pareto frontier characterization")
$lines.Add("")
$lines.Add("NSGA-II, 50 population, 100 generations, seed 42.")
$lines.Add("")

$jsonlFiles = Get-ChildItem -Path $DataDir -Filter '*.jsonl' -Recurse -ErrorAction SilentlyContinue
if (-not $jsonlFiles) {
    Write-Warning "No Pareto .jsonl files found under '$DataDir'."
    exit 0
}

# ── Pair A: Isp/mass ──────────────────────────────────────────────────────────

$lines.Add("### Pair A: performance vs. mass  (−IdealIspVacuum\_s,  Mass\_g)")
$lines.Add("")
$lines.Add("| Preset | Pareto points (feasible) | Best −Isp (s) | Worst −Isp (s) | Min mass (g) | Max mass (g) | HVI |")
$lines.Add("|--------|--------------------------|---------------|----------------|-------------|-------------|-----|")

$anyEmptyFront = $false

foreach ($jf in $jsonlFiles | Sort-Object Name) {
    $records = Get-Content $jf.FullName -Encoding UTF8 |
               Where-Object { $_.Trim() -ne '' } |
               ForEach-Object { $_ | ConvertFrom-Json }

    $preset = if ($records.Count -gt 0 -and $records[0].PSObject.Properties['preset']) {
        $records[0].preset
    } else {
        [System.IO.Path]::GetFileNameWithoutExtension($jf.Name)
    }

    $pairA = @($records | Where-Object {
        $_.PSObject.Properties['pair'] -and $_.pair -eq 'isp_mass' -and $_.feasible -eq $true
    })

    if ($pairA.Count -eq 0) {
        $lines.Add("| $preset | 0 | — | — | — | — | 0 |")
        $anyEmptyFront = $true
        continue
    }

    $points = @($pairA | ForEach-Object {
        $objs = $_.objectives
        [PSCustomObject]@{ Obj1 = [double]$objs[0]; Obj2 = [double]$objs[1] }
    })

    $obj1s  = @($points | ForEach-Object { $_.Obj1 })
    $obj2s  = @($points | ForEach-Object { $_.Obj2 })
    $bestNegIsp  = ($obj1s | Measure-Object -Minimum).Minimum
    $worstNegIsp = ($obj1s | Measure-Object -Maximum).Maximum
    $minMass     = ($obj2s | Measure-Object -Minimum).Minimum
    $maxMass     = ($obj2s | Measure-Object -Maximum).Maximum

    $hv = Compute-HV2D -Points $points -NadirObj1 $NadirIspMass_Obj1 -NadirObj2 $NadirIspMass_Obj2

    $lines.Add("| $preset | $($pairA.Count) | $($bestNegIsp.ToString('F1')) | $($worstNegIsp.ToString('F1')) | $($minMass.ToString('F0')) | $($maxMass.ToString('F0')) | $($hv.ToString('G4')) |")
}

$lines.Add("")

# ── Pair B: cost/mass ─────────────────────────────────────────────────────────

$lines.Add("### Pair B: cost vs. mass  (Cost\_USD,  Mass\_g)")
$lines.Add("")
$lines.Add("| Preset | Pareto points (feasible) | Min cost (USD) | Max cost (USD) | Min mass (g) | Max mass (g) | HVI |")
$lines.Add("|--------|--------------------------|----------------|----------------|-------------|-------------|-----|")

foreach ($jf in $jsonlFiles | Sort-Object Name) {
    $records = Get-Content $jf.FullName -Encoding UTF8 |
               Where-Object { $_.Trim() -ne '' } |
               ForEach-Object { $_ | ConvertFrom-Json }

    $preset = if ($records.Count -gt 0 -and $records[0].PSObject.Properties['preset']) {
        $records[0].preset
    } else {
        [System.IO.Path]::GetFileNameWithoutExtension($jf.Name)
    }

    $pairB = @($records | Where-Object {
        $_.PSObject.Properties['pair'] -and $_.pair -eq 'cost_mass' -and $_.feasible -eq $true
    })

    if ($pairB.Count -eq 0) {
        $lines.Add("| $preset | 0 | — | — | — | — | 0 |")
        $anyEmptyFront = $true
        continue
    }

    $points = @($pairB | ForEach-Object {
        $objs = $_.objectives
        [PSCustomObject]@{ Obj1 = [double]$objs[0]; Obj2 = [double]$objs[1] }
    })

    $obj1s   = @($points | ForEach-Object { $_.Obj1 })
    $obj2s   = @($points | ForEach-Object { $_.Obj2 })
    $minCost = ($obj1s | Measure-Object -Minimum).Minimum
    $maxCost = ($obj1s | Measure-Object -Maximum).Maximum
    $minMass = ($obj2s | Measure-Object -Minimum).Minimum
    $maxMass = ($obj2s | Measure-Object -Maximum).Maximum

    $hv = Compute-HV2D -Points $points -NadirObj1 $NadirCostMass_Obj1 -NadirObj2 $NadirCostMass_Obj2

    $lines.Add("| $preset | $($pairB.Count) | $($minCost.ToString('F0')) | $($maxCost.ToString('F0')) | $($minMass.ToString('F0')) | $($maxMass.ToString('F0')) | $($hv.ToString('G4')) |")
}

$lines.Add("")

# ── Summary footer ────────────────────────────────────────────────────────────

if ($anyEmptyFront) {
    $lines.Add("> ⚠ One or more presets produced an empty feasible Pareto front. The optimizer may not have found feasible candidates within 100 generations — consider increasing population size or checking gate calibration.")
} else {
    $lines.Add("> ✅ All presets produced non-empty feasible Pareto fronts for both objective pairs.")
}

if ($env:GITHUB_STEP_SUMMARY) {
    $lines | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding UTF8
}
if ($OutFile) {
    $lines | Set-Content -Path $OutFile -Encoding UTF8
    Write-Host "Report written to: $OutFile"
}
$lines | Write-Host

exit ($anyEmptyFront ? 1 : 0)
