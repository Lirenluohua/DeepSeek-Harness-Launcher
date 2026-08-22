# dsh vision patch write-back
# Restores the native-image patches into the installed dsh dependency packages.
# Run this after any dsh / dependency upgrade, or from the launcher before it
# starts the web service. Each patched file is backed up once as <file>.bak-vision.
#
# Usage:  powershell -ExecutionPolicy Bypass -File writeback.ps1 [-NpxRoot <path>] [-DshRoot <path>]
#   -NpxRoot  where npx keeps per-run installs (default ~/AppData/Local/npm-cache/_npx)
#   -DshRoot  the launcher's bundled dsh dir, e.g. <AppDir>\dsh (covers the installer build)
param(
    [string]$NpxRoot = (Join-Path $env:LOCALAPPDATA 'npm-cache\_npx'),
    [string]$DshRoot = ''
)
$ErrorActionPreference = 'Stop'
$patchRoot = $PSScriptRoot

# Marker strings that uniquely identify each patch, so we never double-clobber a
# file that already carries it, nor overwrite a genuinely-different version blindly.
$markers = @{
    'dsh-llm-deepseek'  = 'DeepSeek file upload to '
    'dsh-host-apiproxy' = 'resolved.inputModalities ?? model.inputModalities'
}

function Get-Targets([string]$pkg, [string]$subRel) {
    $targets = @()
    if (Test-Path $NpxRoot) {
        Get-ChildItem $NpxRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $targets += (Join-Path $_.FullName "node_modules\@deepseek-ai\$pkg\$subRel")
        }
    }
    if ($DshRoot -ne '' -and (Test-Path $DshRoot)) {
        $targets += (Join-Path $DshRoot "node_modules\@deepseek-ai\$pkg\$subRel")
    }
    return @($targets | Where-Object { Test-Path $_ })
}

function Write-Back([string]$pkg, [string]$subRel) {
    $patch = Join-Path $patchRoot "$pkg\$subRel"
    if (-not (Test-Path $patch)) { Write-Host "  [skip] patch missing: $patch"; return }
    $marker = $markers[$pkg]
    $done = 0
    foreach ($target in (Get-Targets $pkg $subRel)) {
        $cur = Get-Content $target -Raw -ErrorAction SilentlyContinue
        if ($null -ne $cur -and $cur.Contains($marker)) {
            Write-Host "  [ok] already patched: $target"
            $done++
            continue
        }
        $bak = "$target.bak-vision"
        if (-not (Test-Path $bak)) { Copy-Item $target $bak -Force }
        Copy-Item $patch $target -Force
        Write-Host "  [patched] $target"
        $done++
    }
    if ($done -eq 0) { Write-Host "  [warn] no $pkg install found" }
}

Write-Host '=== dsh vision patch write-back ==='
Write-Host "patchRoot: $patchRoot"
Write-Host "npxRoot:   $NpxRoot"
if ($DshRoot -ne '') { Write-Host "dshRoot:   $DshRoot" }
Write-Back 'dsh-llm-deepseek'  'lib\index.js'
Write-Back 'dsh-host-apiproxy' 'lib\index.js'
Write-Host 'done.'
