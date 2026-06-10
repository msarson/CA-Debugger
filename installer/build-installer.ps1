# Build the CA Debugger installer.
#
# Stages a separately-built addin DLL for each Clarion version (the addin links
# against version-specific IDE assemblies, so one build per version is required),
# stages the version-independent ClarionDbg engine once, then compiles the Inno
# Setup script into installer\output\.
#
# Usage: .\build-installer.ps1 [-Versions 12,11,10] [-NoBuild]
#
#   -Versions   Which Clarion versions to include (default: all that are installed).
#   -NoBuild    Skip MSBuild; (re)stage from existing build output and compile only.
#
# Requires: Visual Studio 2022 / MSBuild, Inno Setup 6, and the Clarion install
# root for every version being built (its \bin\ICSharpCode.*.dll are referenced
# at compile time).

param(
    [int[]]$Versions,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$InstallerDir = $PSScriptRoot
$RepoRoot     = Split-Path -Parent $InstallerDir
$AddinProj    = Join-Path $RepoRoot "src\ClarionDebugger.Addin\ClarionDebugger.Addin.csproj"
$AddinOut     = Join-Path $RepoRoot "src\ClarionDebugger.Addin\bin\Debug"
$EngineProj   = Join-Path $RepoRoot "src\ClarionDbg.Cli\ClarionDbg.Cli.csproj"
$EngineOut    = Join-Path $RepoRoot "src\ClarionDbg.Cli\bin\Debug\net48"
$StageDir     = Join-Path $InstallerDir "staging"
$OutputDir    = Join-Path $InstallerDir "output"
$IssFile      = Join-Path $InstallerDir "CA-Debugger.iss"

# Clarion install roots per version (first existing root wins) — mirrors deploy-addin.ps1.
$VersionRoots = @{
    12 = @("C:\Clarion12", "C:\Clarion12d")
    11 = @("D:\Clarion11.1EE", "C:\Clarion11-13372", "C:\Clarion11")
    10 = @("C:\Clarion10", "C:\Clarion10v8")
}

function Resolve-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -requires Microsoft.Component.MSBuild `
                            -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($found -and (Test-Path $found)) { return $found }
    }
    throw "MSBuild.exe not found. Install Visual Studio 2022 with the MSBuild component."
}

function Resolve-ISCC {
    foreach ($p in @("C:\Program Files (x86)\Inno Setup 6\ISCC.exe", "C:\Program Files\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $p) { return $p }
    }
    throw "Inno Setup 6 (ISCC.exe) not found. Install from https://jrsoftware.org/isdl.php"
}

function Get-VersionRoot([int]$ver) {
    return @($VersionRoots[$ver]) | Where-Object { Test-Path (Join-Path $_ "bin\ICSharpCode.Core.dll") } | Select-Object -First 1
}

$MSBuild = Resolve-MSBuild
$ISCC    = Resolve-ISCC

# Decide which versions to build: requested, or every one with an install root present.
if (-not $Versions) { $Versions = @(12, 11, 10) }
$Build = @()
foreach ($v in $Versions) {
    $root = Get-VersionRoot $v
    if ($root) { $Build += [pscustomobject]@{ Ver = $v; Root = $root } }
    else { Write-Host "SKIP Clarion $v — no install root with IDE assemblies found." -ForegroundColor DarkYellow }
}
if (-not $Build) { throw "No Clarion versions available to build. Need at least one install root." }

Write-Host "=== CA Debugger Installer Build ===" -ForegroundColor Cyan
Write-Host "MSBuild:    $MSBuild"
Write-Host "Inno Setup: $ISCC"
Write-Host "Versions:   $(( $Build | ForEach-Object { $_.Ver }) -join ', ')"
Write-Host ""

# Clean staging.
if (Test-Path $StageDir) { Remove-Item $StageDir -Recurse -Force }
New-Item -ItemType Directory -Path $StageDir | Out-Null

# --- Engine (version-independent) ---
if (-not $NoBuild) {
    Write-Host "Building ClarionDbg engine..." -ForegroundColor Yellow
    & $MSBuild $EngineProj /t:Build /restore /p:Configuration=Debug /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "Engine build failed." }
}
$EngineStage = Join-Path $StageDir "engine"
New-Item -ItemType Directory -Path $EngineStage | Out-Null
foreach ($e in @("ClarionDbg.exe", "ClarionDbg.pdb", "ClarionDbg.Core.dll", "ClarionDbg.Core.pdb")) {
    Copy-Item (Join-Path $EngineOut $e) (Join-Path $EngineStage $e) -Force
}
Write-Host "  staged engine ($EngineStage)" -ForegroundColor Green

# --- Addin, once per version ---
function Stage-Addin([string]$dest) {
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    foreach ($f in @(
        "ClarionDebugger.dll", "ClarionDebugger.pdb", "ClarionDebugger.addin",
        "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "Microsoft.Web.WebView2.Wpf.dll"
    )) {
        Copy-Item (Join-Path $AddinOut $f) (Join-Path $dest $f) -Force
    }
    # WebView2 native loader: at the addin root and under runtimes\win-x86\native (mirrors deploy-addin.ps1).
    $loader = Join-Path $AddinOut "runtimes\win-x86\native\WebView2Loader.dll"
    Copy-Item $loader (Join-Path $dest "WebView2Loader.dll") -Force
    $nativeDir = Join-Path $dest "runtimes\win-x86\native"
    New-Item -ItemType Directory -Path $nativeDir -Force | Out-Null
    Copy-Item $loader (Join-Path $nativeDir "WebView2Loader.dll") -Force
    # Debugger pad HTML.
    $termDir = Join-Path $dest "Terminal"
    New-Item -ItemType Directory -Path $termDir -Force | Out-Null
    Copy-Item (Join-Path $AddinOut "Terminal\debugger.html") (Join-Path $termDir "debugger.html") -Force
}

foreach ($b in $Build) {
    if (-not $NoBuild) {
        Write-Host "Building addin for Clarion $($b.Ver) ($($b.Root))..." -ForegroundColor Yellow
        & $MSBuild $AddinProj /t:Rebuild /restore /p:Configuration=Debug /p:ClarionRoot=$($b.Root) /v:minimal /nologo
        if ($LASTEXITCODE -ne 0) { throw "Addin build failed for Clarion $($b.Ver)." }
    }
    $dest = Join-Path $StageDir "C$($b.Ver)"
    Stage-Addin $dest
    Write-Host "  staged Clarion $($b.Ver) addin ($dest)" -ForegroundColor Green
}

# --- Compile installer ---
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir | Out-Null }
Write-Host "`nCompiling installer..." -ForegroundColor Yellow
& $ISCC $IssFile
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }

$exe = Get-ChildItem $OutputDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "`n=== Build Complete ===" -ForegroundColor Green
if ($exe) {
    Write-Host "Installer: $($exe.FullName)"
    Write-Host "Size: $([math]::Round($exe.Length / 1MB, 2)) MB"
}
