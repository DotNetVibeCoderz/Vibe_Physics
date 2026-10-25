<p align="center"><img src="assets/icon-128.png" width="96" alt="Logo Chrono.Net" /></p>

<h1 align="center">Chrono.Net</h1>

<p align="center">
  <b>Multiphysics Project Chrono untuk .NET 10</b> — benda kaku, kontak, sendi, motor, pegas, elemen hingga dan model kendaraan lengkap, dengan komponen Avalonia dan Blazor.<br/>
  <a href="README.md">Read in English</a> · <a href="docs/id/getting-started.md">Dokumentasi</a>
</p>

<p align="center"><img src="docs/images/gallery-mixer.png" alt="Chrono.Net Gallery menjalankan contoh mixer industri" /></p>

Chrono.Net membungkus mesin C++ [Project Chrono](https://projectchrono.org) (v10.0.0) melalui C ABI yang tipis dan bebas alokasi. Anda menulis C#; solver Chrono yang mengerjakan fisikanya.

```csharp
using Chrono.Net;

using var sys = new ChSystem();                                   // kontak NSC, gravitasi -Y
var mat = sys.Own(new ContactMaterial { Friction = 0.5f });
sys.Add(ChBody.CreateBox(new(10, 1, 10), 1000, mat).At(0, -0.5, 0).Fixed());
var bola = sys.Add(ChBody.CreateSphere(0.25, 7800, mat).At(0, 3, 0));

sys.Step(0.002, 1000);                                            // 1000 langkah dalam satu panggilan native
Console.WriteLine(bola.Position);
```

## Isi paket

| Paket | Fungsinya |
|---|---|
| **Chrono.Net** | `ChSystem`, `ChBody` (kotak, bola, silinder, elipsoid, convex hull, mesh), material kontak (NSC/SMC), 12 jenis sendi, roda gigi, rack-and-pinion, 6 jenis motor, pegas-peredam linear/rotasi dengan hukum gaya C#, laporan kontak, snapshot keadaan, balok dan kabel FEA, Chrono::Vehicle (HMMWV, Sedan, Gator, UAZ, CityBus, M113) di medan kaku atau medan deformable SCM, scene JSON. Sudah termasuk biner native win-x64. |
| **Chrono.Net.Rendering** | Model render netral platform, kamera orbit, rasterizer perangkat lunak multithread dengan bayangan, ekspor PNG dan GIF — bisa berjalan tanpa layar. |
| **Chrono.Net.Avalonia** | `ChronoViewport` (3D interaktif), `ChronoPlot` (sinyal langsung), `SimulationHost` (loop waktu nyata), dan tema instrument-bench. |
| **Chrono.Net.Blazor** | Viewport WebGL (three.js), plot sinyal SVG, `SimulationLoop` untuk rendering interaktif di server. |
| **Chrono.Net.Samples** | 16 scene siap jalan yang dipakai Gallery, CLI dan dokumentasi. |
| **Chrono.Net.Cli** | Perintah `chrono`: `info`, `list`, `run` (ekspor CSV), `render` (PNG/GIF), `bench`, `new`, `validate`, `source`. |
| **Chrono.Net.Templates** | `dotnet new chrono-console`, `chrono-avalonia`, `chrono-blazor`. |

Ditambah tiga aplikasi di repo ini: **Chrono.Net Gallery** (desktop Avalonia dan web Blazor), **Chrono.Net Simulator** untuk menyusun scene JSON, dan **ChronoAppGen**.

### ChronoAppGen — buat aplikasi dengan mendeskripsikannya

IDE desktop dengan **Jack — The Code Bender**, asisten AI (Semantic Kernel; OpenAI, Azure OpenAI, Claude, Gemini, Ollama atau DeepSeek) yang menulis aplikasi Chrono.Net, membangunnya, menjalankan fisikanya tanpa jendela, dan memperbaiki yang rusak. Explorer, editor bertab dengan penyorotan sintaks, build/run/deploy, log, dan 23 template proyek terverifikasi mulai dari simulator dan sains hingga industri, keselamatan dan game. Lihat [ChronoAppGen](docs/id/appgen.md).

<p align="center"><img src="docs/images/appgen.png" alt="ChronoAppGen: Jack mengubah proyek, membangunnya dan menjelaskan hasil self-test" /></p>

<table>
<tr>
<td><img src="docs/images/render-four-bar.png" alt="Mekanisme empat batang" /></td>
<td><img src="docs/images/render-robot-arm.png" alt="Lengan robot 3-DOF" /></td>
</tr>
<tr>
<td><img src="docs/images/render-fea-cable.png" alt="Bandul kabel ANCF" /></td>
<td><img src="docs/images/render-vehicle-hmmwv.png" alt="Kendaraan HMMWV" /></td>
</tr>
</table>

## Mulai cepat

```powershell
dotnet new install Chrono.Net.Templates
dotnet new chrono-console -n SimulasiSaya
cd SimulasiSaya; dotnet run    # menyimulasikan lalu menulis result.png
```

Atau jelajahi contoh:

```powershell
dotnet tool install -g Chrono.Net.Cli
chrono list
chrono render mixer --out mixer.gif --duration 6
chrono run four-bar --duration 5 --csv fourbar.csv
```

## Membangun dari kode sumber

Kebutuhan: Windows x64, .NET 10 SDK, Visual Studio 2022+ dengan workload C++ (CMake dan Ninja sudah termasuk).

```powershell
./build/build-native.ps1            # mengunduh dan membangun Project Chrono 10 + Eigen, lalu shim native (1–2 jam pertama kali)
dotnet build Chrono.Net.slnx
dotnet test --project tests/Chrono.Net.Tests
dotnet run --project samples/Chrono.Net.Gallery.Avalonia
dotnet run --project samples/Chrono.Net.Gallery.Blazor      # lalu buka URL yang tercetak
dotnet run --project tools/Chrono.Net.AppGen                # ChronoAppGen (isi API key di Settings → Models)
```

Lihat [Membangun dari kode sumber](docs/id/building-from-source.md) untuk opsi, dan [Arsitektur](docs/id/architecture.md) untuk susunan lapisannya.

## Dokumentasi

- [Memulai](docs/id/getting-started.md)
- [Panduan API inti](docs/id/guide.md) — sistem, benda, bentuk, material, sendi, motor, pegas, beban, snapshot
- [FEA dan kendaraan](docs/id/fea-and-vehicles.md)
- [Komponen UI](docs/id/ui-components.md) — rendering, Avalonia, Blazor
- [Perangkat](docs/id/tools.md) — Gallery, Simulator, CLI, template, scene JSON
- [ChronoAppGen](docs/id/appgen.md) — IDE pembuat aplikasi berbasis AI bersama Jack
- [Arsitektur](docs/id/architecture.md) dan [catatan performa](docs/id/architecture.md#performa)
- [Membangun dari kode sumber](docs/id/building-from-source.md)
- Peta jalan: [PLAN.md](PLAN.md) · Kemajuan: [Progress.md](Progress.md)

## Lisensi

Chrono.Net berlisensi MIT. Pustaka ini membungkus Project Chrono (BSD-3-Clause) dan dibangun dengan Eigen (MPL-2.0); font yang disertakan (Barlow Condensed, JetBrains Mono) berlisensi SIL Open Font License dan three.js berlisensi MIT. Lihat [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

<p align="center"><b>Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil</b></p>
