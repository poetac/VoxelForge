<#
.SYNOPSIS
    Diff BenchmarkDotNet JSONL output against committed baselines (#652).

.DESCRIPTION
    Compares each *-bdn.jsonl in -CurrentDir against the most recent matching
    baseline in -BaselineDir. Reports delta for mean_ns (wall-time), alloc_bytes,
    and stddev_ns (spread proxy). Writes a Markdown table to $env:GITHUB_STEP_SUMMARY
    when running in GitHub Actions. Exits 1 if any kernel regresses more than
    -ThresholdPct (default 10%) on mean_ns.

.PARAMETER CurrentDir
    Directory containing the freshly-generated *-bdn.jsonl files
    (typically BenchmarkDotNet.Artifacts/results/ or current/bdn/results/).

.PARAMETER BaselineDir
    Directory containing the committed baseline *-bdn.jsonl files
    (Voxelforge.MicroBenchmarks/baselines/).

.PARAMETER ThresholdPct
    Regression threshold for mean_ns in percent (default 10).
#>
param(
    [string]$CurrentDir  = 'BenchmarkDotNet.Artifacts/results',
    [string]$BaselineDir = 'Voxelforge.MicroBenchmarks/baselines',
    [double]$ThresholdPct = 10.0
)

$ErrorActionPreference = 'Stop'

$summaryLines = [System.Collections.Generic.List[string]]::new()
$summaryLines.Add('## BDN microbench drift vs. committed baseline')
$summaryLines.Add('')
$summaryLines.Add('| Kernel | Baseline mean (ns) | Current mean (ns) | Δ mean | Alloc baseline (B) | Alloc current (B) | Δ alloc | Stddev Δ |')
$summaryLines.Add('|--------|-------------------|------------------|--------|-------------------|------------------|---------|----------|')

$regressions = 0
$checked     = 0

foreach ($current in Get-ChildItem -Path $CurrentDir -Filter '*-bdn.jsonl' -ErrorAction SilentlyContinue) {
    # Strip timestamp to get class name: Foo.Bar.BenchClass-20260525-021500-bdn.jsonl -> Foo.Bar.BenchClass
    $className = $current.BaseName -replace '-\d{8}-\d{6}-bdn$', ''

    $baseline = Get-ChildItem -Path $BaselineDir -Filter "${className}-*-bdn.jsonl" -ErrorAction SilentlyContinue |
                Sort-Object Name |
                Select-Object -Last 1

    if (-not $baseline) {
        $summaryLines.Add("| $className | — | — | — | — | — | — | ⚠ no baseline |")
        continue
    }

    $currentRows  = Get-Content $current.FullName  | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json }
    $baselineRows = Get-Content $baseline.FullName | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json }
    $baselineMap  = @{}
    foreach ($row in $baselineRows) { $baselineMap[$row.bench_name] = $row }

    foreach ($cur in $currentRows) {
        $base = $baselineMap[$cur.bench_name]
        if (-not $base) { continue }

        $deltaMean   = ($cur.mean_ns   - $base.mean_ns)   / $base.mean_ns   * 100
        $deltaStddev = if ($base.stddev_ns -gt 0) { ($cur.stddev_ns - $base.stddev_ns) / $base.stddev_ns * 100 } else { 0 }
        $deltaAlloc  = if ($base.alloc_bytes -and $base.alloc_bytes -gt 0) {
                           ($cur.alloc_bytes - $base.alloc_bytes) / $base.alloc_bytes * 100
                       } else { 0 }

        $flag = if ($deltaMean -gt $ThresholdPct) {
            $regressions++; ' ❌'
        } elseif ($deltaMean -gt ($ThresholdPct / 2)) {
            ' ⚠'
        } else { '' }

        $checked++
        $summaryLines.Add(
            "| $($cur.bench_name) " +
            "| $($base.mean_ns.ToString('G6')) " +
            "| $($cur.mean_ns.ToString('G6')) " +
            "| $($deltaMean.ToString('F1'))%$flag " +
            "| $($base.alloc_bytes) " +
            "| $($cur.alloc_bytes) " +
            "| $($deltaAlloc.ToString('F1'))% " +
            "| $($deltaStddev.ToString('F1'))% |"
        )
    }
}

$summaryLines.Add('')
$summaryLines.Add("**Kernels checked:** $checked | **Regressions (>$($ThresholdPct.ToString('F0'))% mean):** $regressions")

if ($env:GITHUB_STEP_SUMMARY) {
    $summaryLines | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding UTF8
} else {
    $summaryLines | Write-Host
}

if ($checked -eq 0) {
    Write-Error "BDN microbench: 0 kernels compared (empty results dir, renamed benchmarks, or no matching baseline). Drift detection would be blind — failing instead of reporting a false green (#850)."
    exit 1
}

if ($regressions -gt 0) {
    Write-Error "BDN microbench: $regressions kernel(s) regressed > $($ThresholdPct)% on mean_ns."
    exit 1
}

Write-Host "BDN microbench diff: $checked kernels checked, 0 regressions."
