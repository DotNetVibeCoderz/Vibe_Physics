# FEA and vehicles

[Bahasa Indonesia](../id/fea-and-vehicles.md) · [Core API guide](guide.md)

## Finite elements (Chrono::FEA)

```csharp
using Chrono.Net.Fea;

using var sys = new ChSystem(ContactMethod.Smc);
sys.SetSolver(SolverType.SparseQr);
sys.SetTimestepper(TimestepperType.EulerImplicitLinearized);

var mesh = sys.Add(new FeaMesh());
Range nodes = mesh.BuildEulerBeam(a: new(0, 1, 0), b: new(2, 1, 0), elements: 16, BeamSection.Aluminium(0.03, 0.06));
mesh.FixNode(nodes.Start.Value);                         // clamp the root
mesh.AttachNode(nodes.End.Value - 1, tipMassBody);       // weld the tip to a rigid body
mesh.SetNodeForce(8, new Vec3(0, -50, 0));               // point load
```

| Builder | Elements | Section |
|---|---|---|
| `BuildEulerBeam` | Euler-Bernoulli beams (6 DOF nodes) | `BeamSection(widthY, widthZ, E, G, density, damping)`, presets `Steel`, `Aluminium` |
| `BuildCable` | ANCF cables (position + slope nodes) — large deformation | `CableSection(diameter, E, density, damping)`, presets `SteelWire`, `Rubber` |

Tips:

- Use a direct solver (`SparseQr` or `SparseLu`) and an implicit stepper.
- `sys.DoStaticLinear()` gives the static deflection directly. The tests check a cantilever against δ = FL³/3EI.
- Read deformed shapes with `mesh.GetNodePositions()` (allocation-free) and `mesh.GetLineElements()` for connectivity. `RenderWorld` draws them automatically.

## Vehicles (Chrono::Vehicle)

Vehicle worlds are **Z-up** (ISO convention): X forward, Y left, Z up.

```csharp
using Chrono.Net.Vehicles;

using var car = Vehicle.Create(VehicleModel.Sedan, position: new Vec3(0, 0, 0.5), tire: TireModel.TMeasy);
using var road = RigidTerrain.Flat(car.System, length: 400, width: 40);

while (car.System.Time < 20)
{
    var t = car.Telemetry;
    double throttle = Math.Clamp((80 - t.SpeedKmh) * 0.05, 0, 1);
    car.Step(road, new DriverInputs(Steering: 0, Throttle: throttle, Braking: 0), dt: 0.002);
}
```

| Model | Type |
|---|---|
| `Hmmwv` | AWD off-road truck, double wishbones |
| `Sedan` | passenger car |
| `Gator` | utility vehicle |
| `UazBus` | van, solid axles |
| `CityBus` | city bus |
| `M113` | tracked APC (single-pin track shoes) |

Tire models: `Rigid`, `TMeasy` (default), `Fiala`, `Pac02`.

Terrains:

```csharp
var course = new RigidTerrain(car.System)
    .AddPatch(Frame.Identity, 200, 40)                                                // flat ground
    .AddPatch(new Frame(new(30, 0, -0.3), Quat.FromAngleY(-0.2)), 10, 8)            // ramp
    .Initialize();

var soil = new ScmTerrain(car.System, sizeX: 30, sizeY: 8, resolution: 0.05, SoilParameters.DrySand);
TriangleMesh ruts = soil.GetSurfaceMesh();      // deformed surface for rendering
```

`Telemetry` gives speed, engine rpm and torque, gear, chassis pose and the last driver inputs.

---
Made by Gravicode Studios, led by Kang Fadhil
