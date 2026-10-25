using Chrono.Net.AppGen.Ai;
using Chrono.Net.AppGen.Settings;

namespace Chrono.Net.AppGen;

public static partial class CommandLine
{
    /// <summary>
    /// <c>--agent "prompt" --project dir [--provider p] [--model m] [--image file]</c>: one Jack turn on the console,
    /// with the same plugins as the IDE. The project folder may be empty (Jack then creates a project in it).
    /// </summary>
    private static async Task<int> RunAgentAsync(string[] args)
    {
        if (args.Length < 2) return Usage();
        string prompt = args[1];
        string? Opt(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

        var settings = AppSettings.Load();
        var provider = Opt("--provider") is { } pn ? Enum.Parse<ProviderKind>(pn, ignoreCase: true) : settings.ActiveProvider;
        string model = Opt("--model") ?? (settings.ActiveProvider == provider && settings.ActiveModel.Length > 0 ? settings.ActiveModel : settings.Providers[provider].Models.FirstOrDefault() ?? "");
        string? projectDir = Opt("--project");
        if (projectDir is not null)
        {
            projectDir = Path.GetFullPath(projectDir);
            settings.ProjectsRoot = Path.GetDirectoryName(projectDir)!;   // new projects are created next to it (not saved)
        }

        var host = new ConsoleHost();
        if (projectDir is not null && Directory.Exists(projectDir) && Directory.EnumerateFiles(projectDir, "*.csproj").Any())
            await host.OpenProjectAsync(projectDir);

        var attachments = new List<ChatAttachment>();
        if (Opt("--image") is { } img)
            attachments.Add(new ChatAttachment(Path.GetFileName(img), await File.ReadAllBytesAsync(img), Path.GetExtension(img).ToLowerInvariant() == ".png" ? "image/png" : "image/jpeg"));

        var jack = new JackAgent(settings, host);
        jack.ToolCallStarted += c => Console.WriteLine($"  ▸ {c.Plugin}.{c.Function}({c.Arguments})");
        jack.ToolCallFinished += c => Console.WriteLine($"    {(c.Failed ? "✗" : "✓")} {c.Elapsed.TotalSeconds:0.0}s {First(c.Result)}");
        Console.WriteLine($"Jack via {ProviderCatalog.DisplayName(provider)} / {model}");
        try
        {
            await foreach (var chunk in jack.SendAsync(prompt, attachments, provider, model)) Console.Write(chunk);
            Console.WriteLine();
            return 0;
        }
        catch (JackException ex)
        {
            Console.Error.WriteLine($"\nerror: {ex.Message}");
            return 1;
        }

        static string First(string? s) => s is null ? "" : (s.Split('\n')[0] is var l && l.Length > 110 ? l[..110] + "…" : l);
    }

    /// <summary>IDE host for the command line: file events are printed, launching is not supported.</summary>
    private sealed class ConsoleHost : IIdeHost
    {
        public string? ProjectRoot { get; private set; }
        public string? ProjectFile { get; private set; }
        public (string Path, string Text, int Line)? ActiveDocument { get; private set; }

        public Task OpenProjectAsync(string path)
        {
            ProjectRoot = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            ProjectFile = File.Exists(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? path
                : Directory.EnumerateFiles(ProjectRoot!, "*.csproj").FirstOrDefault();
            Console.WriteLine($"  [project] {ProjectFile ?? ProjectRoot}");
            return Task.CompletedTask;
        }

        public Task OpenFileAsync(string path, int line = 0)
        {
            if (File.Exists(path)) ActiveDocument = (path, File.ReadAllText(path), Math.Max(1, line));
            return Task.CompletedTask;
        }

        public Task FileChangedAsync(string path)
        {
            if (ActiveDocument is { } d && d.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                ActiveDocument = (path, File.ReadAllText(path), d.Line);
            return Task.CompletedTask;
        }

        public void Log(LogChannel channel, string text)
        {
            if (channel == LogChannel.Jack) Console.WriteLine($"  [{channel}] {text}");
        }

        public Task LaunchProjectAsync()
        {
            Console.WriteLine("  [launch] (not available on the command line)");
            return Task.CompletedTask;
        }
    }
}
