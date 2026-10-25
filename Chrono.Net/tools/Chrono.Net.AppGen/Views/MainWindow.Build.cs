using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using Chrono.Net.AppGen.Ai;
using Chrono.Net.AppGen.Build;

namespace Chrono.Net.AppGen.Views;

// Build, run, self-test and deploy.
public sealed partial class MainWindow
{
    private string ProjectDir => Path.GetDirectoryName(_projectFile!)!;

    private void SetBuildStatus(string text, global::Avalonia.Media.IBrush brush)
    {
        _statusBuild.Text = text;
        _statusBuild.Foreground = brush;
    }

    /// <summary>dotnet build; returns true on success. Errors go to the Build log and enable "Ask Jack to fix".</summary>
    private async Task<bool> BuildAsync()
    {
        if (_projectFile is null || _busyBuild) return false;
        SaveAll();
        _busyBuild = true;
        _buildCts = new CancellationTokenSource();
        UpdateCommandStates();
        ShowPanel("logs", true);
        SelectLog(LogChannel.Build);
        _logs[LogChannel.Build].Clear();
        SetBuildStatus("● Building…", Icons.Signal);
        try
        {
            var r = await DotnetRunner.RunAsync(["build", _projectFile, "-c", _s.BuildConfiguration, "-nologo", "-v", "m"], ProjectDir,
                l => Dispatcher.UIThread.Post(() => AppendLog(LogChannel.Build, l)), TimeSpan.FromMinutes(10), _buildCts.Token);
            var (errors, warnings) = DotnetRunner.ParseDiagnostics(r.Output);
            if (r.Succeeded)
            {
                SetBuildStatus($"✓ Build succeeded · {warnings.Count} warning{(warnings.Count == 1 ? "" : "s")} · {r.Elapsed.TotalSeconds:0.0}s", Icons.Trace);
                _lastBuildErrors = "";
                _fixBuild.IsVisible = false;
                return true;
            }
            SetBuildStatus(r.TimedOut ? "✗ Build timed out" : $"✗ Build failed · {errors.Count} error{(errors.Count == 1 ? "" : "s")}", Icons.Fault);
            _lastBuildErrors = string.Join("\n", errors.Take(25).Select(e => e.Replace(ProjectDir + Path.DirectorySeparatorChar, "")));
            _fixBuild.IsVisible = errors.Count > 0;
            return false;
        }
        catch (OperationCanceledException)
        {
            SetBuildStatus("Build cancelled", Icons.Muted);
            return false;
        }
        finally
        {
            _busyBuild = false;
            UpdateCommandStates();
        }
    }

    /// <summary>Builds, then starts the app with dotnet run (output in the Output log). Stop kills it.</summary>
    private async Task RunAsync()
    {
        if (_projectFile is null || _runProcess is { HasExited: false }) return;
        if (!await BuildAsync()) return;
        SelectLog(LogChannel.Output);
        AppendLog(LogChannel.Output, $"▶ Running {Path.GetFileNameWithoutExtension(_projectFile)}…");
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = ProjectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "run", "--project", _projectFile, "-c", _s.BuildConfiguration, "--no-build" }) psi.ArgumentList.Add(a);
        try
        {
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) Dispatcher.UIThread.Post(() => AppendLog(LogChannel.Output, e.Data)); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) Dispatcher.UIThread.Post(() => AppendLog(LogChannel.Output, e.Data)); };
            p.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                AppendLog(LogChannel.Output, $"■ App exited (code {SafeExitCode(p)}).");
                SetBuildStatus("App stopped", Icons.Muted);
                _runProcess = null;
                UpdateCommandStates();
            });
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _runProcess = p;
            SetBuildStatus("▶ Running", Icons.Signal);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            AppendLog(LogChannel.Output, $"Could not start dotnet: {ex.Message}");
        }
        UpdateCommandStates();
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }

    private void StopRun()
    {
        _buildCts?.Cancel();
        _runCts?.Cancel();
        if (_runProcess is { HasExited: false } p)
        {
            try { p.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            AppendLog(LogChannel.Output, "■ Stopped.");
        }
    }

    private async Task SelfTestAsync()
    {
        if (_projectFile is null) return;
        if (!await BuildAsync()) return;
        SelectLog(LogChannel.Output);
        AppendLog(LogChannel.Output, "▶ Self-test (headless, 3 s)…");
        _runCts = new CancellationTokenSource();
        var r = await DotnetRunner.RunAsync(["run", "--project", _projectFile, "-c", _s.BuildConfiguration, "--no-build", "--", "--selftest", "--seconds", "3"], ProjectDir,
            l => Dispatcher.UIThread.Post(() => AppendLog(LogChannel.Output, l)), TimeSpan.FromMinutes(5), _runCts.Token);
        SetBuildStatus(r.Succeeded ? "✓ Self-test passed" : "✗ Self-test failed", r.Succeeded ? Icons.Trace : Icons.Fault);
    }

    /// <summary>dotnet publish to &lt;project&gt;/publish/&lt;rid&gt; with the deploy settings.</summary>
    private async Task DeployAsync()
    {
        if (_projectFile is null || _busyBuild) return;
        SaveAll();
        string output = Path.Combine(ProjectDir, "publish", _s.DeployRuntime);
        _busyBuild = true;
        _buildCts = new CancellationTokenSource();
        UpdateCommandStates();
        ShowPanel("logs", true);
        SelectLog(LogChannel.Build);
        SetBuildStatus("● Publishing…", Icons.Signal);
        try
        {
            var args = new List<string> { "publish", _projectFile, "-c", "Release", "-r", _s.DeployRuntime, "-o", output, "-nologo",
                "--self-contained", _s.DeploySelfContained ? "true" : "false" };
            if (_s.DeploySingleFile) args.Add("-p:PublishSingleFile=true");
            var r = await DotnetRunner.RunAsync(args, ProjectDir, l => Dispatcher.UIThread.Post(() => AppendLog(LogChannel.Build, l)), TimeSpan.FromMinutes(15), _buildCts.Token);
            if (!r.Succeeded)
            {
                SetBuildStatus("✗ Publish failed", Icons.Fault);
                return;
            }
            SetBuildStatus("✓ Published", Icons.Trace);
            string exe = Directory.EnumerateFiles(output, "*.exe").FirstOrDefault() ?? output;
            var choice = await Dialogs.ConfirmAsync(this, "Deploy", $"Published to\n{output}\n\nCopy this folder to another Windows x64 PC to run the app{(_s.DeploySelfContained ? "" : " (it needs the .NET 10 runtime)")}.",
                "Open folder", "Close");
            if (choice == "Open folder")
            {
                try { Process.Start("explorer.exe", exe == output ? $"\"{output}\"" : $"/select,\"{exe}\""); }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        catch (OperationCanceledException) { SetBuildStatus("Publish cancelled", Icons.Muted); }
        finally
        {
            _busyBuild = false;
            UpdateCommandStates();
        }
    }

    public Task LaunchProjectAsync() => Dispatcher.UIThread.InvokeAsync(RunAsync);
}
