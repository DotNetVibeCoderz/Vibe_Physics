using System.Runtime.InteropServices;
using Chrono.Net.Interop;

namespace Chrono.Net;

/// <summary>Raised when a native Chrono call fails.</summary>
public sealed class ChronoException(string message) : Exception(message);

/// <summary>Library-level information and global settings.</summary>
public static class ChronoEngine
{
    /// <summary>ABI version this managed assembly was built against.</summary>
    public const int ExpectedAbiVersion = 1;

    public const string Credits = "Made by Gravicode Studios, led by Kang Fadhil";
    public const string CreditsId = "Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil";

    private static readonly Lazy<bool> _available = new(() =>
    {
        try { return NativeMethods.chn_abi_version() == ExpectedAbiVersion; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
    });

    /// <summary>True when the native engine is present and ABI-compatible.</summary>
    public static bool IsAvailable => _available.Value;

    /// <summary>Version of the wrapped Project Chrono engine (e.g. "10.0.0").</summary>
    public static string ChronoVersion => Marshal.PtrToStringUTF8(NativeMethods.chn_chrono_version()) ?? "?";

    /// <summary>Version of the Chrono.Net assembly.</summary>
    public static string Version => typeof(ChronoEngine).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>True when the native build includes Chrono::Vehicle.</summary>
    public static bool VehicleSupported
    {
        get
        {
            try { return NativeMethods.chn_vehicle_supported() == 1; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }

    /// <summary>Sets Chrono's data directory (textures, meshes, vehicle JSON files).</summary>
    public static void SetDataPath(string path) => NativeMethods.chn_set_data_path(path);

    /// <summary>Default collision envelope and margin applied to collision models created afterwards.</summary>
    public static void SetDefaultCollisionEnvelope(double envelope, double margin) =>
        NativeMethods.chn_set_default_envelope(envelope, margin);

    /// <summary>Throws a descriptive exception when the native engine cannot be loaded.</summary>
    public static void EnsureAvailable()
    {
        if (!IsAvailable)
            throw new ChronoException(
                "chrono_native could not be loaded. Build it with build/build-native.ps1, reference the Chrono.Net NuGet " +
                "package (win-x64), or set CHRONO_NET_NATIVE_PATH to the folder containing chrono_native.");
    }
}
