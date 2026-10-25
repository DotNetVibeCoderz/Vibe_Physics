# Architecture

[Bahasa Indonesia](../id/architecture.md)

```
 ┌───────────────────────── apps ──────────────────────────┐
 │ Gallery (Avalonia, Blazor) · Simulator · chrono CLI      │
 └──────────────┬───────────────────────────┬──────────────┘
   Chrono.Net.Avalonia            Chrono.Net.Blazor (three.js)
          └──────── Chrono.Net.Rendering ────────┘   Chrono.Net.Samples
                           │
                      Chrono.Net  (managed API, SafeHandles, LibraryImport)
                           │  flat C ABI — native/include/chrono_native*.h
                   chrono_native.dll (C++17 shim, compiled vehicle models)
                           │
        Project Chrono 10: Chrono_core · Chrono_vehicle · Bullet · Eigen
```

## Native boundary

- **Opaque handles.** Each native object is a heap holder with one `std::shared_ptr`. Managed wrappers own it through a `SafeHandle`, so lifetime is deterministic (`Dispose`) and safe (finalizer). Releasing a handle drops one reference; Chrono keeps objects alive while a system uses them.
- **No exceptions across the ABI.** Every entry point catches C++ exceptions, returns a status or a null handle, and stores a thread-local message. The managed side throws `ChronoException` with that message.
- **Blittable math.** `Vec3`, `Quat`, `Frame`, `BodyState`, `ContactInfo` and `VisualShape` have the same layout in C and C#, and are passed by value or by pointer with no marshalling.
- **Callbacks** (`ChFunction.FromDelegate`, spring force laws, torque laws) use `delegate* unmanaged[Cdecl]` trampolines plus a `GCHandle` to the target object. No delegates are marshalled and no thunks are allocated per call.
- **Vehicle models** come from Chrono's own model sources, compiled directly into `chrono_native` (only the six exposed models), which keeps the native build shorter.

## Why no Rust?

The brief allows Rust "if needed for performance". Chrono's API is C++ (templates, `shared_ptr`, virtual callbacks), so a C++ shim is the natural and fastest boundary. Adding Rust would mean a second FFI hop and no gain, so it was evaluated and not used.

## Performance

- `ChSystem.Step(dt, count)` runs the whole loop natively when no step events are attached (one P/Invoke for thousands of steps).
- `GetBodyStates()` fetches pose, velocity and visual frame of **all** bodies in one call into a reused buffer. Renderers, CSV export and Blazor streaming all use it.
- `RenderWorld` caches unit meshes per primitive type and only updates matrices per frame.
- The software rasterizer splits the screen into bands processed in parallel, and shades per vertex.
- Blazor sends 28 bytes per body per frame as binary (`byte[]` → `Uint8Array`), not JSON.
- Measure on your machine with `chrono bench --bodies 1000`.

## Repository layout

| Folder | Contents |
|---|---|
| `native/` | C ABI headers and C++ shim, CMake project |
| `src/` | Packages: Chrono.Net, Rendering, Samples, Avalonia, Blazor |
| `tools/` | CLI, Simulator, headless screenshot tool |
| `samples/` | Gallery apps |
| `templates/` | `dotnet new` templates |
| `tests/` | xUnit v3 tests (math, physics against analytic results, FEA, scenes) |
| `build/` | `build-native.ps1`, `pack.ps1` |
| `external/` | Chrono, Eigen and build trees (git-ignored) |

---
Made by Gravicode Studios, led by Kang Fadhil
