# FEA dan kendaraan

[English](../en/fea-and-vehicles.md) · [Panduan API inti](guide.md)

## Elemen hingga (Chrono::FEA)

```csharp
using Chrono.Net.Fea;

using var sys = new ChSystem(ContactMethod.Smc);
sys.SetSolver(SolverType.SparseQr);
sys.SetTimestepper(TimestepperType.EulerImplicitLinearized);

var mesh = sys.Add(new FeaMesh());
Range node = mesh.BuildEulerBeam(a: new(0, 1, 0), b: new(2, 1, 0), elements: 16, BeamSection.Aluminium(0.03, 0.06));
mesh.FixNode(node.Start.Value);                          // jepit pangkalnya
mesh.AttachNode(node.End.Value - 1, bendaMassaUjung);    // las ujungnya ke benda kaku
mesh.SetNodeForce(8, new Vec3(0, -50, 0));               // beban titik
```

| Builder | Elemen | Penampang |
|---|---|---|
| `BuildEulerBeam` | Balok Euler-Bernoulli (node 6 DOF) | `BeamSection(widthY, widthZ, E, G, densitas, redaman)`, preset `Steel`, `Aluminium` |
| `BuildCable` | Kabel ANCF (node posisi + kemiringan) — deformasi besar | `CableSection(diameter, E, densitas, redaman)`, preset `SteelWire`, `Rubber` |

Tips:

- Gunakan solver langsung (`SparseQr` atau `SparseLu`) dan stepper implisit.
- `sys.DoStaticLinear()` langsung memberikan lendutan statis. Pengujian membandingkan kantilever dengan δ = FL³/3EI.
- Baca bentuk terdeformasi dengan `mesh.GetNodePositions()` (tanpa alokasi) dan `mesh.GetLineElements()` untuk konektivitas. `RenderWorld` menggambarnya otomatis.

## Kendaraan (Chrono::Vehicle)

Dunia kendaraan menggunakan **sumbu Z ke atas** (konvensi ISO): X ke depan, Y ke kiri, Z ke atas.

```csharp
using Chrono.Net.Vehicles;

using var mobil = Vehicle.Create(VehicleModel.Sedan, position: new Vec3(0, 0, 0.5), tire: TireModel.TMeasy);
using var jalan = RigidTerrain.Flat(mobil.System, length: 400, width: 40);

while (mobil.System.Time < 20)
{
    var t = mobil.Telemetry;
    double gas = Math.Clamp((80 - t.SpeedKmh) * 0.05, 0, 1);
    mobil.Step(jalan, new DriverInputs(Steering: 0, Throttle: gas, Braking: 0), dt: 0.002);
}
```

| Model | Jenis |
|---|---|
| `Hmmwv` | truk off-road AWD, double wishbone |
| `Sedan` | mobil penumpang |
| `Gator` | kendaraan utilitas |
| `UazBus` | van, gandar kaku |
| `CityBus` | bus kota |
| `M113` | APC beroda rantai (track shoe single-pin) |

Model ban: `Rigid`, `TMeasy` (bawaan), `Fiala`, `Pac02`.

Medan:

```csharp
var lintasan = new RigidTerrain(mobil.System)
    .AddPatch(Frame.Identity, 200, 40)                                              // tanah datar
    .AddPatch(new Frame(new(30, 0, -0.3), Quat.FromAngleY(-0.2)), 10, 8)          // tanjakan
    .Initialize();

var tanah = new ScmTerrain(mobil.System, sizeX: 30, sizeY: 8, resolution: 0.05, SoilParameters.DrySand);
TriangleMesh jejak = tanah.GetSurfaceMesh();    // permukaan terdeformasi untuk rendering
```

`Telemetry` memberikan kecepatan, rpm dan torsi mesin, gigi, pose sasis, dan input pengemudi terakhir.

---
Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
