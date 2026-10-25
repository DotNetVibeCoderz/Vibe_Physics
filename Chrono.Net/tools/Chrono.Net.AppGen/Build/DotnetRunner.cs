using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Chrono.Net.AppGen.Build;

public sealed record ProcessResult(int ExitCode, string Output, bool TimedOut, TimeSpan Elapsed)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>Runs <c>dotnet</c> commands with streamed output, cancellation and an optional timeout.</summary>
public static partial class DotnetRunner
{
    public static async Task<ProcessResult> RunAsync(IEnumerable<string> args, string workingDirectory, Action<string>? onLine = null,
        TimeSpan? timeout = null, CancellationToken ct = default, string fileName = "dotnet")
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["MSBUILDTERMINALLOGGER"] = "off";

        var sb = new StringBuilder();
        var sw = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        void Line(string? l)
        {
            if (l is null) return;
            lock (sb) sb.AppendLine(l);
            onLine?.Invoke(l);
        }
        process.OutputDataReceived += (_, e) => Line(e.Data);
        process.ErrorDataReceived += (_, e) => Line(e.Data);
        onLine?.Invoke($"> {fileName} {string.Join(' ', psi.ArgumentList.Select(Quote))}");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) timeoutCts.CancelAfter(t);
        bool timedOut = false;
        try { await process.WaitForExitAsync(timeoutCts.Token); }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
        }
        // Flush the async readers.
        process.WaitForExit();
        lock (sb) return new ProcessResult(timedOut ? -1 : process.ExitCode, sb.ToString(), timedOut, sw.Elapsed);
    }

    private static string Quote(string a) => a.Contains(' ') ? $"\"{a}\"" : a;

    /// <summary>Distinct compiler/MSBuild errors (and the first warnings) from build output, for Jack and the status bar.</summary>
    public static (List<string> Errors, List<string> Warnings) ParseDiagnostics(string output)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        foreach (Match m in DiagnosticRegex().Matches(output))
        {
            string line = m.Value.Trim();
            // Drop the trailing "[project.csproj]" that MSBuild repeats on every line.
            int bracket = line.LastIndexOf(" [", StringComparison.Ordinal);
            if (bracket > 0 && line.EndsWith(']')) line = line[..bracket];
            var list = m.Groups["kind"].Value == "error" ? errors : warnings;
            if (!list.Contains(line)) list.Add(line);
        }
        return (errors, warnings);
    }

    [GeneratedRegex(@"^.*?:\s*(?<kind>error|warning)\s+[A-Z]+\d+:.*$", RegexOptions.Multiline)]
    private static partial Regex DiagnosticRegex();
}
