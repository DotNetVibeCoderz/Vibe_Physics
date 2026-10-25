using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Chrono.Net.Simulator;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()
            .StartWithClassicDesktopLifetime(args);
}

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new SimulatorWindow(desktop.Args is { Length: > 0 } a ? a[0] : null);
        base.OnFrameworkInitializationCompleted();
    }
}
