# Arsitektur

[English](../en/architecture.md)

```
 ┌──────────────────────── aplikasi ───────────────────────┐
 │ Gallery (Avalonia, Blazor) · Simulator · CLI chrono      │
 └──────────────┬───────────────────────────┬──────────────┘
   Chrono.Net.Avalonia            Chrono.Net.Blazor (three.js)
          └──────── Chrono.Net.Rendering ────────┘   Chrono.Net.Samples
                           │
                      Chrono.Net  (API managed, SafeHandle, LibraryImport)
                           │  C ABI datar — native/include/chrono_native*.h
                   chrono_native.dll (shim C++17, model kendaraan terkompilasi)
                           │
        Project Chrono 10: Chrono_core · Chrono_vehicle · Bullet · Eigen
```

## Batas native

- **Handle opak.** Setiap objek native adalah holder di heap yang berisi satu `std::shared_ptr`. Wrapper managed memilikinya lewat `SafeHandle`, sehingga masa hidupnya deterministik (`Dispose`) dan aman (finalizer). Melepas handle mengurangi satu referensi; Chrono tetap menjaga objek selama sistem memakainya.
- **Tanpa exception melintasi ABI.** Setiap titik masuk menangkap exception C++, mengembalikan status atau handle null, dan menyimpan pesan thread-local. Sisi managed melempar `ChronoException` dengan pesan tersebut.
- **Matematika blittable.** `Vec3`, `Quat`, `Frame`, `BodyState`, `ContactInfo` dan `VisualShape` memiliki tata letak yang sama di C dan C#, dan dikirim by value atau pointer tanpa marshalling.
- **Callback** (`ChFunction.FromDelegate`, hukum gaya pegas, hukum torsi) memakai trampolin `delegate* unmanaged[Cdecl]` ditambah `GCHandle` ke objek tujuan. Tidak ada delegate yang di-marshal dan tidak ada thunk yang dialokasikan per panggilan.
- **Model kendaraan** berasal dari kode sumber model Chrono sendiri, dikompilasi langsung ke `chrono_native` (hanya enam model yang diekspos), sehingga build native lebih singkat.

## Mengapa tanpa Rust?

Brief mengizinkan Rust "jika diperlukan untuk performa". API Chrono berbasis C++ (template, `shared_ptr`, callback virtual), sehingga shim C++ adalah batas yang paling alami dan tercepat. Menambahkan Rust berarti ada lompatan FFI kedua tanpa manfaat, jadi Rust dievaluasi dan tidak dipakai.

## Performa

- `ChSystem.Step(dt, count)` menjalankan seluruh loop secara native bila tidak ada event langkah (satu P/Invoke untuk ribuan langkah).
- `GetBodyStates()` mengambil pose, kecepatan dan frame visual **semua** benda dalam satu panggilan ke buffer yang dipakai ulang. Renderer, ekspor CSV dan streaming Blazor memakainya.
- `RenderWorld` menyimpan mesh unit per jenis primitif dan hanya memperbarui matriks setiap frame.
- Rasterizer perangkat lunak membagi layar menjadi pita yang diproses paralel, dengan shading per verteks.
- Blazor mengirim 28 byte per benda per frame dalam bentuk biner (`byte[]` → `Uint8Array`), bukan JSON.
- Ukur di mesin Anda dengan `chrono bench --bodies 1000`.

## Struktur repositori

| Folder | Isi |
|---|---|
| `native/` | Header C ABI dan shim C++, proyek CMake |
| `src/` | Paket: Chrono.Net, Rendering, Samples, Avalonia, Blazor |
| `tools/` | CLI, Simulator, alat screenshot headless |
| `samples/` | Aplikasi Gallery |
| `templates/` | Template `dotnet new` |
| `tests/` | Tes xUnit v3 (matematika, fisika terhadap hasil analitik, FEA, scene) |
| `build/` | `build-native.ps1`, `pack.ps1` |
| `external/` | Chrono, Eigen dan folder build (diabaikan git) |

---
Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
