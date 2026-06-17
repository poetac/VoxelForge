<#
.SYNOPSIS
    Generate STL topology validation report (#657).

.DESCRIPTION
    Reads the JSONL output from --bench-stl-validation and emits a
    per-preset Markdown summary table showing triangle count, degenerate
    triangle count, manifold-edge status, watertightness, and pass/warn/fail.

    Writes to $env:GITHUB_STEP_SUMMARY (when in GitHub Actions) and to
    -OutFile when specified.

    Exits 1 if any preset has status "fail"; exits 0 otherwise.

.PARAMETER JsonlFile
    Path to the JSONL output from --bench-stl-validation.

.PARAMETER OutFile
    Optional path to write the consolidated report as a standalone .md file.
#>
param(
    [string]$JsonlFile  = 'stl-validation-results.jsonl',
    [string]$OutFile    = ''
)

$ErrorActionPreference = 'Stop'

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("## Nightly STL topology validation")
$lines.Add("")
$lines.Add("Pure-managed binary-STL reader; no admesh or MeshLab CLI (ADR-024).")
$lines.Add("")
$lines.Add("| Preset | Triangles | Degenerate | Non-manifold edges | Watertight | Export (ms) | Status |")
$lines.Add("|--------|-----------|------------|-------------------|-----------|------------|--------|")

if (-not (Test-Path $JsonlFile)) {
    Write-Warning "JSONL file not found: '$JsonlFile'. Report will be empty."
    $lines.Add("| — | — | — | — | — | — | — |")
    $lines | Write-Host
    exit 0
}

$records = Get-Content $JsonlFile -Encoding UTF8 |
           Where-Object { $_.Trim() -ne '' } |
           ForEach-Object { $_ | ConvertFrom-Json }

$anyFail = $false

foreach ($rec in $records | Sort-Object preset) {
    $preset     = $rec.preset
    $triangles  = $rec.triangle_count
    $degen      = $rec.degenerate_count
    $nonMan     = $rec.non_manifold_edge_count
    $watertight = if ($rec.watertight) { 'yes' } else { 'no' }
    $exportMs   = $rec.export_ms
    $status     = $rec.status

    $statusBadge = switch ($status) {
        'pass' { '✅ pass' }
        'warn' { '⚠ warn' }
        'fail' { '❌ fail' }
        default { $status }
    }

    if ($status -eq 'fail') { $anyFail = $true }

    $lines.Add("| $preset | $triangles | $degen | $nonMan | $watertight | $exportMs | $statusBadge |")
}

$lines.Add("")

if ($anyFail) {
    $lines.Add("> ❌ One or more presets failed topology validation. Failing STL files are uploaded as the `stl-failing-*` artifact for inspection.")
} else {
    $nWarn = @($records | Where-Object { $_.status -eq 'warn' }).Count
    if ($nWarn -gt 0) {
        $lines.Add("> ⚠ All presets exported successfully and are watertight, but $nWarn preset(s) have degenerate (zero-area) triangles. PicoGK marching cubes may produce isolated degenerate slivers at coarse voxel sizes; non-manifold / non-watertight defects fail the guardrail rather than warn.")
    } else {
        $lines.Add("> ✅ All presets passed topology validation (manifold, watertight, no degenerate triangles).")
    }
}

if ($env:GITHUB_STEP_SUMMARY) {
    $lines | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding UTF8
}
if ($OutFile) {
    $lines | Set-Content -Path $OutFile -Encoding UTF8
    Write-Host "Report written to: $OutFile"
}
$lines | Write-Host

exit ($anyFail ? 1 : 0)
