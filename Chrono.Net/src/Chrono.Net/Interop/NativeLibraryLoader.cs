using System.Reflection;
using System.Runtime.InteropServices;

namespace Chrono.Net.Interop;

/// <summary>
/// Locates and loads <c>chrono_native</c> and the Chrono engine libraries it depends on.
/// Search order: <c>CHRONO_NET_NATIVE_PATH</c> env var, the app base directory, then <c>runtimes/{rid}/native</c>.
/// </summary>
internal static class NativeLibraryLoader
{
    private static int _registered;
    private static nint _handle;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeLibraryLoader).Assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // A resolver was already set by the host; fall back to default probing.
        }
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != NativeMethods.Lib) return 0;
        if (_handle != 0) return _handle;

        foreach (var dir in CandidateDirectories())
        {
            var path = Path.Combine(dir, LibraryFileName(libraryName));
            if (!File.Exists(path)) continue;
            PreloadDependencies(dir);
            if (NativeLibrary.TryLoad(path, out _handle)) return _handle;
        }
        return NativeLibrary.TryLoad(libraryName, assembly, searchPath, out _handle) ? _handle : 0;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var env = Environment.GetEnvironmentVariable("CHRONO_NET_NATIVE_PATH");
        if (!string.IsNullOrWhiteSpace(env)) yield return env;
        var baseDir = AppContext.BaseDirectory;
        yield return baseDir;
        yield return Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
        yield return Path.Combine(baseDir, "runtimes", DefaultRid(), "native");
    }

    /// <summary>
    /// Loads Chrono engine libraries from <paramref name="dir"/> by absolute path so that the OS loader resolves
    /// chrono_native's imports even when the directory is not on the default search path.
    /// </summary>
    private static void PreloadDependencies(string dir)
    {
        string pattern = OperatingSystem.IsWindows() ? "Chrono_*.dll" : "libChrono_*";
        var libs = Directory.GetFiles(dir, pattern)
            .OrderBy(f => Path.GetFileName(f).Contains("core", StringComparison.OrdinalIgnoreCase) ? 0
                : Path.GetFileName(f).Contains("models", StringComparison.OrdinalIgnoreCase) ? 2 : 1)
            .ToArray();
        foreach (var lib in libs) NativeLibrary.TryLoad(lib, out _);
    }

    private static string LibraryFileName(string name) =>
        OperatingSystem.IsWindows() ? name + ".dll" : OperatingSystem.IsMacOS() ? "lib" + name + ".dylib" : "lib" + name + ".so";

    private static string DefaultRid()
    {
        string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64",
        };
        return $"{os}-{arch}";
    }
}
