# Building from source

[Bahasa Indonesia](../id/building-from-source.md)

## Requirements

- Windows 10/11 x64
- .NET 10 SDK
- Visual Studio 2022 or newer with **Desktop development with C++** (it includes CMake and Ninja)
- Git, and about 6 GB of free disk space
- 16 GB RAM recommended (Chrono translation units are large)

## Native engine

```powershell
./build/build-native.ps1                 # Chrono 10.0.0 + Eigen 3.4.1 + shim → artifacts/native/win-x64
./build/build-native.ps1 -SkipChrono     # rebuild only the shim after editing native/
./build/build-native.ps1 -Jobs 4         # fewer parallel compiles if memory is tight
./build/build-native.ps1 -EnableVehicle:$false   # core only (faster)
```

The script finds MSVC with `vswhere`, clones Chrono and Eigen into `external/`, builds Chrono (Release, shared libraries) with the vehicle module, installs it to `external/chrono-install`, builds `native/`, and copies all DLLs to `artifacts/native/win-x64`. The first build takes 1–2 hours on a 4-core laptop.

`src/Chrono.Net` copies those DLLs into every app's output folder automatically, and packs them under `runtimes/win-x64/native`.

## Managed code

```powershell
dotnet build Chrono.Net.slnx
dotnet test --project tests/Chrono.Net.Tests                                       # physics tests skip if the engine is missing
dotnet test --project tests/Chrono.Net.Tests -- --filter-method "*FreeFall*"      # one test
```

## Screenshots for the docs

```powershell
dotnet run --project tools/Chrono.Net.Screenshots -- docs/images mixer@3 four-bar@1.2
```

This renders the Avalonia Gallery headlessly (Skia) and the plain viewport for each sample.

## NuGet packages

```powershell
./build/pack.ps1                       # → artifacts/packages
./build/pack.ps1 -Publish              # pushes to nuget.org (key from $env:NUGET_API_KEY or your credentials file)
```

## Continuous integration and releases

`.github/workflows/chrono-net.yml` (at the root of the Vibe_Physics repository) runs on every push or pull request
that touches `Chrono.Net/`. It builds Chrono and the shim on `windows-latest`, runs the tests, packs, and uploads the
packages and native DLLs as workflow artifacts. The first run builds Chrono from source (about 2 hours); the install
tree is cached, so later runs rebuild only the shim.

To publish, create a GitHub Release whose tag is `chrono-net-v<version>`, for example `chrono-net-v0.1.0`. The
workflow packs that version and pushes it with the `NUGET_API_KEY` repository secret. A manual run (*Run workflow*,
`publish: true`) does the same. Bump the `Chrono.Net` version pinned in the template projects along with it.

---
Made by Gravicode Studios, led by Kang Fadhil
