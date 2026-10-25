# Memulai

[English](../en/getting-started.md) · [Indeks dokumentasi](../../README.id.md#dokumentasi)

## 1. Instalasi

**Dari NuGet** (Windows x64):

```powershell
dotnet add package Chrono.Net              # mesin + biner native
dotnet add package Chrono.Net.Rendering    # opsional: gambar, GIF, renderer perangkat lunak
dotnet add package Chrono.Net.Avalonia     # opsional: kontrol desktop
dotnet add package Chrono.Net.Blazor       # opsional: kontrol web
```

Pustaka native berada di `runtimes/win-x64/native`. Beri aplikasi Anda runtime identifier (`<RuntimeIdentifier>win-x64</RuntimeIdentifier>`) atau jalankan di win-x64 agar pustaka tersebut disalin ke sebelahnya.

**Dari template:**

```powershell
dotnet new install Chrono.Net.Templates
dotnet new chrono-console  -n SimSaya       # konsol + keluaran PNG
dotnet new chrono-avalonia -n DesktopSaya   # aplikasi desktop dengan viewport dan plot
dotnet new chrono-blazor   -n WebSaya       # aplikasi web yang mengalirkan ke WebGL
```

Periksa instalasi:

```csharp
Console.WriteLine(ChronoEngine.IsAvailable);    // true
Console.WriteLine(ChronoEngine.ChronoVersion);  // 10.0.0
```

Jika `IsAvailable` bernilai false, pustaka native tidak ditemukan. Atur `CHRONO_NET_NATIVE_PATH` ke folder yang berisi `chrono_native.dll`, atau bangun sendiri (lihat [Membangun dari kode sumber](building-from-source.md)).

## 2. Simulasi pertama Anda

```csharp
using Chrono.Net;

using var sys = new ChSystem();                       // gravitasi (0, -9.81, 0)
var mat = sys.Own(new ContactMaterial { Friction = 0.6f, Restitution = 0.2f });

// Tanah: kotak tetap yang permukaan atasnya di y = 0
sys.Add(ChBody.CreateBox(new(10, 1, 10), 1000, mat).At(0, -0.5, 0).Fixed());

// Bandul: batang berengsel pada jangkar tak terlihat
var jangkar = sys.Add(new ChBody { IsFixed = true, CollisionEnabled = false });
var batang = sys.Add(ChBody.CreateBox(new(1, 0.05, 0.05), 2700, collide: false).At(0.5, 2, 0));
sys.Add(ChJoint.Revolute(batang, jangkar, point: new Vec3(0, 2, 0), axis: Vec3.UnitZ));

for (int i = 0; i < 2000; i++)
{
    sys.Step(0.001);
    if (i % 250 == 0) Console.WriteLine($"t={sys.Time:0.00}  ujung={batang.Frame.TransformPoint(new(0.5, 0, 0))}");
}
```

Konsep utama:

- **Benda** dibuat dengan factory yang menghitung massa dan inersia dari densitas (`CreateBox`, `CreateSphere`, …) dan dikonfigurasi secara berantai (`At`, `Rotated`, `Fixed`, `Colored`, `Named`).
- **Semuanya ditambahkan ke sistem** dengan `sys.Add(...)`, yang mengembalikan objek yang sama sehingga Anda bisa menyimpan referensinya.
- **Pelepasan sumber daya:** membuang (dispose) sistem akan membuang semua yang dimilikinya. Gunakan `sys.Own(x)` untuk material dan fungsi yang Anda buat sendiri.
- **Satuan** mengikuti SI: meter, kilogram, detik, newton, radian.

## 3. Lihat hasilnya

```csharp
using Chrono.Net.Rendering;

var world = new RenderWorld(sys);
var camera = new OrbitCamera { Target = new(0.5f, 1.5f, 0), Distance = 5 };
Snapshot.SavePng(world, camera, "bandul.png");
```

Untuk tampilan interaktif gunakan `ChronoViewport` di [Avalonia atau Blazor](ui-components.md).

## 4. Langkah berikutnya

- [Panduan API inti](guide.md): bentuk, material, semua sendi dan motor, pegas dengan hukum kustom, beban, kontak, snapshot.
- [FEA dan kendaraan](fea-and-vehicles.md).
- Jelajahi 16 contoh: `chrono list`, `chrono source four-bar`, atau aplikasi Gallery.

---
Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
