# test-hdri-hash-fail.ps1 -- verifies fetch-hdri.ps1 fails loud on hash mismatch.
#
# Simulates a CDN/MITM compromise by stubbing tools/hdri-manifest.json with a
# bogus expected SHA-256, then asserting fetch-hdri.ps1 (a) detects the
# mismatch, (b) deletes the freshly-downloaded file, (c) exits non-zero.
#
# Also verifies the local-tamper-detect path: truncates an existing good file
# by 1 byte, runs fetch-hdri.ps1, asserts the corrupted file is re-fetched
# cleanly and the final on-disk hash matches the manifest.
#
# Usage:
#   .\tools\test-hdri-hash-fail.ps1
#
# Exit 0 = smoke test passed. Exit 1 = smoke test failed (hash verification
# is broken or fetch-hdri.ps1 silently accepted bad content).

$ErrorActionPreference = "Stop"

$repoRoot     = Split-Path -Parent $PSScriptRoot
$hdriDir      = Join-Path (Join-Path (Join-Path $repoRoot "Voxelforge.Renderer") "Assets") "Hdri"
$fetchPath    = Join-Path $PSScriptRoot "fetch-hdri.ps1"
$manifestPath = Join-Path $PSScriptRoot "hdri-manifest.json"
$victimName   = "studio_small.exr"
$victimPath   = Join-Path $hdriDir $victimName

function Invoke-Fetch {
    param([switch]$ForceFetch)
    $invokeArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $fetchPath)
    if ($ForceFetch) { $invokeArgs += "-Force" }
    & powershell @invokeArgs | Out-Null
    return $LASTEXITCODE
}

if (-not (Test-Path $victimPath)) {
    Write-Host "  setup: $victimName not present locally - running fetch-hdri.ps1 first to seed it."
    $exitSetup = Invoke-Fetch
    if ($exitSetup -ne 0 -or -not (Test-Path $victimPath)) {
        Write-Host "  abort: could not seed $victimName (exit $exitSetup). Check network/CDN access."
        exit 1
    }
}

$victimBackup   = "$victimPath.bak"
$manifestBackup = "$manifestPath.bak"
Copy-Item -Path $victimPath -Destination $victimBackup -Force
Copy-Item -Path $manifestPath -Destination $manifestBackup -Force

$testPassed = $false
try {
    # ----- Phase 1: local-tamper-detect branch -----
    $bytes = [System.IO.File]::ReadAllBytes($victimPath)
    $truncated = New-Object byte[] ($bytes.Length - 1)
    [Array]::Copy($bytes, $truncated, $truncated.Length)
    [System.IO.File]::WriteAllBytes($victimPath, $truncated)

    Write-Host "  test1: mutated $victimName (truncated 1 byte). Running fetch-hdri.ps1..."
    $exit1 = Invoke-Fetch
    if ($exit1 -ne 0) {
        Write-Host "  FAIL: fetch-hdri.ps1 returned $exit1 after locally-tampered file. Expected 0 (re-fetched cleanly)."
        exit 1
    }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $expected = $manifest.assets.$victimName.sha256.ToLowerInvariant()
    $actual = (Get-FileHash -Path $victimPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        Write-Host "  FAIL: post-fetch hash $actual does not match manifest $expected."
        exit 1
    }
    Write-Host "  ok1:   local-tamper-detect verified - corrupted file was re-fetched cleanly."

    # ----- Phase 2: CDN-swap-detect branch -----
    $stubManifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $stubManifest.assets.$victimName.sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
    $stubManifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8

    Remove-Item $victimPath -Force

    Write-Host "  test2: manifest stubbed with bogus hash. Running fetch-hdri.ps1 (expect non-zero)..."
    $exit2 = Invoke-Fetch -ForceFetch
    if ($exit2 -eq 0) {
        Write-Host "  FAIL: fetch-hdri.ps1 returned 0 with a deliberately wrong manifest. Hash check is broken."
        exit 1
    }
    if (Test-Path $victimPath) {
        Write-Host "  FAIL: fetch-hdri.ps1 left $victimName in place after hash mismatch. Should have deleted it."
        exit 1
    }
    Write-Host "  ok2:   CDN-swap-detect verified - bad-hash download was rejected and deleted (exit $exit2)."

    $testPassed = $true
} finally {
    if (Test-Path $manifestBackup) {
        Move-Item -Path $manifestBackup -Destination $manifestPath -Force
    }
    if (Test-Path $victimBackup) {
        Move-Item -Path $victimBackup -Destination $victimPath -Force
    }
}

Write-Host ""
if ($testPassed) {
    Write-Host "  PASS: HDRi hash verification smoke test passed."
    exit 0
} else {
    Write-Host "  FAIL: smoke test did not complete."
    exit 1
}
