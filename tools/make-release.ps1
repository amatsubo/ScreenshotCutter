# Builds the distributable zip for ScreenshotCutter.
#
# Steps:
#   1. run the unit tests (skip with -SkipTests)
#   2. publish self-contained + ReadyToRun via the win-x64 profile
#   3. pack the output into dist/ScreenshotCutter-v<version>-win-x64.zip
#   4. print the size and SHA256 for the release notes
#
# The zip contains a single top-level folder so that extracting it does not
# scatter 240+ files into whatever directory the user happened to be in.
#
# Files produced by running the app (settings.json, logs, *.pdb) are excluded,
# so a zip built right after local testing is still clean.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\make-release.ps1
#   powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 -SkipTests

[CmdletBinding()]
param(
    # Skip the test run. Use only when the tests were just run by hand.
    [switch]$SkipTests,

    # Override the version instead of reading it from the csproj.
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot 'src\ScreenshotCutter\ScreenshotCutter.csproj'
$testProjectPath = Join-Path $repoRoot 'tests\ScreenshotCutter.Tests\ScreenshotCutter.Tests.csproj'
$publishDir = Join-Path $repoRoot 'publish\win-x64'
$distDir = Join-Path $repoRoot 'dist'

if (-not (Test-Path $projectPath)) {
    throw "Project not found: $projectPath"
}

# --- version ---------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$csproj = Get-Content -LiteralPath $projectPath -Raw
    $versionNode = $csproj.SelectSingleNode('//PropertyGroup/Version')
    if ($null -eq $versionNode) {
        throw "No <Version> element in $projectPath. Pass -Version instead."
    }
    $Version = $versionNode.InnerText.Trim()
}

Write-Host "ScreenshotCutter v$Version" -ForegroundColor Cyan

# --- tests -----------------------------------------------------------------
if ($SkipTests) {
    Write-Host "[1/4] tests   : skipped" -ForegroundColor DarkYellow
}
else {
    Write-Host "[1/4] tests   : running..." -ForegroundColor Gray
    & dotnet test $testProjectPath --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed. Release aborted."
    }
    Write-Host "[1/4] tests   : passed" -ForegroundColor Green
}

# --- publish ---------------------------------------------------------------
Write-Host "[2/4] publish : building..." -ForegroundColor Gray

# Start from a clean folder so files removed from the project do not linger.
if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

& dotnet publish $projectPath -p:PublishProfile=win-x64 --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed. Release aborted."
}

if (-not (Test-Path (Join-Path $publishDir 'ScreenshotCutter.exe'))) {
    throw "ScreenshotCutter.exe was not produced in $publishDir"
}

Write-Host "[2/4] publish : done" -ForegroundColor Green

# --- pack ------------------------------------------------------------------
$rootName = "ScreenshotCutter"
$zipName = "ScreenshotCutter-v$Version-win-x64.zip"
$zipPath = Join-Path $distDir $zipName

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

# Anything the app writes at run time must not ship inside the zip.
$excludedNames = @('settings.json', 'settings.json.bak', 'settings.json.tmp')
$excludedDirs = @('logs')

Write-Host "[3/4] pack    : $zipName" -ForegroundColor Gray

$publishFull = [System.IO.Path]::GetFullPath($publishDir)
$prefixLength = $publishFull.Length + 1
$added = 0
$skipped = 0

$archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in (Get-ChildItem -LiteralPath $publishFull -Recurse -File)) {
        $relative = $file.FullName.Substring($prefixLength)

        if ($excludedNames -contains $file.Name) { $skipped++; continue }
        if ($file.Extension -eq '.pdb') { $skipped++; continue }

        $firstSegment = ($relative -split '\\')[0]
        if ($excludedDirs -contains $firstSegment) { $skipped++; continue }

        # Zip entries always use forward slashes.
        $entryName = "$rootName/" + ($relative -replace '\\', '/')

        $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $file.FullName,
            $entryName,
            [System.IO.Compression.CompressionLevel]::Optimal)

        $added++
    }
}
finally {
    $archive.Dispose()
}

Write-Host "[3/4] pack    : $added files added, $skipped skipped" -ForegroundColor Green

# --- verify ----------------------------------------------------------------
Write-Host "[4/4] verify  : reopening the archive..." -ForegroundColor Gray

$verify = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entryCount = $verify.Entries.Count
    $hasExe = $null -ne ($verify.Entries | Where-Object { $_.FullName -eq "$rootName/ScreenshotCutter.exe" })
    $hasNotices = $null -ne ($verify.Entries | Where-Object { $_.FullName -eq "$rootName/THIRD-PARTY-NOTICES.txt" })
}
finally {
    $verify.Dispose()
}

if (-not $hasExe) { throw "ScreenshotCutter.exe is missing from the archive." }
if (-not $hasNotices) { throw "THIRD-PARTY-NOTICES.txt is missing from the archive." }
if ($entryCount -ne $added) { throw "Entry count mismatch: expected $added, found $entryCount." }

$info = Get-Item -LiteralPath $zipPath
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash

Write-Host "[4/4] verify  : ok" -ForegroundColor Green
Write-Host ""
Write-Host "Release package" -ForegroundColor Cyan
Write-Host ("  path    : {0}" -f $info.FullName)
Write-Host ("  entries : {0}" -f $entryCount)
Write-Host ("  size    : {0:N1} MB" -f ($info.Length / 1MB))
Write-Host ("  sha256  : {0}" -f $hash)
Write-Host ""
Write-Host "Paste into the release notes:" -ForegroundColor Cyan
Write-Host ("  {0}" -f $zipName)
Write-Host ("  SHA256: {0}" -f $hash)
