# Core API guide

[Bahasa Indonesia](../id/guide.md) · [Getting started](getting-started.md)

## Systems

```csharp
var sys = new ChSystem(ContactMethod.Nsc);   // or ContactMethod.Smc
sys.Gravity = new Vec3(0, -9.81, 0);
sys.SetSolver(SolverType.Psor, maxIterations: 100);
sys.SetTimestepper(TimestepperType.EulerImplicitLinearized);
sys.SetThreads(chrono: 4, collision: 4);
```

| Contact method | Use it for | Typical step |
|---|---|---|
| **NSC** (non-smooth, complementarity) | Rigid stacking, mechanisms, robots, large steps | 1–5 ms |
| **SMC** (smooth, penalty) | Granular media, soft contact, FEA with contact | 0.01–0.5 ms |

Stepping:

| Call | Notes |
|---|---|
| `sys.Step(dt)` | One step. Raises `BeforeStep` / `AfterStep`. |
| `sys.Step(dt, count)` | Many steps. Runs entirely in native code when no step events are subscribed. |
| `sys.RunUntil(t, dt)` | Advances to time `t`. |
| `sys.DoAssembly()` | Moves bodies so constraints are satisfied (use after building a closed loop). |
| `sys.DoStaticLinear()` / `DoStaticNonlinear(n)` | Static equilibrium (FEA). |

Solvers available: `Psor`, `PJacobi`, `BarzilaiBorwein`, `Apgd`, `Admm` (iterative VI, for NSC contacts), `SparseLu`, `SparseQr` (direct, for FEA and stiff problems), `Gmres`, `Minres`, `BiCgStab`.

## Bodies and shapes

```csharp
var box   = ChBody.CreateBox(new(1, 0.5, 0.5), density: 7800, material: mat);
var ball  = ChBody.CreateSphere(0.2, 1000, mat);
var can   = ChBody.CreateCylinder(Axis.Y, radius: 0.1, height: 0.3, density: 2700, mat);
var egg   = ChBody.CreateEllipsoid(new(0.1, 0.15, 0.1), 1000, mat);
var hull  = ChBody.CreateConvexHull(points, 1000, mat);
var model = ChBody.CreateMesh(new TriangleMesh(vertices, indices), 1000, mat);
```

`visual: false` skips the visual shape (collision-only walls); `collide: false` makes the body pass through others.

Build compound bodies manually:

```csharp
var cart = new ChBody { Mass = 20, Inertia = new(1, 1.5, 1) };
cart.AddCollisionBox(new(1, 0.2, 0.6), material: mat)
    .AddVisualBox(new(1, 0.2, 0.6), Color.Orange)
    .AddCollisionCylinder(0.15, 0.1, new Frame(new(0.4, -0.1, 0.35)), mat)
    .AddVisualCylinder(0.15, 0.1, Color.Slate, new Frame(new(0.4, -0.1, 0.35)));
cart.CollisionEnabled = true;
```

Cylinders, capsules and cones are aligned with the **local Z axis** of their frame and centered on it.

State and properties: `Position`, `Rotation` (`Quat`, scalar first), `Frame`, `LinearVelocity`, `AngularVelocity` (absolute frame), `LinearAcceleration`, `Mass`, `Inertia`, `IsFixed`, `CollisionEnabled`, `ContactForce`, `ContactTorque`, `KineticEnergy`, `State` (one native call for all of them).

## Contact materials

```csharp
var rubber = new ContactMaterial(ContactMethod.Nsc) { Friction = 0.9f, Restitution = 0.4f, RollingFriction = 0.001f };
var steel  = new ContactMaterial(ContactMethod.Smc) { Friction = 0.3f, YoungModulus = 2e9f, PoissonRatio = 0.3f };
```

Use the same contact method as the system. Materials can be shared by many bodies.

## Joints

Revolute, prismatic and cylindrical joints act along the **Z axis of the joint frame**. Helpers build the frame from a point and an axis:

```csharp
sys.Add(ChJoint.Revolute(door, frame, point: hingePos, axis: Vec3.UnitY));
sys.Add(ChJoint.Prismatic(piston, block, point: p, axis: Vec3.UnitX).SetLimit(JointAxis.Z, -0.1, 0.1));
sys.Add(ChJoint.Spherical(arm, socket, point: p));
sys.Add(ChJoint.Universal(shaft1, shaft2, new Frame(p, rot)));
sys.Add(new ChJoint(JointKind.Planar, puck, table, new Frame(p)));
```

| Kind | Removes | Notes |
|---|---|---|
| `Lock` | all 6 DOF | welds two bodies |
| `Revolute` | 5 | rotation about frame Z |
| `Spherical` | 3 | ball joint |
| `Cylindrical` | 4 | rotate + slide along Z |
| `Prismatic` | 5 | slide along Z |
| `Universal` | 4 | Cardan joint (X/Y axes) |
| `Planar` | 3 | moves on the XY plane |
| `PointPlane`, `PointLine` | 1, 2 | point constrained to a plane / line |
| `Oldham`, `Parallel`, `Orthogonal` | — | axis couplings |

Other constraints: `ChDistanceConstraint`, `ChGear` (ratio = r1/r2, `internalGear` for ring gears), `ChRackPinion`.

Every link exposes `Reaction` (force and torque on body 2, absolute frame) and `Frames`.

## Motors

```csharp
var drive = sys.Add(ChMotor.RotationSpeed(wheel, chassis, axlePos, Vec3.UnitZ, radPerSec: 10));
var servo = sys.Add(ChMotor.RotationAngle(arm, base_, jointPos, Vec3.UnitZ, ChFunction.Sine(0.8, 0.5)));
var lift  = sys.Add(ChMotor.LinearForce(table, frame, p, Vec3.UnitY, ChFunction.Constant(500)));

Console.WriteLine($"{drive.Speed} rad/s, {drive.Effort} N·m, {drive.Power} W");
```

Driving laws (`ChFunction`): `Constant`, `Ramp`, `Sine`, `Poly345` (smooth step), `Setpoint()` (update `.Value` every step, e.g. from a controller or an RL agent), and `FromDelegate(t => …)` for any C# law.

> Angle and position motors are relative to the initial pose: make laws start at 0 to avoid a jolt at t = 0.

## Springs and dampers

```csharp
var spring = sys.Add(new ChSpring(body, ground, attachA, attachB, stiffness: 2000, damping: 50, restLength: 0.4));
spring.ForceLaw = (t, rest, len, vel) => -8000 * Math.Pow(len - rest, 3) - 50 * vel;   // nonlinear, in C#

var torsion = sys.Add(new ChRotationalSpring(lid, box, new Frame(hinge, rot), stiffness: 5, damping: 0.1));
```

Custom laws are invoked from the solver thread through an unmanaged function pointer (no delegate marshalling).

## Loads, contacts, events

```csharp
sys.BeforeStep += (s, dt) =>
{
    rocket.ClearForces();
    rocket.AddForce(new Vec3(0, thrust, 0), point: new Vec3(0, -1, 0), local: true);
};

ContactInfo[] contacts = sys.GetContacts();          // points, normal, force, distance
int n = sys.ContactCount;
```

## Bulk reads and snapshots

```csharp
ReadOnlySpan<BodyState> states = sys.GetBodyStates();   // one native call, reused buffer, no allocation

var snap = sys.SaveState();       // positions + velocities + time
// … simulate …
sys.RestoreState(snap);           // exact reset (RL episodes, what-if analysis)
```

## JSON scenes

```csharp
using var scene = SceneLoader.LoadFile("pendulum-wave.json");
scene.System.Step(scene.TimeStep, 1000);
var bob = scene.Bodies["bob3"];
```

The format is documented in [Tools → Scene files](tools.md#scene-files).

---
Made by Gravicode Studios, led by Kang Fadhil
