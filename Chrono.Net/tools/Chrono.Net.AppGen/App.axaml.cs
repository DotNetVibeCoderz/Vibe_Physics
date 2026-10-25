using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Chrono.Net.AppGen.Settings;
using Chrono.Net.AppGen.Views;

namespace Chrono.Net.AppGen;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // ChronoAppGen [--project] <folder|.csproj> opens that project at startup.
            var args = desktop.Args ?? [];
            string? project = args.SkipWhile(a => a == "--project").FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
            desktop.MainWindow = new MainWindow(AppSettings.Load(), project);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
