# Build script to generate all 3 release packages for MangaMeYeah v0.3.5
# 1. Windows Installer (MangaMeYeah-v0.3.5-Setup.exe)
# 2. Portable ZIP (MangaMeYeah-v0.3.5-win-x64-portable.zip)
# 3. Source code ZIP (MangaMeYeah-v0.3.5-source.zip)

param (
    [string]$Version = "0.3.5"
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
Set-Location $rootDir

Write-Host "=== Building MangaMeYeah v$Version Release Packages ===" -ForegroundColor Cyan

# Ensure release folder exists
$releaseDir = Join-Path $rootDir "release"
if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
}

# 1. dotnet publish single-file win-x64
Write-Host "[1/3] Publishing single-file win-x64 binary..." -ForegroundColor Yellow
$publishDir = Join-Path $rootDir "publish\win-x64"
dotnet publish "$rootDir\src\MangaMeYeah\MangaMeYeah.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $publishDir

# 2. Create Portable ZIP
Write-Host "[2/3] Creating Portable ZIP..." -ForegroundColor Yellow
$portableTemp = Join-Path $rootDir "temp_portable\MangaMeYeah"
if (Test-Path $portableTemp) { Remove-Item -Recurse -Force $portableTemp }
New-Item -ItemType Directory -Path $portableTemp -Force | Out-Null

Copy-Item "$publishDir\MangaMeYeah.exe" -Destination $portableTemp
Copy-Item "$rootDir\README.md" -Destination $portableTemp
Copy-Item "$rootDir\LICENSE" -Destination $portableTemp
Copy-Item "$rootDir\HISTORY.md" -Destination $portableTemp
Copy-Item "$rootDir\docs" -Destination $portableTemp -Recurse

$portableZip = Join-Path $releaseDir "MangaMeYeah-v$Version-win-x64-portable.zip"
if (Test-Path $portableZip) { Remove-Item -Force $portableZip }
Compress-Archive -Path "$rootDir\temp_portable\MangaMeYeah" -DestinationPath $portableZip -CompressionLevel Optimal
Remove-Item -Recurse -Force "$rootDir\temp_portable"
Write-Host " -> Created: $portableZip ($([Math]::Round((Get-Item $portableZip).Length / 1MB, 2)) MB)" -ForegroundColor Green

# 3. Compile Windows Installer using Inno Setup
Write-Host "[3/3] Compiling Inno Setup Windows Installer..." -ForegroundColor Yellow
$isccPaths = @(
    "C:\Users\emmma\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    & $iscc "$rootDir\installer\MangaMeYeah.iss"
    $installerExe = Join-Path $releaseDir "MangaMeYeah-v$Version-Setup.exe"
    Write-Host " -> Created: $installerExe ($([Math]::Round((Get-Item $installerExe).Length / 1MB, 2)) MB)" -ForegroundColor Green
} else {
    Write-Warning "ISCC.exe not found. Inno Setup installer skipped."
}

# 4. Create Source code ZIP
Write-Host "[4/4] Creating Source code ZIP from Git..." -ForegroundColor Yellow
$sourceZip = Join-Path $releaseDir "MangaMeYeah-v$Version-source.zip"
if (Test-Path $sourceZip) { Remove-Item -Force $sourceZip }
git archive --format=zip --prefix="MangaMeYeah-$Version/" HEAD -o $sourceZip
Write-Host " -> Created: $sourceZip ($([Math]::Round((Get-Item $sourceZip).Length / 1KB, 2)) KB)" -ForegroundColor Green

Write-Host "`nAll release packages ready in $releaseDir :" -ForegroundColor Cyan
Get-ChildItem $releaseDir | Format-Table Name, Length, LastWriteTime
