namespace Chrono.Net.AppGen.Ai;

/// <summary>Log channels shown as tabs in the logs panel.</summary>
public enum LogChannel { Output, Build, Jack }

/// <summary>
/// What Jack's kernel functions may do to the IDE. The desktop window implements it on the UI thread; the
/// command-line harness (<c>ChronoAppGen --agent</c>) implements it on the console.
/// </summary>
public interface IIdeHost
{
    /// <summary>Root folder of the open project, or null.</summary>
    string? ProjectRoot { get; }

    /// <summary>The .csproj of the open project, or null.</summary>
    string? ProjectFile { get; }

    /// <summary>Full path, text and caret line of the active editor tab, or null.</summary>
    (string Path, string Text, int Line)? ActiveDocument { get; }

    Task OpenProjectAsync(string path);
    Task OpenFileAsync(string path, int line = 0);

    /// <summary>A file was created, changed or deleted outside the editor: reload tabs and refresh the explorer.</summary>
    Task FileChangedAsync(string path);

    void Log(LogChannel channel, string text);

    /// <summary>Starts the project's app for the user (does not wait for it to exit).</summary>
    Task LaunchProjectAsync();
}
