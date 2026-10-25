<#
.SYNOPSIS
  Packs all Chrono.Net NuGet packages into ./artifacts/packages, optionally pushing them to nuget.org.

.DESCRIPTION
  Requires native binaries in ./artifacts/native/win-x64 (run build/build-native.ps1 first) so that the
  Chrono.Net package ships runtimes/win-x64/native.

  -Publish pushes every package. The API key comes from the NUGET_API_KEY environment variable (GitHub Actions
  passes the repository secret this way) or, locally, from a credentials file outside the repository. The key is
  never stored in the repo. Nothing is pushed without -Publish.

.EXAMPLE
  ./build/pack.ps1
  ./build/pack.ps1 -Version 0.1.1 -Publish
#>
param(
    [string]$Version = "",
    [switch]$Publish,
    [string]$CredentialsFile = "C:\Users\mifma\Documents\CodeSandbox\PackageCredentials.txt",
    [string]$Source = "https://api.nuget.org/v3/index.json"
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$Out = Join-Path $Root "artifacts/packages"
New-Item -ItemType Directory -Force $Out | Out-Null

if (-not (Test-Path (Join-Path $Root "artifacts/native/win-x64/chrono_native.dll"))) {
    throw "Native binaries missing. Run ./build/build-native.ps1 first."
}

$projects = @(
    "src/Chrono.Net/Chrono.Net.csproj",
    "src/Chrono.Net.Rendering/Chrono.Net.Rendering.csproj",
    "src/Chrono.Net.Samples/Chrono.Net.Samples.csproj",
    "src/Chrono.Net.Avalonia/Chrono.Net.Avalonia.csproj",
    "src/Chrono.Net.Blazor/Chrono.Net.Blazor.csproj",
    "tools/Chrono.Net.Cli/Chrono.Net.Cli.csproj",
    "templates/Chrono.Net.Templates/Chrono.Net.Templates.csproj"
)

$versionArg = if ($Version) { "-p:Version=$Version" } else { "" }
foreach ($p in $projects) {
    Write-Host "Packing $p" -ForegroundColor Cyan
    dotnet pack (Join-Path $Root $p) -c Release -o $Out $versionArg
    if ($LASTEXITCODE) { throw "Pack failed: $p" }
}
Get-ChildItem $Out -Filter *.nupkg | Format-Table Name, Length

if ($Publish) {
    $key = $env:NUGET_API_KEY
    if (-not $key) {
        if (-not (Test-Path $CredentialsFile)) { throw "Set NUGET_API_KEY or provide a credentials file ($CredentialsFile not found)." }
        # The file may hold several entries; take the first token that looks like a NuGet API key (oy2…).
        $content = Get-Content $CredentialsFile -Raw
        $match = [regex]::Match($content, "oy2[a-z0-9]{40,}")
        if (-not $match.Success) { throw "No NuGet API key found in $CredentialsFile" }
        $key = $match.Value
    }
    foreach ($pkg in Get-ChildItem $Out -Filter *.nupkg) {
        if ($Version -and $pkg.Name -notmatch [regex]::Escape($Version)) { continue }
        dotnet nuget push $pkg.FullName --api-key $key --source $Source --skip-duplicate
        if ($LASTEXITCODE) { throw "Push failed: $($pkg.Name)" }
    }
}
