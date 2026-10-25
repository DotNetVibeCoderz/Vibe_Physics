# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What Chrono.Net is

A .NET 10 wrapper for Project Chrono 10.0.0 (C++ multiphysics engine), with Avalonia and Blazor components, a Gallery (desktop + web), a Simulator, a CLI and `dotnet new` templates. Requirements come from `Chrono.Net.txt` (brief, Bahasa Indonesia) and `features.md` (Chrono feature scope). Roadmap: `PLAN.md`; update `Progress.md` as work lands.

## Commands

```powershell
./build/build-native.ps1                # builds Chrono (external/, git-ignored) + native shim → artifacts/native/win-x64 (1–2 h first time)
./build/build-native.ps1 -SkipChrono    # rebuild only native/ after editing the shim
./build/build-native.ps1 -Jobs 4        # use when RAM is tight (Chrono TUs reach ~1.8 GB each)
dotnet build Chrono.Net.slnx
dotnet test --project tests/Chrono.Net.Tests                                    # MTP runner (global.json opts in)
dotnet test --project tests/Chrono.Net.Tests -- --filter-method "*FreeFall*"   # single test
dotnet run --project tools/Chrono.Net.Cli -- list | run <sample> | render <sample> --out x.png
dotnet run --project samples/Chrono.Net.Gallery.Avalonia
dotnet run --project samples/Chrono.Net.Gallery.Blazor
dotnet run --project tools/Chrono.Net.Screenshots -- docs/images mixer@3        # headless README/doc screenshots (also appgen=<dir>, appgen-new, appgen-settings)
dotnet run --project tools/Chrono.Net.AppGen                                   # ChronoAppGen IDE
dotnet run --project tools/Chrono.Net.AppGen -- --verify-templates <dir>       # generate + build + --selftest every project template
dotnet run --project tools/Chrono.Net.AppGen -- --agent "<prompt>" --project <dir> --provider DeepSeek   # one headless Jack turn
./build/pack.ps1   [-Publish]           # NuGet packages → artifacts/packages; never pass -Publish without the user's explicit OK
```

Physics tests use `Assert.SkipUnless(ChronoEngine.IsAvailable, …)`, so they skip (not fail) when the native DLL is missing.

## Architecture (big picture)

- `native/` — flat `extern "C"` ABI (`include/chrono_native.h`, `chrono_native_vehicle.h`) over Chrono. Every object is a heap holder with one `std::shared_ptr` (`src/chrono_native_internal.h`); exceptions never cross the ABI (status/NULL + thread-local `chn_last_error`). Vehicle model sources (hmmwv, sedan, gator, uaz, citybus, m113) are compiled straight from `external/chrono/src/chrono_models` into the shim; Chrono itself is built with `CH_ENABLE_MODULE_VEHICLE_MODELS=OFF`.
- `src/Chrono.Net` — `Interop/NativeMethods.cs` (LibraryImport, SafeHandles in `Handles.cs`, `NativeLibraryLoader` preloads `Chrono_*.dll` from the app dir / `runtimes/{rid}/native` / `CHRONO_NET_NATIVE_PATH`). Blittable structs (`Vec3`, `Quat` with E0 = scalar, `Frame`, `BodyState`, `ContactInfo`, `VisualShape`) must stay layout-identical to the C structs. Callbacks use `delegate* unmanaged` trampolines + `GCHandle`. `ChSystem` mirrors the native body list order — `GetBodyStates()` index i == `Bodies[i]`; bodies created natively (vehicles, terrain) are picked up by `SyncBodiesFromNative()`. Null material/function args pass `MaterialHandle.Null` / `FunctionHandle.Null`, never C# null.
- `src/Chrono.Net.Rendering` — `RenderWorld` (render items built from each body's Chrono visual shapes; per-frame bulk state read), `OrbitCamera` (Y-up or Z-up), `SoftwareRenderer` (CPU rasterizer used by Avalonia, CLI and screenshots), PNG/GIF encoders. Ground = static thin wide box along the up axis (`ClassifyGround`).
- `src/Chrono.Net.Samples` — `Sample`/`SampleRun`/`SampleCatalog`; sample sources in `Catalog/` are embedded as resources (`sample:<FileName>`) so galleries show the real code. New samples: subclass `Sample` in `Catalog/` — discovered by reflection.
- `src/Chrono.Net.Avalonia` (viewport, plot, `SimulationHost`, `Themes/ChronoTheme.axaml` + fonts) and `src/Chrono.Net.Blazor` (three.js viewport in `wwwroot/chrono-viewport.js`, binary frame push, `SimulationLoop`).
- `tools/Chrono.Net.AppGen` (ChronoAppGen, assembly `ChronoAppGen`) — code-built Avalonia IDE (`Views/MainWindow*.cs`, AvaloniaEdit + TextMate) with Jack: `Ai/JackAgent.cs` wraps each provider's `IChatClient` (`Ai/ProviderCatalog.cs`: Anthropic SDK for Claude, OpenAI client for OpenAI/Azure `/openai/v1/`/Gemini/Ollama/DeepSeek) with Semantic Kernel's `UseKernelFunctionInvocation()`; plugins in `Ai/Plugins` act through `IIdeHost` (window or `--agent` console host). Settings are `App.config` → `ChronoAppGen.dll.config` via `Settings/AppSettings.cs`; keep keys out of the source `App.config`. Templates: `Projects/TemplateCatalog.*.cs` (scene body only) + `ProjectGenerator` (shared shell with `--selftest`); run `--verify-templates` after changing either. Inside `Chrono.Net.*` namespaces write `global::Avalonia.` for qualified Avalonia names.
- Conventions: Chrono cylinders/capsules/cones are along local Z and centered; revolute/prismatic/cylindrical joints act on frame Z (`Frame.AlongAxis`); angle/position motors are relative to the start pose (laws should start at 0); vehicle worlds are Z-up, everything else Y-up.
- Inside `namespace Chrono.Net.*`, `Color` resolves to `Chrono.Net.Color` — UI code aliases `using Color = global::Avalonia.Media.Color;` **inside** the namespace.

## Documentation requirements

- Docs are bilingual: `docs/en/*` and `docs/id/*` mirror each other; `README.md` / `README.id.md`. Include screenshots from `docs/images`.
- Credit in docs and apps: "Made by Gravicode Studios, led by Kang Fadhil" / "Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil" (`ChronoEngine.Credits` / `CreditsId`).
- UI work uses the `frontend-design` skill; the established look is the "instrument bench" palette in `ChronoTheme.axaml` / Blazor `app.css` (Barlow Condensed display, JetBrains Mono data, amber `#F2A33A` signal, teal `#3CC8B4` traces).

## Secrets and assets

- NuGet API key: CI uses the `NUGET_API_KEY` repository secret (`.github/workflows/chrono-net.yml` at the Vibe_Physics repo root publishes when a GitHub Release is published for a `chrono-net-v<version>` tag). Locally `build/pack.ps1 -Publish` reads `$env:NUGET_API_KEY`, else `C:\Users\mifma\Documents\CodeSandbox\PackageCredentials.txt`; never copy the key into the repo.
- Templates pin `Chrono.Net` 0.1.0 in their csproj files — bump them together with `<Version>` in `Directory.Build.props`.
- MCP servers (`.mcp.json`): **rodin** (image/3D generation, used for `assets/icon.png`) and **blender**.
- Optional supporting libraries from the brief (RLNet, Visual.Net, ThreeNet, GravicodeScience, HF.Net, MediaPipeNet, IoTComNet, TorchSharp, OpenCvSharp, ML.NET) are not integrated yet — see PLAN.md phase 2.
