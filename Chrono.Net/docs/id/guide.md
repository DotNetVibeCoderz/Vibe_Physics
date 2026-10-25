# Panduan API inti

[English](../en/guide.md) · [Memulai](getting-started.md)

## Sistem

```csharp
var sys = new ChSystem(ContactMethod.Nsc);   // atau ContactMethod.Smc
sys.Gravity = new Vec3(0, -9.81, 0);
sys.SetSolver(SolverType.Psor, maxIterations: 100);
sys.SetTimestepper(TimestepperType.EulerImplicitLinearized);
sys.SetThreads(chrono: 4, collision: 4);
```

| Metode kontak | Cocok untuk | Langkah tipikal |
|---|---|---|
| **NSC** (non-smooth, komplementaritas) | Tumpukan benda kaku, mekanisme, robot, langkah besar | 1–5 ms |
| **SMC** (smooth, penalti) | Material granular, kontak lunak, FEA dengan kontak | 0,01–0,5 ms |

Menjalankan simulasi:

| Panggilan | Catatan |
|---|---|
| `sys.Step(dt)` | Satu langkah. Memicu `BeforeStep` / `AfterStep`. |
| `sys.Step(dt, count)` | Banyak langkah. Berjalan sepenuhnya di kode native bila tidak ada event langkah yang didaftarkan. |
| `sys.RunUntil(t, dt)` | Maju sampai waktu `t`. |
| `sys.DoAssembly()` | Menggeser benda agar constraint terpenuhi (pakai setelah membangun loop tertutup). |
| `sys.DoStaticLinear()` / `DoStaticNonlinear(n)` | Kesetimbangan statis (FEA). |

Solver yang tersedia: `Psor`, `PJacobi`, `BarzilaiBorwein`, `Apgd`, `Admm` (VI iteratif, untuk kontak NSC), `SparseLu`, `SparseQr` (langsung, untuk FEA dan masalah kaku), `Gmres`, `Minres`, `BiCgStab`.

## Benda dan bentuk

```csharp
var kotak  = ChBody.CreateBox(new(1, 0.5, 0.5), density: 7800, material: mat);
var bola   = ChBody.CreateSphere(0.2, 1000, mat);
var kaleng = ChBody.CreateCylinder(Axis.Y, radius: 0.1, height: 0.3, density: 2700, mat);
var telur  = ChBody.CreateEllipsoid(new(0.1, 0.15, 0.1), 1000, mat);
var hull   = ChBody.CreateConvexHull(titik, 1000, mat);
var model  = ChBody.CreateMesh(new TriangleMesh(verteks, indeks), 1000, mat);
```

`visual: false` melewatkan bentuk visual (dinding khusus tumbukan); `collide: false` membuat benda menembus benda lain.

Membangun benda gabungan secara manual:

```csharp
var kereta = new ChBody { Mass = 20, Inertia = new(1, 1.5, 1) };
kereta.AddCollisionBox(new(1, 0.2, 0.6), material: mat)
      .AddVisualBox(new(1, 0.2, 0.6), Color.Orange)
      .AddCollisionCylinder(0.15, 0.1, new Frame(new(0.4, -0.1, 0.35)), mat)
      .AddVisualCylinder(0.15, 0.1, Color.Slate, new Frame(new(0.4, -0.1, 0.35)));
kereta.CollisionEnabled = true;
```

Silinder, kapsul dan kerucut sejajar dengan **sumbu Z lokal** dari frame-nya dan berpusat di sana.

Keadaan dan properti: `Position`, `Rotation` (`Quat`, skalar di depan), `Frame`, `LinearVelocity`, `AngularVelocity` (frame absolut), `LinearAcceleration`, `Mass`, `Inertia`, `IsFixed`, `CollisionEnabled`, `ContactForce`, `ContactTorque`, `KineticEnergy`, `State` (semuanya dalam satu panggilan native).

## Material kontak

```csharp
var karet = new ContactMaterial(ContactMethod.Nsc) { Friction = 0.9f, Restitution = 0.4f, RollingFriction = 0.001f };
var baja  = new ContactMaterial(ContactMethod.Smc) { Friction = 0.3f, YoungModulus = 2e9f, PoissonRatio = 0.3f };
```

Gunakan metode kontak yang sama dengan sistemnya. Satu material bisa dipakai bersama oleh banyak benda.

## Sendi

Sendi revolute, prismatic dan cylindrical bekerja sepanjang **sumbu Z frame sendi**. Helper membangun frame dari sebuah titik dan sumbu:

```csharp
sys.Add(ChJoint.Revolute(pintu, kusen, point: posEngsel, axis: Vec3.UnitY));
sys.Add(ChJoint.Prismatic(piston, blok, point: p, axis: Vec3.UnitX).SetLimit(JointAxis.Z, -0.1, 0.1));
sys.Add(ChJoint.Spherical(lengan, soket, point: p));
sys.Add(ChJoint.Universal(poros1, poros2, new Frame(p, rot)));
sys.Add(new ChJoint(JointKind.Planar, keping, meja, new Frame(p)));
```

| Jenis | Menghilangkan | Catatan |
|---|---|---|
| `Lock` | 6 DOF | mengelas dua benda |
| `Revolute` | 5 | rotasi terhadap Z frame |
| `Spherical` | 3 | sendi bola |
| `Cylindrical` | 4 | berputar + bergeser sepanjang Z |
| `Prismatic` | 5 | bergeser sepanjang Z |
| `Universal` | 4 | sendi Cardan (sumbu X/Y) |
| `Planar` | 3 | bergerak di bidang XY |
| `PointPlane`, `PointLine` | 1, 2 | titik dibatasi pada bidang / garis |
| `Oldham`, `Parallel`, `Orthogonal` | — | kopling sumbu |

Constraint lain: `ChDistanceConstraint`, `ChGear` (rasio = r1/r2, `internalGear` untuk roda gigi cincin), `ChRackPinion`.

Setiap sambungan menyediakan `Reaction` (gaya dan torsi pada benda 2, frame absolut) dan `Frames`.

## Motor

```csharp
var penggerak = sys.Add(ChMotor.RotationSpeed(roda, sasis, posAs, Vec3.UnitZ, radPerSec: 10));
var servo     = sys.Add(ChMotor.RotationAngle(lengan, dasar, posSendi, Vec3.UnitZ, ChFunction.Sine(0.8, 0.5)));
var pengangkat = sys.Add(ChMotor.LinearForce(meja, rangka, p, Vec3.UnitY, ChFunction.Constant(500)));

Console.WriteLine($"{penggerak.Speed} rad/s, {penggerak.Effort} N·m, {penggerak.Power} W");
```

Hukum penggerak (`ChFunction`): `Constant`, `Ramp`, `Sine`, `Poly345` (langkah halus), `Setpoint()` (perbarui `.Value` setiap langkah, misalnya dari pengendali atau agen RL), dan `FromDelegate(t => …)` untuk hukum C# apa pun.

> Motor sudut dan posisi relatif terhadap pose awal: buat hukumnya mulai dari 0 agar tidak ada hentakan pada t = 0.

## Pegas dan peredam

```csharp
var pegas = sys.Add(new ChSpring(benda, tanah, titikA, titikB, stiffness: 2000, damping: 50, restLength: 0.4));
pegas.ForceLaw = (t, rest, len, vel) => -8000 * Math.Pow(len - rest, 3) - 50 * vel;   // nonlinier, di C#

var torsi = sys.Add(new ChRotationalSpring(tutup, kotak, new Frame(engsel, rot), stiffness: 5, damping: 0.1));
```

Hukum kustom dipanggil dari thread solver melalui function pointer unmanaged (tanpa marshalling delegate).

## Beban, kontak, event

```csharp
sys.BeforeStep += (s, dt) =>
{
    roket.ClearForces();
    roket.AddForce(new Vec3(0, dorong, 0), point: new Vec3(0, -1, 0), local: true);
};

ContactInfo[] kontak = sys.GetContacts();          // titik, normal, gaya, jarak
int n = sys.ContactCount;
```

## Pembacaan massal dan snapshot

```csharp
ReadOnlySpan<BodyState> keadaan = sys.GetBodyStates();   // satu panggilan native, buffer dipakai ulang, tanpa alokasi

var snap = sys.SaveState();       // posisi + kecepatan + waktu
// … simulasi …
sys.RestoreState(snap);           // reset persis (episode RL, analisis what-if)
```

## Scene JSON

```csharp
using var scene = SceneLoader.LoadFile("pendulum-wave.json");
scene.System.Step(scene.TimeStep, 1000);
var bandul = scene.Bodies["bob3"];
```

Formatnya dijelaskan di [Perangkat → Berkas scene](tools.md#berkas-scene).

---
Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
