# ChronoAppGen — Chrono.Net App Generator

[English](../en/appgen.md)

ChronoAppGen adalah IDE desktop (Avalonia) untuk membuat aplikasi Chrono.Net bersama **Jack — The Code Bender**, asisten
AI yang dibangun di atas Semantic Kernel. Anda mendeskripsikan aplikasinya; Jack menulis kode, membangunnya, menjalankan
fisikanya tanpa jendela, membaca angkanya, lalu memperbaiki yang salah.

![ChronoAppGen dengan Claude: Jack mengubah template bowling, membangunnya dan menjelaskan hasil self-test](../images/appgen.png)

```powershell
dotnet run --project tools/Chrono.Net.AppGen            # atau: ChronoAppGen.exe [folderProyek]
```

## Jendela

| Area | Fungsinya |
|---|---|
| Menu dan toolbar | **New** proyek (Ctrl+N), **Open** proyek (Ctrl+Shift+O) atau file (Ctrl+O), **Save** (Ctrl+S), **Close** proyek, **Build** (Ctrl+Shift+B), **Run** (F5) / **Stop** (Shift+F5), **Deploy** (`dotnet publish`), **Go to line** (Ctrl+G), **Format** (Shift+Alt+F), nomor baris tampil/sembunyi (Ctrl+Alt+L), **Exit** |
| Explorer (kiri) | Pohon folder proyek seperti VS Code: ikon jenis file, file/folder baru, ganti nama, hapus, tampilkan di File Explorer. Diperbarui otomatis saat file berubah di disk. Tampil/sembunyi dengan Ctrl+B |
| Editor (tengah) | Tab, nomor baris, penyorotan sintaks (C#, AXAML/XML, JSON, Markdown, dll.), cari/ganti (Ctrl+F), undo per file, penanda belum disimpan, muat ulang saat file berubah di disk |
| Log (bawah) | **Output** (keluaran aplikasi, self-test), **Build** (keluaran kompiler) dan **Jack** (setiap pemanggilan tool). Setelah build gagal, *Ask Jack to fix the build* mengirim error ke Jack. Tampil/sembunyi dengan Ctrl+J |
| Jack (kanan) | Pemilih model di atas, utas chat, lampiran gambar, **Send** dengan Ctrl+Enter atau tombol, **Stop**, **Clear thread**. Ubah lebar dengan menyeret pemisah; sembunyikan dengan Ctrl+Shift+J |
| Status bar | Proyek, file, baris/kolom, bahasa, status build, jejak kerja Jack, model aktif, kredit |

Setiap jawaban Jack diawali **jejak kerja**: garis osiloskop teal dengan satu pulsa per pemanggilan tool (makin tinggi
makin lama, celah merah untuk yang gagal). Buka *steps* untuk melihat semua pemanggilan; arahkan kursor ke langkah untuk
melihat argumen dan hasilnya.

## Proyek baru

**File → New project** menyediakan dua awal:

- **Blank** — aplikasi desktop kosong (viewport 3D, plot, run/step/reset) atau aplikasi konsol kosong.
- **From template** — 23 aplikasi lengkap yang langsung bisa dibangun dan dijalankan, per kategori:

| Kategori | Template |
|---|---|
| Simulator | Vehicle Test Track (slalom HMMWV), Gantry Crane Operator |
| Sains | Projectile & Drag Lab, Orbital Mechanics, Coupled Oscillators |
| Militer | Artillery Fire Control, Tactical Obstacle Course (HMMWV), Blast Load on a Barrier |
| Hiburan | Pinball Table, Bowling Alley, Marble Run |
| Pekerjaan | Forklift Pallet Handling |
| Industri | Belt Conveyor Sorter, Inline-4 Engine, Pick & Place Arm |
| Edukasi | Pendulum Lab, Friction on an Inclined Plane, Momentum & Collisions |
| Bencana & Keselamatan | Earthquake Shake Table, Rockfall Protection Barrier, Crash Test Sled |
| Perangkat | JSON Scene Player, Parameter Sweep (CSV, konsol) |

![Dialog proyek baru](../images/appgen-new-project.png)

Setiap proyek hasil generate punya susunan yang sama: `Simulation.cs` (scene dan probe yang diplot), `Scene.cs`
(wadah + runner tanpa jendela), `Build.cs` (helper), `Program.cs` (jendela, atau entri konsol) dan README.
`dotnet run -- --selftest [--seconds N]` menjalankan scene tanpa jendela dan mencetak probe — Jack memakainya untuk
memeriksa fisika. Semua template diverifikasi dengan `ChronoAppGen --verify-templates <dir>` (generate, build, self-test).

Proyek mereferensikan paket `Chrono.Net` dan `Chrono.Net.Avalonia`. Selama belum ada di nuget.org, ChronoAppGen menulis
`nuget.config` yang menunjuk folder paket lokal (terdeteksi otomatis dari build repositori di `artifacts/packages`, atau
diatur di Settings → Projects & build).

## Tool Jack (kernel function)

| Plugin | Fungsi |
|---|---|
| workspace | `GetProjectInfo`, `ListFiles`, `ReadFile`, `WriteFile`, `ReplaceInFile`, `SearchFiles`, `CreateFolder`, `DeleteFile`, `RenameFile`, `GetActiveEditor`, `OpenInEditor` — terbatas pada folder proyek |
| build | `BuildProject` (mengembalikan error kompiler), `RunSelfTest` (cek fisika tanpa jendela), `LaunchApp`, `AddNuGetPackage`, `RunDotnetCommand` (restore, build, clean, list, add, remove, new, format, test) |
| chrono | `GetChronoGuide`, `GetApiReference` (hasil refleksi assembly Chrono.Net beserta ringkasan XML-doc), `ListSamples`, `GetSampleSource`, `ListProjectTemplates`, `CreateProjectFromTemplate` |
| web | `SearchInternet` (Tavily), `ScrapeWebPage` (teks halaman yang mudah dibaca) |
| math | `Calculate` (ekspresi eksak), `ConvertUnits` |
| time | `GetCurrentDateTime` (zona waktu apa pun), `DaysBetween` |

## Model dan pengaturan

Penyedia yang didukung: **OpenAI**, **Azure OpenAI**, **Claude** (SDK Anthropic, workspace ID opsional), **Gemini**
(endpoint kompatibel OpenAI), **Ollama** (lokal, tanpa key) dan **DeepSeek**. Pilih model di bagian atas panel chat.

![Settings → Models](../images/appgen-settings.png)

Semua disimpan di **app.config** (`ChronoAppGen.dll.config` di samping executable) dan bisa diubah di
**Tools → Settings** (Ctrl+,):

- **Models** — per penyedia: model (dipisah koma), API key, endpoint, workspace ID (Claude), temperature (kosong =
  bawaan penyedia; model reasoning yang menolak temperature dicoba ulang tanpa itu), max output tokens, system prompt
  khusus. *Test connection* mengirim permintaan satu kata.
- **Jack & tools** — system prompt Jack (dengan *Reset to default*), batas pemanggilan tool per pesan, key Tavily.
- **Editor** — ukuran dan jenis font, ukuran indentasi, nomor baris, word wrap.
- **Projects & build** — folder proyek, versi Chrono.Net dan Avalonia, folder paket lokal, konfigurasi build, runtime
  deploy, self-contained, single file.

Tata letak (ukuran dan visibilitas panel), model aktif dan proyek terakhir juga disimpan di sana. API key tidak pernah
ditulis ke `App.config` di repositori — hanya ke salinan yang terpasang.

## Baris perintah

```powershell
ChronoAppGen --list-templates
ChronoAppGen --generate pinball-table C:\work PinballSaya
ChronoAppGen --verify-templates C:\temp\verify            # semua template: generate, build, self-test
ChronoAppGen --agent "Tambahkan slider gravitasi" --project C:\work\PinballSaya --provider Claude
```

`--agent` menjalankan satu giliran Jack di konsol dengan tool yang sama seperti IDE (untuk generate terskrip dan pengujian).

---
Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
