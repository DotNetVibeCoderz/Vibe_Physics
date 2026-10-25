# Getting started

[Bahasa Indonesia](../id/getting-started.md) · [Docs index](../../README.md#documentation)

## 1. Install

**From NuGet** (Windows x64):

```powershell
dotnet add package Chrono.Net              # engine + native binaries
dotnet add package Chrono.Net.Rendering    # optional: images, GIFs, software renderer
dotnet add package Chrono.Net.Avalonia     # optional: desktop controls
dotnet add package Chrono.Net.Blazor       # optional: web controls
```

The native libraries live under `runtimes/win-x64/native`. Give your app a runtime identifier (`<RuntimeIdentifier>win-x64</RuntimeIdentifier>`) or run on win-x64 so they are copied next to it.

**From templates:**

```powershell
dotnet new install Chrono.Net.Templates
dotnet new chrono-console  -n MySim      # console + PNG output
dotnet new chrono-avalonia -n MyDesktop  # desktop app with viewport and plot
dotnet new chrono-blazor   -n MyWeb      # web app streaming to WebGL
```

Check the installation:

```csharp
Console.WriteLine(ChronoEngine.IsAvailable);    // true
Console.WriteLine(ChronoEngine.ChronoVersion);  // 10.0.0
```

If `IsAvailable` is false, the native libraries were not found. Set `CHRONO_NET_NATIVE_PATH` to the folder that contains `chrono_native.dll`, or build them yourself (see [Building from source](building-from-source.md)).

## 2. Your first simulation

```csharp
using Chrono.Net;

using var sys = new ChSystem();                       // gravity (0, -9.81, 0)
var mat = sys.Own(new ContactMaterial { Friction = 0.6f, Restitution = 0.2f });

// Ground: a fixed box whose top face is at y = 0
sys.Add(ChBody.CreateBox(new(10, 1, 10), 1000, mat).At(0, -0.5, 0).Fixed());

// A pendulum: a bar hinged to an invisible anchor
var anchor = sys.Add(new ChBody { IsFixed = true, CollisionEnabled = false });
var bar = sys.Add(ChBody.CreateBox(new(1, 0.05, 0.05), 2700, collide: false).At(0.5, 2, 0));
sys.Add(ChJoint.Revolute(bar, anchor, point: new Vec3(0, 2, 0), axis: Vec3.UnitZ));

for (int i = 0; i < 2000; i++)
{
    sys.Step(0.001);
    if (i % 250 == 0) Console.WriteLine($"t={sys.Time:0.00}  tip={bar.Frame.TransformPoint(new(0.5, 0, 0))}");
}
```

Key ideas:

- **Bodies** are created with factories that compute mass and inertia from density (`CreateBox`, `CreateSphere`, …) and are configured fluently (`At`, `Rotated`, `Fixed`, `Colored`, `Named`).
- **Everything is added to a system** with `sys.Add(...)`, which returns the same object so you can keep a reference.
- **Disposal:** disposing the system disposes everything it owns. Use `sys.Own(x)` for materials and functions you create yourself.
- **Units** are SI: meters, kilograms, seconds, newtons, radians.

## 3. See it

```csharp
using Chrono.Net.Rendering;

var world = new RenderWorld(sys);
var camera = new OrbitCamera { Target = new(0.5f, 1.5f, 0), Distance = 5 };
Snapshot.SavePng(world, camera, "pendulum.png");
```

For interactive views use `ChronoViewport` in [Avalonia or Blazor](ui-components.md).

## 4. Next steps

- [Core API guide](guide.md): shapes, materials, every joint and motor, springs with custom laws, applied loads, contacts, snapshots.
- [FEA and vehicles](fea-and-vehicles.md).
- Browse the 16 samples: `chrono list`, `chrono source four-bar`, or the Gallery app.

---
Made by Gravicode Studios, led by Kang Fadhil
