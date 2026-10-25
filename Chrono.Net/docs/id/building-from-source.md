# Membangun dari kode sumber

[English](../en/building-from-source.md)

## Kebutuhan

- Windows 10/11 x64
- .NET 10 SDK
- Visual Studio 2022 atau lebih baru dengan **Desktop development with C++** (sudah termasuk CMake dan Ninja)
- Git, dan ruang disk kosong sekitar 6 GB
- Disarankan RAM 16 GB (unit kompilasi Chrono berukuran besar)

## Mesin native

```powershell
./build/build-native.ps1                 # Chrono 10.0.0 + Eigen 3.4.1 + shim → artifacts/native/win-x64
./build/build-native.ps1 -SkipChrono     # hanya membangun ulang shim setelah mengubah native/
./build/build-native.ps1 -Jobs 4         # kompilasi paralel lebih sedikit bila memori terbatas
./build/build-native.ps1 -EnableVehicle:$false   # hanya inti (lebih cepat)
```

Skrip ini mencari MSVC dengan `vswhere`, meng-clone Chrono dan Eigen ke `external/`, membangun Chrono (Release, pustaka bersama) beserta modul kendaraan, memasangnya ke `external/chrono-install`, membangun `native/`, dan menyalin semua DLL ke `artifacts/native/win-x64`. Build pertama memakan 1–2 jam di laptop 4 inti.

`src/Chrono.Net` otomatis menyalin DLL tersebut ke folder keluaran setiap aplikasi, dan mengemasnya di `runtimes/win-x64/native`.

## Kode managed

```powershell
dotnet build Chrono.Net.slnx
dotnet test --project tests/Chrono.Net.Tests                                       # tes fisika dilewati bila mesin tidak ada
dotnet test --project tests/Chrono.Net.Tests -- --filter-method "*FreeFall*"      # satu tes
```

## Screenshot untuk dokumentasi

```powershell
dotnet run --project tools/Chrono.Net.Screenshots -- docs/images mixer@3 four-bar@1.2
```

Perintah ini merender Gallery Avalonia tanpa layar (Skia) dan viewport polos untuk setiap contoh.

## Paket NuGet

```powershell
./build/pack.ps1                       # → artifacts/packages
./build/pack.ps1 -Publish              # mendorong ke nuget.org (key dari $env:NUGET_API_KEY atau berkas kredensial Anda)
```

## Integrasi berkelanjutan dan rilis

`.github/workflows/chrono-net.yml` (di root repositori Vibe_Physics) berjalan pada setiap push atau pull request yang
menyentuh `Chrono.Net/`. Workflow ini membangun Chrono dan shim di `windows-latest`, menjalankan tes, membuat paket,
lalu mengunggah paket dan DLL native sebagai artefak workflow. Run pertama membangun Chrono dari sumber (sekitar
2 jam); hasil instalasinya di-cache, sehingga run berikutnya hanya membangun ulang shim.

Untuk menerbitkan, buat GitHub Release dengan tag `chrono-net-v<versi>`, misalnya `chrono-net-v0.1.0`. Workflow akan
membuat paket versi tersebut dan mendorongnya memakai secret repositori `NUGET_API_KEY`. Run manual (*Run workflow*,
`publish: true`) melakukan hal yang sama. Naikkan juga versi `Chrono.Net` yang dipatok di proyek template.

---
Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
