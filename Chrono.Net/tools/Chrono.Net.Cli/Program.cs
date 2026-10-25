using System.Diagnostics;
using System.Globalization;
using System.Text;
using Chrono.Net;
using Chrono.Net.Rendering;
using Chrono.Net.Samples;
using Chrono.Net.Scenes;
using Spectre.Console;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
Console.OutputEncoding = Encoding.UTF8;

var cli = new Cli(args);
return cli.Run();

/// <summary>
/// <c>chrono</c> — the Chrono.Net command-line tool.
/// Made by Gravicode Studios, led by Kang Fadhil.
/// </summary>
internal sealed class Cli
{
    private readonly Dictionary<string, string> _opts;
    private readonly List<string> _positional;

    public Cli(string[] args) => _opts = ParseOptions(args, out _positional);

    public int Run()
    {
        if (_positional.Count == 0 || _opts.ContainsKey("help")) return Help();
        try
        {
            return _positional[0].ToLowerInvariant() switch
            {
                "info" => Info(),
                "list" => List(),
                "run" => RunScene(),
                "render" => Render(),
                "bench" => Bench(),
                "new" => NewScene(),
                "validate" => Validate(),
                "source" => Source(),
                _ => Help($"Unknown command '{_positional[0]}'."),
            };
        }
        catch (Exception ex) when (ex is ChronoException or IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    // ------------------------------------------------------------------ commands

    private static int Help(string? error = null)
    {
        if (error is not null) AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]\n");
        AnsiConsole.Write(new FigletText("Chrono.Net").Color(Spectre.Console.Color.Orange1));
        AnsiConsole.MarkupLine("[grey]Made by Gravicode Studios, led by Kang Fadhil[/]\n");
        var t = new Table().Border(TableBorder.Rounded).AddColumn("Command").AddColumn("Description");
        // Plain Text cells: the usage strings contain [brackets] that markup would parse as styles.
        void Row(string usage, string description) => t.AddRow(new Text(usage, new Style(Spectre.Console.Color.Orange1)), new Text(description));
        Row("info", "Engine, native library and feature information");
        Row("list", "List built-in samples");
        Row("run <scene.json|sample> [--duration s] [--dt s] [--csv file]", "Run a simulation headless, optionally export body trajectories");
        Row("render <scene.json|sample> --out file.png|.gif [--width] [--height] [--duration] [--fps]", "Render a still image or an animated GIF");
        Row("bench [--bodies n] [--steps n]", "Benchmark a falling-box pile");
        Row("new <file.json> [--template pendulum|stack|empty]", "Create a scene file");
        Row("validate <scene.json>", "Load a scene and report its contents");
        Row("source <sample>", "Print the C# source of a sample");
        AnsiConsole.Write(t);
        return error is null ? 0 : 1;
    }

    private static int Info()
    {
        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("[bold]Chrono.Net[/]", ChronoEngine.Version);
        grid.AddRow("[bold]Native engine[/]", ChronoEngine.IsAvailable ? "[green]available[/]" : "[red]not found[/]");
        if (ChronoEngine.IsAvailable)
        {
            grid.AddRow("[bold]Project Chrono[/]", ChronoEngine.ChronoVersion);
            grid.AddRow("[bold]Chrono::Vehicle[/]", ChronoEngine.VehicleSupported ? "[green]yes[/]" : "no");
        }
        grid.AddRow("[bold]Runtime[/]", $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} ({System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier})");
        grid.AddRow("[bold]Samples[/]", SampleCatalog.All.Count.ToString());
        AnsiConsole.Write(new Panel(grid).Header("chrono info").Border(BoxBorder.Rounded));
        AnsiConsole.MarkupLine("[grey]" + ChronoEngine.Credits + " · " + ChronoEngine.CreditsId + "[/]");
        return ChronoEngine.IsAvailable ? 0 : 2;
    }

    private static int List()
    {
        var t = new Table().Border(TableBorder.Rounded).AddColumn("Id").AddColumn("Title").AddColumn("Category").AddColumn("Tags");
        foreach (var s in SampleCatalog.All)
            t.AddRow($"[orange1]{s.Id}[/]", Markup.Escape(s.Title), s.Category, Markup.Escape(string.Join(", ", s.Tags)));
        AnsiConsole.Write(t);
        return 0;
    }

    private int RunScene()
    {
        using var run = Load(Arg(1), out var dt, out var duration);
        string? csv = Opt("csv");
        var probes = run.Probes;
        var bodies = run.System.Bodies;
        StringBuilder? sb = null;
        if (csv is not null)
        {
            sb = new StringBuilder("time");
            for (int i = 0; i < bodies.Count; i++)
            {
                string n = string.IsNullOrEmpty(bodies[i].Name) ? $"body{i}" : bodies[i].Name;
                sb.Append($",{n}.x,{n}.y,{n}.z");
            }
            foreach (var p in probes) sb.Append(',').Append(p.Name);
            sb.AppendLine();
        }

        int steps = (int)Math.Round(duration / dt);
        int sampleEvery = Math.Max(1, (int)Math.Round(0.01 / dt));
        var sw = Stopwatch.StartNew();
        AnsiConsole.Progress().Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
            .Start(ctx =>
            {
                var task = ctx.AddTask($"Simulating {duration:0.##} s", maxValue: steps);
                for (int i = 0; i < steps; i++)
                {
                    run.Step(dt);
                    if (sb is not null && i % sampleEvery == 0)
                    {
                        sb.Append(run.System.Time.ToString("0.#####"));
                        var states = run.System.GetBodyStates();
                        foreach (ref readonly var s in states) sb.Append($",{s.Position.X:0.######},{s.Position.Y:0.######},{s.Position.Z:0.######}");
                        foreach (var p in probes) sb.Append(',').Append(p.Read().ToString("0.######"));
                        sb.AppendLine();
                    }
                    if (i % 50 == 0) task.Value = i;
                }
                task.Value = steps;
            });
        sw.Stop();
        if (csv is not null) File.WriteAllText(csv, sb!.ToString());

        var t = new Table().Border(TableBorder.Rounded).AddColumn("Metric").AddColumn("Value");
        t.AddRow("Simulated time", $"{run.System.Time:0.###} s");
        t.AddRow("Steps", steps.ToString("N0"));
        t.AddRow("Wall time", $"{sw.Elapsed.TotalSeconds:0.##} s");
        t.AddRow("Real-time factor", $"{run.System.Time / sw.Elapsed.TotalSeconds:0.##}×");
        t.AddRow("Bodies / links", $"{run.System.Bodies.Count} / {run.System.Links.Count}");
        t.AddRow("Contacts (final)", run.System.ContactCount.ToString());
        foreach (var p in probes) t.AddRow(Markup.Escape(p.Name), $"{p.Read():0.####} {Markup.Escape(p.Unit)}");
        if (csv is not null) t.AddRow("CSV", Markup.Escape(Path.GetFullPath(csv)));
        AnsiConsole.Write(t);
        return 0;
    }

    private int Render()
    {
        string output = Opt("out") ?? throw new InvalidDataException("--out is required (.png or .gif).");
        int width = OptInt("width", 960), height = OptInt("height", 540);
        using var run = Load(Arg(1), out var dt, out var duration, defaultDuration: output.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? 6 : 1.5);
        var world = new RenderWorld(run.System, run.System.Gravity.Z < 0 && Math.Abs(run.System.Gravity.Y) < 1e-9) { ShowContacts = run.ShowContacts };
        run.DecorateWorld?.Invoke(world);
        var cam = new OrbitCamera { UpIsZ = world.UpIsZ };
        run.Camera.Apply(cam);
        var renderer = new SoftwareRenderer();

        void Follow()
        {
            if (run.Follow is { } f) cam.Target = f.Position.ToVector3();
            run.UpdateWorld?.Invoke(world);
            world.Update();
        }

        if (output.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            int fps = OptInt("fps", 25);
            int frames = (int)(duration * fps);
            int stepsPerFrame = Math.Max(1, (int)Math.Round(1.0 / fps / dt));
            using var gif = GifEncoder.Create(output, width, height);
            var fb = new FrameBuffer(width, height);
            AnsiConsole.Progress().Start(ctx =>
            {
                var task = ctx.AddTask("Rendering GIF", maxValue: frames);
                for (int f = 0; f < frames; f++)
                {
                    for (int s = 0; s < stepsPerFrame; s++) run.Step(dt);
                    Follow();
                    renderer.Render(world, cam, fb);
                    gif.AddFrame(fb, 100 / fps);
                    task.Increment(1);
                }
            });
        }
        else
        {
            int steps = (int)Math.Round(duration / dt);
            for (int i = 0; i < steps; i++) run.Step(dt);
            Follow();
            var fb = Snapshot.RenderFrame(world, cam, width, height, supersample: 2);
            PngEncoder.Save(fb, output);
        }
        AnsiConsole.MarkupLine($"[green]✓[/] wrote {Markup.Escape(Path.GetFullPath(output))}");
        return 0;
    }

    private int Bench()
    {
        int n = OptInt("bodies", 500), steps = OptInt("steps", 1000);
        using var sys = new ChSystem();
        using var mat = new ContactMaterial();
        sys.Add(ChBody.CreateBox(new(40, 1, 40), 1000, mat).At(0, -0.5, 0).Fixed());
        int side = (int)Math.Ceiling(Math.Sqrt(n / 5.0));
        for (int i = 0; i < n; i++)
            sys.Add(ChBody.CreateBox(new(0.5, 0.5, 0.5), 500, mat).At((i % side - side / 2) * 0.6, 0.3 + i / (side * side) * 0.6, (i / side % side - side / 2) * 0.6));

        var sw = Stopwatch.StartNew();
        sys.Step(0.005, steps);
        sw.Stop();
        double sps = steps / sw.Elapsed.TotalSeconds;
        var t = new Table().Border(TableBorder.Rounded).AddColumn("Metric").AddColumn("Value");
        t.AddRow("Bodies", n.ToString());
        t.AddRow("Steps (dt = 5 ms)", steps.ToString());
        t.AddRow("Steps / s", sps.ToString("N0"));
        t.AddRow("Real-time factor", $"{sps * 0.005:0.##}×");
        t.AddRow("Contacts (final)", sys.ContactCount.ToString());

        // Overhead of the managed bulk state read.
        var buf = new BodyState[n + 1];
        sw.Restart();
        for (int i = 0; i < 1000; i++) sys.ReadBodyStates(buf);
        t.AddRow("Bulk state read", $"{sw.Elapsed.TotalMilliseconds * 1000 / 1000:0.#} µs / call ({n + 1} bodies)");
        AnsiConsole.Write(t);
        return 0;
    }

    private int NewScene()
    {
        string path = Arg(1);
        var def = (Opt("template") ?? "pendulum") switch
        {
            "empty" => new SceneDefinition { Name = "Empty" },
            "stack" => new SceneDefinition
            {
                Name = "Box stack",
                Materials = { ["default"] = new MaterialDefinition { Friction = 0.6f } },
                Bodies =
                {
                    new BodyDefinition { Name = "ground", Size = [10, 1, 10], Position = [0, -0.5, 0], Fixed = true, Color = "#2A3342", Material = "default" },
                    new BodyDefinition { Name = "box", Size = [0.5, 0.5, 0.5], Position = [0, 0.26, 0], Density = 500, Color = "#F28C28",
                        Material = "default", Array = [3, 6, 3], ArraySpacing = [0.55, 0.51, 0.55] },
                },
            },
            _ => new SceneDefinition
            {
                Name = "Pendulum",
                Bodies =
                {
                    new BodyDefinition { Name = "arm", Size = [1, 0.06, 0.06], Position = [0.5, 2, 0], Collide = false, Color = "#18B6A3" },
                },
                Joints = { new JointDefinition { Name = "hinge", Type = JointKind.Revolute, Body1 = "arm", Body2 = "ground", Position = [0, 2, 0], Axis = [0, 0, 1] } },
            },
        };
        def.Save(path);
        AnsiConsole.MarkupLine($"[green]✓[/] created {Markup.Escape(Path.GetFullPath(path))}");
        return 0;
    }

    private int Validate()
    {
        var def = SceneDefinition.FromFile(Arg(1));
        using var scene = SceneLoader.Load(def);
        AnsiConsole.MarkupLine($"[green]✓[/] [bold]{Markup.Escape(def.Name)}[/]: {scene.System.Bodies.Count} bodies, {scene.System.Links.Count} links, dt = {def.Settings.TimeStep} s");
        return 0;
    }

    private int Source()
    {
        var sample = SampleCatalog.Find(Arg(1)) ?? throw new InvalidDataException($"Unknown sample '{Arg(1)}'.");
        Console.WriteLine(sample.SourceCode);
        return 0;
    }

    // ------------------------------------------------------------------ helpers

    private SampleRun Load(string target, out double dt, out double duration, double? defaultDuration = null)
    {
        ChronoEngine.EnsureAvailable();
        if (File.Exists(target))
        {
            var scene = SceneLoader.LoadFile(target);
            dt = OptDouble("dt", scene.TimeStep);
            duration = OptDouble("duration", defaultDuration ?? scene.Definition.Settings.Duration);
            var run = new SampleRun(scene.System);
            run.Camera = new CameraHint(Vec3.Zero, 8);
            return run;
        }
        var sample = SampleCatalog.Find(target) ?? throw new InvalidDataException($"'{target}' is neither a scene file nor a sample id (see `chrono list`).");
        dt = OptDouble("dt", sample.TimeStep);
        duration = OptDouble("duration", defaultDuration ?? sample.Duration);
        return sample.Create();
    }

    private string Arg(int i) => _positional.Count > i ? _positional[i] : throw new InvalidDataException("Missing argument.");
    private string? Opt(string name) => _opts.TryGetValue(name, out var v) ? v : null;
    private int OptInt(string name, int fallback) => Opt(name) is { } v ? int.Parse(v) : fallback;
    private double OptDouble(string name, double fallback) => Opt(name) is { } v ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;

    private static Dictionary<string, string> ParseOptions(string[] args, out List<string> positional)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        positional = [];
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) { positional.Add(args[i]); continue; }
            string key = args[i][2..];
            bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
            d[key] = hasValue ? args[++i] : "true";
        }
        return d;
    }
}
