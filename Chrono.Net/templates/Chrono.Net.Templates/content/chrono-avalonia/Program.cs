using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Chrono.Net;
using Chrono.Net.Avalonia;
using Chrono.Net.Rendering;

namespace ChronoAvaloniaApp;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
}

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://ChronoAvaloniaApp")) { Source = new Uri("avares://Chrono.Net.Avalonia/Themes/ChronoTheme.axaml") });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

public sealed class MainWindow : Window
{
    private readonly ChSystem _sys = new();
    private readonly SimulationHost _host;

    public MainWindow()
    {
        Title = "ChronoAvaloniaApp";
        Width = 1200;
        Height = 760;

        // ---- scene: a rotating mixer with falling spheres
        var mat = _sys.Own(new ContactMaterial { Friction = 0.4f });
        var floor = _sys.Add(ChBody.CreateBox(new(8, 0.4, 8), 1000, mat).At(0, -0.2, 0).Fixed().Colored(Color.Slate));
        var paddle = _sys.Add(ChBody.CreateBox(new(5, 0.5, 0.3), 2000, mat).At(0, 0.3, 0).Colored(Color.Orange));
        _sys.Add(ChMotor.RotationSpeed(paddle, floor, new Vec3(0, 0.3, 0), Vec3.UnitY, 1.2));
        var rng = new Random(1);
        for (int i = 0; i < 80; i++)
            _sys.Add(ChBody.CreateSphere(0.2, 900, mat).At(rng.NextDouble() * 4 - 2, 1 + i * 0.1, rng.NextDouble() * 4 - 2).Colored(Color.Teal));

        // ---- UI: viewport + plot + run button
        var world = new RenderWorld(_sys);
        var viewport = new ChronoViewport { World = world };
        viewport.Camera.Target = new(0, 0.5f, 0);
        viewport.Camera.Distance = 11;
        var plot = new ChronoPlot { Height = 150 };
        var energy = plot.AddSeries("Kinetic energy", "J");
        var run = new Button { Content = "Pause", Classes = { "transport", "primary" }, Margin = new Thickness(0, 10) };

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(plot, Dock.Bottom);
        DockPanel.SetDock(run, Dock.Bottom);
        root.Children.Add(plot);
        root.Children.Add(run);
        root.Children.Add(viewport);
        Content = root;

        _host = new SimulationHost(_sys.Step, 0.003);
        _host.Frame += () =>
        {
            world.Update();
            energy.Add(_sys.Time, _sys.KineticEnergy);
            viewport.Refresh();
            plot.Refresh();
        };
        run.Click += (_, _) =>
        {
            if (_host.IsRunning) _host.Pause(); else _host.Start();
            run.Content = _host.IsRunning ? "Pause" : "Run";
        };
        _host.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _host.Dispose();
        _sys.Dispose();
        base.OnClosed(e);
    }
}
