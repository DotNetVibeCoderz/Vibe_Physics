<#
.SYNOPSIS
  Builds Project Chrono (C++) and the Chrono.Net native shim (chrono_native) for win-x64.

.DESCRIPTION
  1. Fetches Project Chrono and Eigen into ./external if missing.
  2. Configures and builds Chrono (Release, shared libs) and installs it into ./external/chrono-install.
     The full vehicle-models library is skipped; the shim compiles only the models it exposes.
  3. Builds ./native (the extern "C" shim) against the installed Chrono.
  4. Copies all runtime DLLs into ./artifacts/native/win-x64 (consumed by src/Chrono.Net.Native).

.EXAMPLE
  ./build/build-native.ps1                 # full build
  ./build/build-native.ps1 -SkipChrono     # only rebuild the shim
#>
param(
    [string]$ChronoTag = "10.0.0",
    [string]$EigenTag = "3.4.1",
    [switch]$SkipChrono,
    [switch]$EnableVehicle = $true,
    [int]$Jobs = [Environment]::ProcessorCount
)

$ErrorActionPreference = "Stop"
# Forward slashes everywhere: CMake writes these paths verbatim into ChronoConfig.cmake, where "\U" etc. are
# invalid escape sequences.
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path.Replace('\', '/')
$External = "$Root/external"
$ChronoSrc = "$External/chrono"
$EigenDir = "$External/eigen-$EigenTag"
$ChronoBuild = "$External/chrono-build"
$ChronoInstall = "$External/chrono-install"
$ShimBuild = "$External/native-build"
$OutDir = "$Root/artifacts/native/win-x64"

function Import-VsDevEnv {
    if (Get-Command cl.exe -ErrorAction SilentlyContinue) { return }
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $vsPath = & $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vsPath) { throw "Visual Studio with C++ tools not found." }
    $vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat"
    Write-Host "Using MSVC from $vsPath"
    cmd /c "`"$vcvars`" >nul && set" | ForEach-Object {
        if ($_ -match "^(.*?)=(.*)$") { Set-Item -Path "env:$($matches[1])" -Value $matches[2] }
    }
}

Import-VsDevEnv
New-Item -ItemType Directory -Force $External, $OutDir | Out-Null

if (-not (Test-Path $ChronoSrc)) {
    git clone --depth 1 --branch $ChronoTag https://github.com/projectchrono/chrono.git $ChronoSrc
}
if (-not (Test-Path $EigenDir)) {
    $tgz = Join-Path $External "eigen.tar.gz"
    Invoke-WebRequest "https://gitlab.com/libeigen/eigen/-/archive/$EigenTag/eigen-$EigenTag.tar.gz" -OutFile $tgz
    tar xzf $tgz -C $External
    Remove-Item $tgz
}

if (-not $SkipChrono) {
    $vehicle = if ($EnableVehicle) { "ON" } else { "OFF" }
    cmake -S $ChronoSrc -B $ChronoBuild -G Ninja `
        -DCMAKE_BUILD_TYPE=Release `
        -DCMAKE_INSTALL_PREFIX="$ChronoInstall" `
        -DEIGEN3_INCLUDE_DIR="$EigenDir" `
        -DBUILD_DEMOS=OFF -DBUILD_BENCHMARKING=OFF -DBUILD_TESTING=OFF `
        -DCH_ENABLE_MODULE_VEHICLE=$vehicle `
        -DCH_ENABLE_MODULE_VEHICLE_MODELS=OFF `
        -DCH_ENABLE_MODULE_VEHICLE_COSIM=OFF
    if ($LASTEXITCODE) { throw "Chrono configure failed" }
    cmake --build $ChronoBuild --parallel $Jobs
    if ($LASTEXITCODE) { throw "Chrono build failed" }
    cmake --install $ChronoBuild
    if ($LASTEXITCODE) { throw "Chrono install failed" }
}

cmake -S "$Root/native" -B $ShimBuild -G Ninja `
    -DCMAKE_BUILD_TYPE=Release `
    -DCHRONO_SOURCE_DIR="$ChronoSrc" `
    -DChrono_DIR="$ChronoInstall/cmake" `
    -DEIGEN3_INCLUDE_DIR="$EigenDir"
if ($LASTEXITCODE) { throw "Shim configure failed" }
cmake --build $ShimBuild --parallel $Jobs
if ($LASTEXITCODE) { throw "Shim build failed" }

Copy-Item (Join-Path $ShimBuild "chrono_native.dll") $OutDir -Force
Get-ChildItem (Join-Path $ChronoInstall "bin") -Filter "Chrono*.dll" | Copy-Item -Destination $OutDir -Force
Write-Host "Native binaries written to $OutDir" -ForegroundColor Green
Get-ChildItem $OutDir | Format-Table Name, Length
