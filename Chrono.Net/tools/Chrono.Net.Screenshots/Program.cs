// Usage: dotnet run --project tools/Chrono.Net.Screenshots -- [outputDir] [sampleId[@seconds] | appgen=<projectDir> ...]
// Renders the Avalonia Gallery window headlessly (Skia) and saves PNGs.
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Chrono.Net.Gallery;
using Chrono.Net.Rendering;
using Chrono.Net.Samples;

string outDir = args.Length > 0 ? args[0] : Path.Combine("docs", "images");
Directory.CreateDirectory(outDir);
var targets = args.Skip(1).ToList();
if (targets.Count == 0) targets = ["mixer@3", "four-bar@1.2", "robot-arm@4", "fea-cable@1.2", "dominoes@2.5", "vehicle-hmmwv@4"];

var session = HeadlessUnitTestSession.StartNew(typeof(ScreenshotApp));
await session.Dispatch(() =>
{
    // A running DispatcherTimer would keep the headless job queue busy forever.
    MainWindow.AutoRunOnStartup = false;
    // ChronoAppGen IDE: appgen=<projectDir>
    foreach (var target in targets.Where(t => t.StartsWith("appgen=", StringComparison.Ordinal)))
        AppGenShot.Capture(target["appgen=".Length..], Path.Combine(outDir, "appgen.png"));
    targets.RemoveAll(t => t.StartsWith("appgen=", StringComparison.Ordinal));
    // Real Jack turn in the IDE: appgen-live=<projectDir>|<configWithKeys>|<prompt>
    foreach (var target in targets.Where(t => t.StartsWith("appgen-live=", StringComparison.Ordinal)))
    {
        var parts = target["appgen-live=".Length..].Split('|', 3);
        AppGenShot.CaptureLive(parts[0], parts[2], parts[1], Path.Combine(outDir, "appgen-live.png"), TimeSpan.FromMinutes(6));
    }
    targets.RemoveAll(t => t.StartsWith("appgen-live=", StringComparison.Ordinal));
    foreach (var kind in new[] { "new", "settings" })
        if (targets.Remove($"appgen-{kind}")) AppGenShot.CaptureDialog(kind, Path.Combine(outDir, $"appgen-{kind}.png"));

    foreach (var target in targets)
    {
        var parts = target.Split('@');
        string id = parts[0];
        double seconds = parts.Length > 1 ? double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 2;
        bool indonesian = id.EndsWith(":id");
        if (indonesian) id = id[..^3];

        var window = new MainWindow { Width = 1480, Height = 900 };
        window.Show();
        try
        {
            window.ShowSample(id, seconds, indonesian);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{id}: {ex.Message}");
        }
        // CaptureRenderedFrame forces layout + render itself; pumping the dispatcher here never drains.
        var frame = window.CaptureRenderedFrame();
        string file = Path.Combine(outDir, $"gallery-{id}{(indonesian ? "-id" : "")}.png");
        frame?.Save(file);
        Console.WriteLine($"saved {file}");
        window.Close();
    }

    // Plain viewport renders (software renderer) for the docs.
    foreach (var target in targets.Take(6))
    {
        string id = target.Split('@')[0].Replace(":id", "");
        var sample = SampleCatalog.Find(id);
        if (sample is null || !Chrono.Net.ChronoEngine.IsAvailable) continue;
        if (sample.RequiresVehicle && !Chrono.Net.ChronoEngine.VehicleSupported) continue;
        using var run = sample.Create();
        double seconds = target.Contains('@') ? double.Parse(target.Split('@')[1], System.Globalization.CultureInfo.InvariantCulture) : 2;
        for (double t = 0; t < seconds; t += sample.TimeStep) run.Step(sample.TimeStep);
        var world = new RenderWorld(run.System, sample.UpIsZ);
        run.DecorateWorld?.Invoke(world);
        run.UpdateWorld?.Invoke(world);
        var cam = new OrbitCamera { UpIsZ = sample.UpIsZ };
        run.Camera.Apply(cam);
        if (run.Follow is { } f) cam.Target = f.Position.ToVector3();
        string file = Path.Combine(outDir, $"render-{id}.png");
        Snapshot.SavePng(world, cam, file, 1280, 720);
        Console.WriteLine($"saved {file}");
    }
}, CancellationToken.None);

// Disposing the headless session can block on its dispatcher thread; all files are written, so exit directly.
Console.Out.Flush();
Environment.Exit(0);

public sealed class ScreenshotApp : App
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ScreenshotApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void OnFrameworkInitializationCompleted() { }
}
