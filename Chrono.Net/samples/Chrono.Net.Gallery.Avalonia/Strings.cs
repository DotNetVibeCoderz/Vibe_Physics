namespace Chrono.Net.Gallery;

/// <summary>UI copy in English and Bahasa Indonesia.</summary>
public sealed record Strings(
    string Search, string Run, string Pause, string Reset, string Step, string Speed,
    string About, string Code, string Signals, string Tags, string Bodies, string Links, string Contacts,
    string RealTime, string Time, string NoEngineTitle, string NoEngineBody, string NoVehicle, string CopyCode, string Copied,
    string Credits, string Hint)
{
    public static readonly Strings En = new(
        "Search samples", "Run", "Pause", "Reset", "Step", "Speed",
        "About", "Code", "Signals", "Demonstrates", "bodies", "links", "contacts",
        "real time", "t", "Native engine not found",
        "Build it with ./build/build-native.ps1, then restart the gallery. The samples, code and descriptions stay browsable meanwhile.",
        "This native build has no Chrono::Vehicle module.", "Copy code", "Copied",
        "Made by Gravicode Studios, led by Kang Fadhil",
        "Drag to orbit · right-drag to pan · scroll to zoom · double-click to frame");

    public static readonly Strings Id = new(
        "Cari contoh", "Jalankan", "Jeda", "Ulang", "Langkah", "Kecepatan",
        "Tentang", "Kode", "Sinyal", "Mendemonstrasikan", "benda", "sambungan", "kontak",
        "waktu nyata", "t", "Mesin native tidak ditemukan",
        "Bangun dengan ./build/build-native.ps1, lalu jalankan ulang galeri. Contoh, kode dan deskripsi tetap bisa dijelajahi.",
        "Build native ini tidak menyertakan modul Chrono::Vehicle.", "Salin kode", "Tersalin",
        "Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil",
        "Seret untuk memutar · seret kanan untuk geser · gulir untuk zoom · klik ganda untuk membingkai");

    public static string Category(string category, bool indonesian) => !indonesian ? category : category switch
    {
        "Basics" => "Dasar",
        "Mechanisms" => "Mekanisme",
        "Contacts" => "Kontak",
        "Robotics" => "Robotika",
        "FEA" => "FEA",
        "Vehicles" => "Kendaraan",
        _ => category,
    };
}
