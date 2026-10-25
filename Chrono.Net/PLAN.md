# Chrono.Net — Roadmap / Rencana Pengembangan

Made by Gravicode Studios, led by Kang Fadhil · Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil

Status legend: ✅ done · 🟡 in progress · ⬜ planned

## Phase 1 — Foundation (v0.1)

| Area | Item | Status |
|---|---|---|
| Native | Build pipeline (Chrono 10.0.0 + Eigen + shim, `build/build-native.ps1`) | ✅ |
| Native | C ABI: system, bodies, shapes, materials NSC/SMC, contacts report, state save/restore | ✅ |
| Native | Joints (12 kinds), distance, gear, rack-pinion, 6 motor types, TSDA/RSDA with managed callbacks, limits, reactions | ✅ |
| Native | FEA: Euler beams, ANCF cables, node loads/fixing/attachment | ✅ |
| Native | Vehicles: HMMWV, Sedan, Gator, UAZBUS, CityBus, M113; rigid + SCM terrain | ✅ |
| Managed | `Chrono.Net` API, JSON scenes, AOT-compatible interop | ✅ |
| Rendering | Render model, orbit camera, software rasterizer with shadows, PNG/GIF | ✅ |
| UI | Avalonia controls + theme; Blazor WebGL viewport, plots, loop | ✅ |
| Apps | Gallery (Avalonia + Blazor), Simulator, CLI, templates | ✅ |
| Apps | ChronoAppGen IDE + Jack (Semantic Kernel, 6 LLM providers, 23 verified templates) | ✅ |
| Quality | xUnit v3 tests (analytic checks: free fall, pendulum period, spring frequency, cantilever deflection) | ✅ |
| Docs | Bilingual README + docs (EN/ID), screenshots, icon | ✅ |
| Release | NuGet packages (win-x64 native), GitHub Actions build/test/pack; publish via GitHub Release `chrono-net-v*` | 🟡 CI added, first publish pending |

## Phase 2 — Breadth (v0.2–0.4)

- Robots from `Chrono_models_robot`: Curiosity, Viper, Turtlebot, RoboSimian, industrial arms.
- FEA: Reissner/ANCF shells, tetrahedral/hexahedral solids, plastic materials, contact surfaces, modal analysis (Chrono::Modal, needs Spectra).
- More collision shapes in the API: rounded box/cylinder, barrel, heightfield helper, convex decomposition.
- Hydraulic actuators, conveyors, feeders, shafts and powertrain components.
- Serialization (ChArchive JSON/XML) and checkpoint files.
- Sensors (camera, LiDAR, GPS, IMU) via Chrono::Sensor (needs OptiX/CUDA) or a managed implementation for non-GPU sensors.
- Supporting libraries: RLNet (reinforcement learning environments built on `SaveState`/`RestoreState` and `SetpointFunction`), Visual.Net charts, GravicodeScience for analysis, IoTComNet for digital twins.
- Linux x64 and macOS arm64 native builds (CI currently builds win-x64 only).

## Phase 3 — Scale (v0.5+)

- Chrono::FSI (SPH) and Chrono::DEM on CUDA GPUs, Chrono::Multicore (needs Thrust/Blaze).
- FMI/FMU co-simulation, preCICE, ROS 2 bridge, SynChrono distributed simulation.
- CAD import (OpenCASCADE/STEP), Blender asset pipeline using the Blender MCP server.
- GPU viewport for Avalonia (OpenGL/Vulkan) for very large scenes.
