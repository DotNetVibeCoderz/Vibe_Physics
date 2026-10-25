using System.ComponentModel;
using System.Globalization;
using Chrono.Net.AppGen.Build;
using Microsoft.SemanticKernel;

namespace Chrono.Net.AppGen.Ai.Plugins;

/// <summary>Build, test and run the open project with the .NET SDK.</summary>
public sealed class BuildPlugin(IIdeHost host, Func<string> configuration)
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.OrdinalIgnoreCase)
        { "restore", "build", "clean", "list", "add", "remove", "new", "format", "test" };

    [KernelFunction, Description("Builds the open project with 'dotnet build'. Returns success or the compiler errors (file(line,col): error CODE: message).")]
    public async Task<string> BuildProject(CancellationToken ct = default)
    {
        var (dir, project) = RequireProject();
        var r = await DotnetRunner.RunAsync(["build", project, "-c", configuration(), "-v", "q", "-nologo"], dir,
            l => host.Log(LogChannel.Build, l), TimeSpan.FromMinutes(10), ct);
        var (errors, warnings) = DotnetRunner.ParseDiagnostics(r.Output);
        if (r.Succeeded) return $"Build succeeded in {r.Elapsed.TotalSeconds:0.0} s with {warnings.Count} warning(s).";
        if (errors.Count == 0) errors.Add(string.Join("\n", r.Output.Split('\n').TakeLast(15)));
        return $"Build FAILED ({errors.Count} error(s)):\n" + string.Join("\n", errors.Take(30).Select(e => Shorten(e, dir)));
    }

    [KernelFunction, Description("Builds, then runs the app headless with '--selftest' for a few simulated seconds and returns the printed probe values. Use it to check the physics.")]
    public async Task<string> RunSelfTest([Description("Simulated seconds")] double seconds = 3, CancellationToken ct = default)
    {
        string build = await BuildProject(ct);
        if (!build.StartsWith("Build succeeded", StringComparison.Ordinal)) return build;
        var (dir, project) = RequireProject();
        var r = await DotnetRunner.RunAsync(["run", "--project", project, "-c", configuration(), "--no-build", "--", "--selftest", "--seconds",
            seconds.ToString(CultureInfo.InvariantCulture)], dir, l => host.Log(LogChannel.Output, l), TimeSpan.FromMinutes(5), ct);
        string output = string.Join("\n", r.Output.Split('\n').Where(l => !l.StartsWith('>')).TakeLast(40));
        if (r.TimedOut) return "Self-test timed out after 5 minutes (the scene may be too heavy, or the app ignores --selftest).\n" + output;
        return (r.ExitCode == 0 ? "Self-test finished:\n" : $"Self-test FAILED (exit code {r.ExitCode}):\n") + output;
    }

    [KernelFunction, Description("Starts the app for the user in its own window (does not wait). Build first.")]
    public async Task<string> LaunchApp()
    {
        RequireProject();
        await host.LaunchProjectAsync();
        return "The app was started in its own window.";
    }

    [KernelFunction, Description("Adds a NuGet package reference to the project ('dotnet add package').")]
    public async Task<string> AddNuGetPackage(string packageId, [Description("Version, blank for latest")] string version = "", CancellationToken ct = default)
    {
        var (dir, project) = RequireProject();
        var args = new List<string> { "add", project, "package", packageId };
        if (!string.IsNullOrWhiteSpace(version)) args.AddRange(["--version", version]);
        var r = await DotnetRunner.RunAsync(args, dir, l => host.Log(LogChannel.Build, l), TimeSpan.FromMinutes(5), ct);
        await host.FileChangedAsync(project);
        return r.Succeeded ? $"Added {packageId} {version}".Trim() + "." : "Failed:\n" + string.Join("\n", r.Output.Split('\n').TakeLast(12));
    }

    [KernelFunction, Description("Runs another dotnet CLI command in the project folder. Allowed: restore, build, clean, list, add, remove, new, format, test.")]
    public async Task<string> RunDotnetCommand([Description("Arguments after 'dotnet', e.g. 'list package'")] string arguments, CancellationToken ct = default)
    {
        var (dir, _) = RequireProject();
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !AllowedCommands.Contains(parts[0])) return $"Not allowed: dotnet {arguments}. Allowed: {string.Join(", ", AllowedCommands)}.";
        var r = await DotnetRunner.RunAsync(parts, dir, l => host.Log(LogChannel.Build, l), TimeSpan.FromMinutes(5), ct);
        await host.FileChangedAsync(dir);
        return $"exit code {r.ExitCode}\n" + string.Join("\n", r.Output.Split('\n').TakeLast(30));
    }

    private (string Dir, string Project) RequireProject()
    {
        if (host.ProjectFile is not { } p) throw new InvalidOperationException("No project with a .csproj is open. Create one with CreateProjectFromTemplate first.");
        return (Path.GetDirectoryName(p)!, p);
    }

    private static string Shorten(string line, string dir) => line.Replace(dir + Path.DirectorySeparatorChar, "", StringComparison.OrdinalIgnoreCase);
}
