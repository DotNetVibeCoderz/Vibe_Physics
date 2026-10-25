using System.Text;
using Chrono.Net.AppGen.Build;
using Chrono.Net.AppGen.Projects;
using Chrono.Net.AppGen.Settings;

namespace Chrono.Net.AppGen;

/// <summary>
/// Headless commands, for scripting and for checking the generator without the UI:
/// <code>
/// ChronoAppGen --list-templates
/// ChronoAppGen --generate &lt;templateId&gt; &lt;parentDir&gt; [name]
/// ChronoAppGen --verify-templates &lt;workDir&gt; [templateId ...]   (generate, build, run --selftest)
/// ChronoAppGen --agent "&lt;prompt&gt;" --project &lt;dir&gt; [--provider DeepSeek] [--model m] [--image file.png]
/// </code>
/// </summary>
public static partial class CommandLine
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return args[0] switch
            {
                "--list-templates" => ListTemplates(),
                "--generate" => Generate(args),
                "--verify-templates" => await VerifyTemplatesAsync(args),
                "--agent" => await RunAgentAsync(args),
                _ => Usage(),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("ChronoAppGen — Chrono.Net App Generator. Made by Gravicode Studios, led by Kang Fadhil.");
        Console.WriteLine("  --list-templates");
        Console.WriteLine("  --generate <templateId> <parentDir> [name]");
        Console.WriteLine("  --verify-templates <workDir> [templateId ...]");
        Console.WriteLine("  --agent \"<prompt>\" --project <dir> [--provider <name>] [--model <id>] [--image <file>]");
        return 2;
    }

    private static int ListTemplates()
    {
        foreach (var t in TemplateCatalog.All)
            Console.WriteLine($"{t.Id,-24} {t.Category,-18} {t.Kind,-8} {t.Name}");
        return 0;
    }

    private static GeneratorOptions Options()
    {
        var s = AppSettings.Load();
        return new GeneratorOptions(s.ChronoNetVersion, s.AvaloniaVersion, s.LocalPackageFeed);
    }

    private static int Generate(string[] args)
    {
        if (args.Length < 3) return Usage();
        var t = TemplateCatalog.Find(args[1]) ?? throw new ArgumentException($"Unknown template '{args[1]}'. Run --list-templates.");
        string name = args.Length > 3 ? args[3] : ProjectGenerator.ToIdentifier(t.Name);
        string csproj = ProjectGenerator.Create(t, Path.GetFullPath(args[2]), name, Options());
        Console.WriteLine(csproj);
        return 0;
    }

    /// <summary>Generates each template, builds it and runs its --selftest; prints a pass/fail table.</summary>
    private static async Task<int> VerifyTemplatesAsync(string[] args)
    {
        if (args.Length < 2) return Usage();
        string work = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(work);
        var ids = args.Skip(2).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = Options();
        Console.WriteLine($"Package feed: {ProjectGenerator.ResolveFeed(options.LocalPackageFeed) ?? "nuget.org"}");
        int failures = 0;
        foreach (var t in TemplateCatalog.All.Where(t => ids.Count == 0 || ids.Contains(t.Id)))
        {
            string name = ProjectGenerator.ToIdentifier(t.Name);
            string dir = Path.Combine(work, name);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            ProjectGenerator.Create(t, work, name, options);
            var build = await DotnetRunner.RunAsync(["build", "-v", "q", "-nologo"], dir, timeout: TimeSpan.FromMinutes(5));
            string verdict;
            string detail = "";
            if (!build.Succeeded)
            {
                var (errors, _) = DotnetRunner.ParseDiagnostics(build.Output);
                verdict = "BUILD FAILED";
                detail = string.Join("\n      ", errors.Take(6).DefaultIfEmpty(build.Output.Trim().Split('\n').LastOrDefault() ?? ""));
            }
            else
            {
                var run = await DotnetRunner.RunAsync(["run", "--no-build", "--", "--selftest", "--seconds", "3"], dir, timeout: TimeSpan.FromMinutes(4));
                var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                verdict = run.Succeeded && lines.Any(l => l.StartsWith("OK ", StringComparison.Ordinal)) ? "ok" : run.TimedOut ? "TIMEOUT" : "RUN FAILED";
                detail = string.Join("\n      ", lines.Where(l => !l.StartsWith('>')).TakeLast(verdict == "ok" ? 2 : 8));
            }
            if (verdict != "ok") failures++;
            Console.WriteLine($"{t.Id,-24} {verdict}\n      {detail}");
        }
        Console.WriteLine(failures == 0 ? "All templates passed." : $"{failures} template(s) failed.");
        return failures == 0 ? 0 : 1;
    }
}
