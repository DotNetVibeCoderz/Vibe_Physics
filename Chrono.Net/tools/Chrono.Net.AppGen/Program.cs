using Avalonia;
using Chrono.Net.AppGen;

if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal) && args[0] != "--project")
    return await CommandLine.RunAsync(args);

AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace().StartWithClassicDesktopLifetime(args);
return 0;
