using System.Text;
using System.Text.RegularExpressions;

namespace Chrono.Net.AppGen.Projects;

public enum AppKind { Desktop, Console }

public sealed record GeneratorOptions(string ChronoNetVersion, string AvaloniaVersion, string? LocalPackageFeed);

/// <summary>
/// Writes a complete, buildable Chrono.Net app. Every project shares the same shell (Program, Scene, Build helpers,
/// csproj, nuget.config, README); a template only contributes the body of the <c>Simulation</c> class.
/// </summary>
public static partial class ProjectGenerator
{
    /// <summary>Creates <paramref name="name"/> under <paramref name="parentDirectory"/> and returns the .csproj path.</summary>
    public static string Create(ProjectTemplate template, string parentDirectory, string name, GeneratorOptions options)
    {
        string ns = ToIdentifier(name);
        string dir = Path.Combine(parentDirectory, name);
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
            throw new IOException($"The folder '{dir}' already exists and is not empty. Choose another project name.");
        Directory.CreateDirectory(dir);

        void Write(string file, string text) => File.WriteAllText(Path.Combine(dir, file), text.Replace("{{Ns}}", ns).ReplaceLineEndings("\r\n"), new UTF8Encoding(false));

        Write($"{name}.csproj", Csproj(template, options));
        if (ResolveFeed(options.LocalPackageFeed) is { } feed) Write("nuget.config", NugetConfig(feed));
        Write("Scene.cs", SceneFile);
        Write("Build.cs", BuildFile);
        Write("Simulation.cs", SimulationFile(template));
        Write("Program.cs", template.Kind == AppKind.Desktop ? DesktopProgram : ConsoleProgram);
        Write("README.md", Readme(template, name));
        foreach (var (file, content) in template.ExtraFiles) Write(file, content);
        return Path.Combine(dir, $"{name}.csproj");
    }

    /// <summary>A C# identifier for the project namespace ("My App 2" → "MyApp2").</summary>
    public static string ToIdentifier(string name)
    {
        var s = NonIdentifier().Replace(name, "");
        if (s.Length == 0) s = "ChronoApp";
        return char.IsDigit(s[0]) ? "App" + s : s;
    }

    /// <summary>
    /// The configured package folder, or one auto-detected from a Chrono.Net repository build (<c>artifacts/packages</c>
    /// above the app), so projects work before the packages are on nuget.org.
    /// </summary>
    public static string? ResolveFeed(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Directory.Exists(configured) ? Path.GetFullPath(configured) : null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "artifacts", "packages");
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "Chrono.Net.*.nupkg").Any()) return candidate;
        }
        return null;
    }

    [GeneratedRegex(@"[^A-Za-z0-9_]")]
    private static partial Regex NonIdentifier();

    // ------------------------------------------------------------------ files

    private static string Csproj(ProjectTemplate t, GeneratorOptions o)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <OutputType>{(t.Kind == AppKind.Desktop ? "WinExe" : "Exe")}</OutputType>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("    <RootNamespace>{{Ns}}</RootNamespace>");
        sb.AppendLine("    <!-- The Chrono native engine ships for win-x64. -->");
        sb.AppendLine("    <RuntimeIdentifier>win-x64</RuntimeIdentifier>");
        sb.AppendLine("    <SelfContained>false</SelfContained>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        if (t.Kind == AppKind.Desktop)
        {
            foreach (var p in new[] { "Avalonia", "Avalonia.Desktop", "Avalonia.Themes.Fluent", "Avalonia.Fonts.Inter" })
                sb.AppendLine($"    <PackageReference Include=\"{p}\" Version=\"{o.AvaloniaVersion}\" />");
            sb.AppendLine($"    <PackageReference Include=\"Chrono.Net.Avalonia\" Version=\"{o.ChronoNetVersion}\" />");
        }
        sb.AppendLine($"    <PackageReference Include=\"Chrono.Net\" Version=\"{o.ChronoNetVersion}\" />");
        sb.AppendLine("  </ItemGroup>");
        if (t.ExtraFiles.Any(f => f.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine();
            sb.AppendLine("  <ItemGroup>");
            sb.AppendLine("    <None Update=\"*.json\" CopyToOutputDirectory=\"PreserveNewest\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        sb.AppendLine();
        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    private static string NugetConfig(string feed) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <!-- Generated by ChronoAppGen: Chrono.Net packages from a local build, everything else from nuget.org. -->
        <configuration>
          <packageSources>
            <clear />
            <add key="chrono-local" value="{feed}" />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
          </packageSources>
          <packageSourceMapping>
            <packageSource key="chrono-local">
              <package pattern="Chrono.Net*" />
            </packageSource>
            <packageSource key="nuget.org">
              <package pattern="*" />
            </packageSource>
          </packageSourceMapping>
        </configuration>
        """;

    private static string SimulationFile(ProjectTemplate t) => $$"""
        using Chrono.Net;
        {{(t.RequiresVehicle ? "using Chrono.Net.Vehicles;\n" : "")}}
        namespace {{"{{Ns}}"}};

        /// <summary>{{Escape(t.Description)}}</summary>
        public static partial class Simulation
        {
            public const string Title = "{{Escape(t.Name)}}";
            public const string Category = "{{Escape(t.Category)}}";
            public const string Description = "{{Escape(t.Description)}}";

        {{Indent(t.SimulationCode.Trim('\r', '\n'), 4)}}
        }
        """;

    private static string Readme(ProjectTemplate t, string name) => $"""
        # {name}

        {t.Description}

        Generated by **ChronoAppGen** (Chrono.Net App Generator) from the *{t.Name}* template ({t.Category}).
        Made with Chrono.Net — Gravicode Studios, led by Kang Fadhil.

        ## Run

        ```powershell
        dotnet run                       # {(t.Kind == AppKind.Desktop ? "opens the simulator window" : "runs the simulation in the console")}
        dotnet run -- --selftest         # steps the scene headless and prints the probe values
        ```

        ## Where to change things

        | File | What it holds |
        |---|---|
        | `Simulation.cs` | The scene: bodies, joints, motors, forces and the probes that are plotted |
        | `Scene.cs` | Scene container (system, camera, probes, step override) and the headless runner |
        | `Build.cs` | Small helpers: ground, anchor, bars, palette |
        | `Program.cs` | {(t.Kind == AppKind.Desktop ? "Avalonia window: 3D viewport, transport controls and live plots" : "Console entry point")} |
        """;

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Indent(string code, int spaces)
    {
        string pad = new(' ', spaces);
        return string.Join("\n", code.Replace("\r\n", "\n").Split('\n').Select(l => l.Length == 0 ? l : pad + l));
    }

    // ------------------------------------------------------------------ shared shell

    private const string SceneFile = """
        using System.Diagnostics;
        using System.Globalization;
        using Chrono.Net;

        namespace {{Ns}};

        /// <summary>A value plotted and printed while the simulation runs.</summary>
        public sealed record Probe(string Name, string Unit, Func<double> Read);

        /// <summary>Everything the window (or the headless runner) needs to show one simulation.</summary>
        public sealed class Scene(ChSystem system) : IDisposable
        {
            public ChSystem System { get; } = system;
            /// <summary>Fixed integration step (s).</summary>
            public double TimeStep { get; init; } = 0.002;
            /// <summary>Vehicle worlds are Z-up; everything else is Y-up.</summary>
            public bool UpIsZ { get; init; }

            public Vec3 CameraTarget { get; set; }
            public double CameraDistance { get; set; } = 10;
            public double CameraYawDeg { get; set; } = 35;
            public double CameraPitchDeg { get; set; } = 22;
            /// <summary>Body the camera follows (e.g. a vehicle chassis).</summary>
            public ChBody? Follow { get; set; }

            /// <summary>Replaces <c>System.Step</c> (vehicles step through their driver and terrain).</summary>
            public Action<double>? StepOverride { get; set; }
            /// <summary>Lines drawn between two moving points each frame (cables, ropes, springs).</summary>
            public List<(Func<Vec3> From, Func<Vec3> To)> Tethers { get; } = [];
            public List<Probe> Probes { get; } = [];
            /// <summary>Extra objects disposed with the scene (vehicles, terrain, materials).</summary>
            public List<IDisposable> Owned { get; } = [];

            public Scene Probe(string name, string unit, Func<double> read)
            {
                Probes.Add(new Probe(name, unit, read));
                return this;
            }

            public Scene Tether(Func<Vec3> from, Func<Vec3> to)
            {
                Tethers.Add((from, to));
                return this;
            }

            public void Step(double dt)
            {
                if (StepOverride is { } step) step(dt);
                else System.Step(dt);
            }

            public void Dispose()
            {
                System.Dispose();
                foreach (var d in Owned) d.Dispose();
            }
        }

        /// <summary>Steps a scene without a window and prints the probes (used by <c>--selftest</c>).</summary>
        public static class Runner
        {
            public static int StepAndPrint(Scene scene, double seconds, TextWriter? output = null)
            {
                output ??= Console.Out;
                var sw = Stopwatch.StartNew();
                double every = Math.Max(0.25, seconds / 8), next = 0;
                while (scene.System.Time < seconds - 1e-9)
                {
                    scene.Step(scene.TimeStep);
                    if (scene.System.Time + 1e-9 >= next)
                    {
                        output.WriteLine(Format(scene));
                        next += every;
                    }
                }
                output.WriteLine(Format(scene));
                output.WriteLine($"OK  {scene.System.Bodies.Count} bodies, {scene.System.Links.Count} links, " +
                                 $"{scene.System.Time:0.00} s simulated in {sw.Elapsed.TotalSeconds:0.00} s");
                return 0;
            }

            private static string Format(Scene scene) =>
                string.Create(CultureInfo.InvariantCulture, $"t={scene.System.Time,6:0.00}s  ") +
                string.Join("  ", scene.Probes.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Name}={p.Read():0.###}{(p.Unit.Length > 0 ? " " + p.Unit : "")}")));

            /// <summary>Reads <c>--seconds N</c> from the command line.</summary>
            public static double Seconds(string[] args, double fallback)
            {
                int i = Array.IndexOf(args, "--seconds");
                return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var s) ? s : fallback;
            }
        }
        """;

    private const string BuildFile = """
        using Chrono.Net;

        namespace {{Ns}};

        /// <summary>Small helpers that keep scene code readable.</summary>
        public static class Build
        {
            public static double Rad(double degrees) => degrees * Math.PI / 180;

            /// <summary>Fixed ground slab whose top surface is at height 0 (Y-up).</summary>
            public static ChBody Ground(ChSystem sys, ContactMaterial mat, double size = 30, Color? color = null) =>
                sys.Add(ChBody.CreateBox(new(size, 1, size), 1000, mat).At(0, -0.5, 0).Fixed().Named("ground")
                    .Colored(color ?? Color.FromHex("#2A3342")));

            /// <summary>Invisible, non-colliding fixed body used as the reference for joints and motors.</summary>
            public static ChBody Anchor(ChSystem sys) =>
                sys.Add(new ChBody { Name = "anchor", IsFixed = true, CollisionEnabled = false });

            /// <summary>A bar-shaped body spanning from <paramref name="a"/> to <paramref name="b"/> (no collision).</summary>
            public static ChBody Bar(ChSystem sys, Vec3 a, Vec3 b, double thickness, Color color, double density = 2700)
            {
                var d = b - a;
                var bar = ChBody.CreateBox(new(d.Length, thickness, thickness), density, collide: false);
                bar.Position = (a + b) * 0.5;
                bar.Rotation = Quat.FromTo(Vec3.UnitX, d);
                bar.SetColor(color);
                return sys.Add(bar);
            }

            public static readonly Color[] Palette =
            [
                Color.FromHex("#F28C28"), Color.FromHex("#18B6A3"), Color.FromHex("#3B82F6"), Color.FromHex("#EF4444"),
                Color.FromHex("#FACC15"), Color.FromHex("#8B5CF6"), Color.FromHex("#22C55E"), Color.FromHex("#EC4899"),
            ];
        }
        """;

    private const string ConsoleProgram = """
        namespace {{Ns}};

        internal static class Program
        {
            public static int Main(string[] args)
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.WriteLine($"{Simulation.Title} · Chrono {Chrono.Net.ChronoEngine.ChronoVersion}");
                return Simulation.Run(args);
            }
        }
        """;

    private const string DesktopProgram = """
        using Avalonia;
        using Avalonia.Controls;
        using Avalonia.Controls.ApplicationLifetimes;
        using Avalonia.Controls.Primitives;
        using Avalonia.Layout;
        using Avalonia.Markup.Xaml.Styling;
        using Avalonia.Media;
        using Avalonia.Themes.Fluent;
        using Chrono.Net;
        using Chrono.Net.Avalonia;
        using Chrono.Net.Rendering;

        namespace {{Ns}};

        internal static class Program
        {
            [STAThread]
            public static int Main(string[] args)
            {
                // Headless check: steps the scene and prints the probes, no window.
                if (args.Contains("--selftest"))
                {
                    using var scene = Simulation.Create();
                    return Runner.StepAndPrint(scene, Runner.Seconds(args, 3));
                }
                AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
                return 0;
            }
        }

        public sealed class App : Application
        {
            public override void Initialize()
            {
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
                Styles.Add(new FluentTheme());
                Styles.Add(new StyleInclude(new Uri("avares://{{Ns}}")) { Source = new Uri("avares://Chrono.Net.Avalonia/Themes/ChronoTheme.axaml") });
            }

            public override void OnFrameworkInitializationCompleted()
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
                base.OnFrameworkInitializationCompleted();
            }
        }

        /// <summary>3D viewport, transport controls and live plots around <see cref="Simulation.Create"/>.</summary>
        public sealed class MainWindow : Window
        {
            private readonly ChronoViewport _viewport = new() { RenderScale = 1 };
            private readonly ChronoPlot _plot = new() { Height = 200, Stacked = true, TimeWindow = 10 };
            private readonly Button _run = new() { Content = "Run", MinWidth = 88, Classes = { "transport", "primary" } };
            private readonly Button _step = new() { Content = "Step", Margin = new Thickness(8, 0, 0, 0), Classes = { "transport" } };
            private readonly Button _reset = new() { Content = "Reset", Margin = new Thickness(8, 0, 0, 0), Classes = { "transport" } };
            private readonly Slider _speed = new() { Minimum = 0.1, Maximum = 2, Value = 1, Width = 140, Margin = new Thickness(18, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            private readonly TextBlock _readout = new() { Classes = { "mono", "muted" }, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            private Scene? _scene;
            private RenderWorld? _world;
            private SimulationHost? _host;
            private readonly List<PlotSeries> _series = [];
            private readonly List<RenderLine> _tethers = [];
            private int _frame;

            public MainWindow()
            {
                Title = Simulation.Title;
                Width = 1280;
                Height = 820;

                var header = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 12) };
                header.Children.Add(new TextBlock { Text = Simulation.Category.ToUpperInvariant(), Classes = { "eyebrow" } });
                header.Children.Add(new TextBlock { Text = Simulation.Title, FontSize = 30, Classes = { "display" } });
                header.Children.Add(new TextBlock { Text = Simulation.Description, TextWrapping = TextWrapping.Wrap, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left, Classes = { "muted" } });

                var transport = new DockPanel { Margin = new Thickness(0, 10) };
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                buttons.Children.Add(_run);
                buttons.Children.Add(_step);
                buttons.Children.Add(_reset);
                buttons.Children.Add(new TextBlock { Text = "Speed", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0), Classes = { "muted" } });
                buttons.Children.Add(_speed);
                DockPanel.SetDock(buttons, Dock.Left);
                transport.Children.Add(buttons);
                transport.Children.Add(_readout);

                var stage = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = _viewport };
                var root = new Grid { Margin = new Thickness(20, 16), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
                Grid.SetRow(header, 0);
                Grid.SetRow(stage, 1);
                Grid.SetRow(transport, 2);
                Grid.SetRow(_plot, 3);
                root.Children.Add(header);
                root.Children.Add(stage);
                root.Children.Add(transport);
                root.Children.Add(_plot);
                Content = root;

                _run.Click += (_, _) => Toggle();
                _step.Click += (_, _) => { _host?.Pause(); UpdateRunLabel(); _host?.StepOnce(); };
                _reset.Click += (_, _) => Load(autoRun: _host?.IsRunning ?? true);
                _speed.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty && _host is not null) _host.Speed = _speed.Value; };
                Load(autoRun: true);
            }

            private void Load(bool autoRun)
            {
                _host?.Dispose();
                _scene?.Dispose();
                _scene = Simulation.Create();
                _world = new RenderWorld(_scene.System, _scene.UpIsZ);
                _tethers.Clear();
                foreach (var _ in _scene.Tethers)
                {
                    var line = new RenderLine { Color = new(0.62f, 0.68f, 0.76f), Width = 2.5f };
                    _tethers.Add(line);
                    _world.Lines.Add(line);
                }
                UpdateTethers();
                _viewport.World = _world;
                _viewport.FollowBody = _scene.Follow;
                var cam = _viewport.Camera;
                cam.UpIsZ = _scene.UpIsZ;
                cam.Target = _scene.CameraTarget.ToVector3();
                cam.Distance = (float)_scene.CameraDistance;
                cam.Yaw = (float)(_scene.CameraYawDeg * Math.PI / 180);
                cam.Pitch = (float)(_scene.CameraPitchDeg * Math.PI / 180);

                _plot.Clear();
                _series.Clear();
                foreach (var p in _scene.Probes) _series.Add(_plot.AddSeries(p.Name, p.Unit));
                _plot.IsVisible = _series.Count > 0;

                _host = new SimulationHost(_scene.Step, _scene.TimeStep) { Speed = _speed.Value };
                _host.Frame += OnFrame;
                _viewport.Refresh();
                if (autoRun) _host.Start();
                UpdateRunLabel();
            }

            private void OnFrame()
            {
                if (_scene is null || _world is null || _host is null) return;
                UpdateTethers();
                _world.Update();
                double t = _scene.System.Time;
                for (int i = 0; i < _series.Count; i++) _series[i].Add(t, _scene.Probes[i].Read());
                _viewport.Refresh();
                _plot.Refresh();
                if (_frame++ % 10 == 0)
                    _readout.Text = $"t {t:0.00} s   {_scene.System.Bodies.Count} bodies   {_scene.System.ContactCount} contacts   " +
                                    (_host.IsRunning ? $"{_host.RealTimeFactor:0.00}× real time" : "paused");
            }

            private void UpdateTethers()
            {
                if (_scene is null) return;
                for (int i = 0; i < _tethers.Count; i++)
                    _tethers[i].Points = [_scene.Tethers[i].From().ToVector3(), _scene.Tethers[i].To().ToVector3()];
            }

            private void Toggle()
            {
                if (_host is null) return;
                if (_host.IsRunning) _host.Pause(); else _host.Start();
                UpdateRunLabel();
            }

            private void UpdateRunLabel() => _run.Content = _host?.IsRunning == true ? "Pause" : "Run";

            protected override void OnClosed(EventArgs e)
            {
                _host?.Dispose();
                _scene?.Dispose();
                base.OnClosed(e);
            }
        }
        """;
}
