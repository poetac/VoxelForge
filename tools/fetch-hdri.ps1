# fetch-hdri.ps1 -- downloads CC0 HDRi environment maps for voxelforge-render.
#
# Team V Wave 1 (2026-05-05).
# Source: Polyhaven (https://polyhaven.com) -- all assets CC0 licensed.
# Files are downloaded to Voxelforge.Renderer/Assets/Hdri/ at 1K resolution
# (~0.5-1 MB per .exr, well under the 5 MB per-file repo limit).
#
# Usage:
#   .\tools\fetch-hdri.ps1              # download all three maps
#   .\tools\fetch-hdri.ps1 -Force       # re-download even if already present
#
# CI note: HDRi files are gitignored. If this script is not run, voxelforge-render
# falls back to Blender's bundled studio.exr (or grey-blue if that is also absent).
# CI render jobs are informational, not gating -- mirror Team C's SU2 pattern.
#
# Security: each downloaded .exr is verified against the SHA-256 pinned in
# tools/hdri-manifest.json. A CDN compromise, MITM, or upstream-account
# takeover that swaps content will fail loud (file deleted, non-zero exit).

param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$repoRoot     = Split-Path -Parent $PSScriptRoot
$outputDir    = Join-Path (Join-Path (Join-Path $repoRoot "Voxelforge.Renderer") "Assets") "Hdri"
$manifestPath = Join-Path $PSScriptRoot "hdri-manifest.json"

if (-not (Test-Path $manifestPath)) {
    throw "Manifest not found at $manifestPath. Cannot verify downloads without it."
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

$baseUrl = "https://dl.polyhaven.org/file/ph-assets/HDRIs/$($manifest.format)/$($manifest.resolution)"

$failures = 0

foreach ($fileName in $manifest.assets.PSObject.Properties.Name) {
    $entry    = $manifest.assets.$fileName
    $slug     = $entry.slug
    $expected = $entry.sha256.ToLowerInvariant()
    $outFile  = Join-Path $outputDir $fileName
    $url      = "$baseUrl/${slug}_$($manifest.resolution).$($manifest.format)"

    if ((Test-Path $outFile) -and -not $Force) {
        # Verify the existing file still matches the manifest -- guards against a
        # locally tampered file as well as a fresh download.
        $actual = (Get-FileHash -Path $outFile -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -eq $expected) {
            Write-Host "  [skip] $fileName already present (sha256 verified)"
            continue
        }
        Write-Warning "  [warn] $fileName present but hash mismatch -- re-downloading"
        Remove-Item $outFile -Force
    }

    Write-Host "  [fetch] $fileName from $url"
    try {
        Invoke-WebRequest -Uri $url -OutFile $outFile -UseBasicParsing
    } catch {
        Write-Warning "  [fail]  Could not download $fileName : $_"
        Write-Warning "  voxelforge-render will use the grey-blue fallback for this map."
        $failures++
        continue
    }

    $actual = (Get-FileHash -Path $outFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        Remove-Item $outFile -Force
        Write-Error ("[hash mismatch] $fileName`n" +
                     "  expected: $expected`n" +
                     "  actual  : $actual`n" +
                     "  file deleted. Refusing to use unverified HDRi content.`n" +
                     "  If the upstream asset was deliberately updated, re-pin the hash in " +
                     "tools/hdri-manifest.json after manual review.")
        $failures++
        continue
    }

    $sizeMb = [math]::Round((Get-Item $outFile).Length / 1MB, 2)
    Write-Host "  [ok]    $fileName ($sizeMb MB, sha256 verified)"
}

Write-Host ""
if ($failures -gt 0) {
    Write-Host "HDRi fetch finished with $failures failure(s). Files in: $outputDir"
    exit 1
}

Write-Host "HDRi fetch complete. Files in: $outputDir"
Write-Host "Run 'dotnet build Voxelforge.Renderer' to copy them to the output directory."
