using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Chrono.Net.AppGen.Ai;
using Chrono.Net.AppGen.Settings;
using Chrono.Net.AppGen.Views;

/// <summary>
/// Headless screenshot of ChronoAppGen: opens a project and replays a Jack conversation through the chat panel's
/// public API. Target syntax: <c>appgen=&lt;projectDir&gt;</c>.
/// </summary>
internal static class AppGenShot
{
    private static bool _stylesAdded;

    public static void Capture(string projectDir, string file)
    {
        EnsureStyles();

        // A throw-away copy of the default settings (no keys); DeepSeek is marked configured for the picker.
        string source = Path.Combine(FindRepo(), "tools", "Chrono.Net.AppGen", "App.config");
        string temp = Path.Combine(Path.GetTempPath(), "chronoappgen-shot.config");
        File.Copy(source, temp, overwrite: true);
        var settings = AppSettings.Load(temp);
        settings.Providers[ProviderKind.DeepSeek].ApiKey = "sk-demo";
        settings.ActiveProvider = ProviderKind.DeepSeek;
        settings.ActiveModel = "deepseek-v4-flash";

        var w = new MainWindow(settings, projectDir) { Width = 1640, Height = 1000 };
        w.Show();

        var chat = w.Chat;
        chat.AddUser("Buatkan aplikasi simulasi bandul ganda (double pendulum): 2 bandul berdampingan yang posisi awalnya berbeda 0,01 rad, supaya efek chaos terlihat. Plot sudut kedua bandul bawah dan selisihnya.", []);
        var m = chat.BeginJack();
        var calls = new (string Plugin, string Fn, string Args, double Secs)[]
        {
            ("chrono", "GetChronoGuide", "", 0.0),
            ("chrono", "CreateProjectFromTemplate", "templateId=blank-desktop, projectName=ChaosPendulum", 0.4),
            ("chrono", "GetSampleSource", "sampleId=double-pendulum", 0.0),
            ("workspace", "WriteFile", "path=Simulation.cs", 0.0),
            ("build", "BuildProject", "", 6.8),
            ("build", "RunSelfTest", "seconds=20", 5.5),
            ("workspace", "ReplaceInFile", "path=Simulation.cs", 0.0),
            ("workspace", "ReplaceInFile", "path=Program.cs", 0.0),
            ("build", "RunSelfTest", "seconds=20", 5.2),
        };
        foreach (var (plugin, fn, args, secs) in calls)
        {
            var c = new ToolCall { Plugin = plugin, Function = fn, Arguments = args };
            m.ToolStarted(c);
            c.Elapsed = TimeSpan.FromSeconds(secs);
            c.Done = true;
            m.ToolFinished(c);
        }
        m.Append("""
            **ChaosPendulum** sudah jadi dan jalan.

            **Isi scene** (`Simulation.cs`)
            - Dua bandul ganda identik (lengan 1,00 m + 0,90 m, sendi revolute sumbu Z), digantung berdampingan.
            - Bandul B sama persis, hanya lengan atasnya digeser **0,01 rad**.
            - Plot: sudut bandul bawah A, sudut bandul bawah B, dan |A − B|.

            **Build & self-test**
            - `dotnet build`: sukses, 0 warning. Self-test 20 s selesai dalam 1,4 s.
            - |A−B| = 1,3° (2,5 s) → 21,9° (5 s) → 128° (12,5 s); mulai terpisah > 30° pada **t ≈ 5,04 s**.
            - Energi total 2232 J ± 1 J selama 20 s (drift 0,05 %).

            Coba berikutnya: slider "selisih awal" untuk melihat eksponen Lyapunov.
            """);
        m.Finish();

        w.WriteLog(LogChannel.Build, "> dotnet build ChaosPendulum.csproj -c Debug -nologo -v m");
        w.WriteLog(LogChannel.Build, "  ChaosPendulum -> ChaosPendulum\\bin\\Debug\\net10.0\\win-x64\\ChaosPendulum.dll");
        w.WriteLog(LogChannel.Build, "Build succeeded.");
        w.WriteLog(LogChannel.Build, "    0 Warning(s)");
        w.WriteLog(LogChannel.Build, "    0 Error(s)");

        var frame = w.CaptureRenderedFrame();
        frame?.Save(file);
        Console.WriteLine($"saved {file}");
        w.Close();
    }

    /// <summary>
    /// Runs a real Jack turn inside the window (settings from <paramref name="configPath"/>, which holds the keys),
    /// pumping the headless dispatcher until it finishes, then captures the window.
    /// </summary>
    public static void CaptureLive(string projectDir, string prompt, string configPath, string file, TimeSpan timeout)
    {
        EnsureStyles();
        var settings = AppSettings.Load(configPath);
        var w = new MainWindow(settings, projectDir) { Width = 1640, Height = 1000 };
        w.Show();
        var task = w.AskJackAsync(prompt);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && sw.Elapsed < timeout)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(25);
        }
        for (int i = 0; i < 20; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(25); }
        Console.WriteLine(task.IsCompleted ? $"Jack finished in {sw.Elapsed.TotalSeconds:0}s" : "Jack timed out");
        w.CaptureRenderedFrame()?.Save(file);
        Console.WriteLine($"saved {file}");
        w.Close();
    }

    /// <summary>Captures the New Project (kind "new") or Settings (kind "settings") dialog.</summary>
    public static void CaptureDialog(string kind, string file)
    {
        EnsureStyles();
        string temp = Path.Combine(Path.GetTempPath(), "chronoappgen-shot.config");
        File.Copy(Path.Combine(FindRepo(), "tools", "Chrono.Net.AppGen", "App.config"), temp, overwrite: true);
        var settings = AppSettings.Load(temp);
        settings.Providers[ProviderKind.DeepSeek].ApiKey = "sk-demo-key-not-real";
        Avalonia.Controls.Window w = kind == "new"
            ? new NewProjectDialog(settings.ProjectsRoot, startWithTemplates: true)
            : new SettingsDialog(settings, "Models");
        w.Show();
        w.CaptureRenderedFrame()?.Save(file);
        Console.WriteLine($"saved {file}");
        w.Close();
    }

    private static void EnsureStyles()
    {
        if (_stylesAdded) return;
        var baseUri = new Uri("avares://ChronoAppGen");
        Application.Current!.Styles.Add(new StyleInclude(baseUri) { Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml") });
        Application.Current.Styles.Add(new StyleInclude(baseUri) { Source = new Uri("avares://ChronoAppGen/Themes/AppGenTheme.axaml") });
        _stylesAdded = true;
    }

    private static string FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Chrono.Net.slnx"))) return d.FullName;
        return Directory.GetCurrentDirectory();
    }
}
