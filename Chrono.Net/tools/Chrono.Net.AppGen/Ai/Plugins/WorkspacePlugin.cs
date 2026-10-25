using System.ComponentModel;
using System.Text;
using Microsoft.SemanticKernel;

namespace Chrono.Net.AppGen.Ai.Plugins;

/// <summary>File access for Jack, restricted to the open project's folder.</summary>
public sealed class WorkspacePlugin(IIdeHost host)
{
    private const int MaxReadChars = 60_000;
    internal static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", ".idea", "node_modules", "publish" };

    [KernelFunction, Description("Describes the open project: root folder, project file, file list (without bin/obj) and the file in the active editor tab.")]
    public string GetProjectInfo()
    {
        if (host.ProjectRoot is not { } root) return "No project is open. Create one with CreateProjectFromTemplate, or ask the user to open one.";
        var sb = new StringBuilder();
        sb.AppendLine($"Root: {root}");
        sb.AppendLine($"Project file: {(host.ProjectFile is { } p ? Path.GetRelativePath(root, p) : "(none)")}");
        if (host.ActiveDocument is { } doc) sb.AppendLine($"Active editor: {Path.GetRelativePath(root, doc.Path)} (caret line {doc.Line})");
        sb.AppendLine("Files:");
        foreach (var f in EnumerateFiles(root, ".", true).Take(300)) sb.AppendLine("  " + f);
        return sb.ToString();
    }

    [KernelFunction, Description("Lists files under a folder of the project (relative path, '.' = root).")]
    public string ListFiles([Description("Folder relative to the project root")] string folder = ".", bool recursive = true)
    {
        var root = RequireRoot();
        var files = EnumerateFiles(root, folder, recursive).Take(500).ToList();
        return files.Count == 0 ? "(no files)" : string.Join("\n", files);
    }

    [KernelFunction, Description("Reads a text file of the project. Returns the content with line numbers when withLineNumbers is true.")]
    public async Task<string> ReadFile([Description("Path relative to the project root")] string path, bool withLineNumbers = false)
    {
        string full = Resolve(path);
        if (!File.Exists(full)) return $"File not found: {path}";
        string text = await File.ReadAllTextAsync(full);
        if (withLineNumbers)
            text = string.Join("\n", text.Split('\n').Select((l, i) => $"{i + 1,4}: {l.TrimEnd('\r')}"));
        return text.Length > MaxReadChars ? text[..MaxReadChars] + $"\n… (truncated, {text.Length} chars)" : text;
    }

    [KernelFunction, Description("Creates or overwrites a text file with the complete content. Folders are created as needed. The file opens in the editor.")]
    public async Task<string> WriteFile([Description("Path relative to the project root")] string path, [Description("Complete file content")] string content)
    {
        string full = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        bool existed = File.Exists(full);
        await File.WriteAllTextAsync(full, content.ReplaceLineEndings("\r\n"), new UTF8Encoding(false));
        await host.FileChangedAsync(full);
        await host.OpenFileAsync(full);
        int lines = content.Count(c => c == '\n') + 1;
        host.Log(LogChannel.Jack, $"{(existed ? "Updated" : "Created")} {Rel(full)} ({lines} lines)");
        return $"{(existed ? "Updated" : "Created")} {Rel(full)} ({lines} lines).";
    }

    [KernelFunction, Description("Replaces an exact snippet in a file (must occur exactly once). Use for small edits; use WriteFile for large changes.")]
    public async Task<string> ReplaceInFile(string path, [Description("Exact existing text, including indentation")] string oldText, string newText)
    {
        string full = Resolve(path);
        if (!File.Exists(full)) return $"File not found: {path}";
        string text = await File.ReadAllTextAsync(full);
        string normalized = text.ReplaceLineEndings("\n"), needle = oldText.ReplaceLineEndings("\n");
        int first = normalized.IndexOf(needle, StringComparison.Ordinal);
        if (first < 0) return "oldText was not found. Read the file again and copy the snippet exactly.";
        if (normalized.IndexOf(needle, first + needle.Length, StringComparison.Ordinal) >= 0) return "oldText occurs more than once; include more surrounding lines.";
        string updated = normalized[..first] + newText.ReplaceLineEndings("\n") + normalized[(first + needle.Length)..];
        await File.WriteAllTextAsync(full, updated.ReplaceLineEndings("\r\n"), new UTF8Encoding(false));
        await host.FileChangedAsync(full);
        int line = normalized[..first].Count(c => c == '\n') + 1;
        await host.OpenFileAsync(full, line);
        host.Log(LogChannel.Jack, $"Edited {Rel(full)} at line {line}");
        return $"Replaced at line {line} of {Rel(full)}.";
    }

    [KernelFunction, Description("Searches all project text files for a string (case-insensitive). Returns file:line: text.")]
    public string SearchFiles(string text, int maxResults = 60)
    {
        var root = RequireRoot();
        var hits = new List<string>();
        foreach (var rel in EnumerateFiles(root, ".", true))
        {
            string full = Path.Combine(root, rel);
            if (!IsText(full)) continue;
            int n = 0;
            foreach (var line in File.ReadLines(full))
            {
                n++;
                if (line.Contains(text, StringComparison.OrdinalIgnoreCase)) hits.Add($"{rel}:{n}: {line.Trim()}");
                if (hits.Count >= maxResults) return string.Join("\n", hits);
            }
        }
        return hits.Count == 0 ? "No matches." : string.Join("\n", hits);
    }

    [KernelFunction, Description("Creates a folder in the project.")]
    public string CreateFolder(string path)
    {
        string full = Resolve(path);
        Directory.CreateDirectory(full);
        _ = host.FileChangedAsync(full);
        return $"Created {Rel(full)}.";
    }

    [KernelFunction, Description("Deletes a file of the project.")]
    public async Task<string> DeleteFile(string path)
    {
        string full = Resolve(path);
        if (!File.Exists(full)) return $"File not found: {path}";
        File.Delete(full);
        await host.FileChangedAsync(full);
        host.Log(LogChannel.Jack, $"Deleted {Rel(full)}");
        return $"Deleted {Rel(full)}.";
    }

    [KernelFunction, Description("Renames or moves a file inside the project.")]
    public async Task<string> RenameFile(string path, string newPath)
    {
        string from = Resolve(path), to = Resolve(newPath);
        if (!File.Exists(from)) return $"File not found: {path}";
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to, overwrite: false);
        await host.FileChangedAsync(from);
        await host.FileChangedAsync(to);
        return $"Moved {Rel(from)} → {Rel(to)}.";
    }

    [KernelFunction, Description("Returns the file open in the active editor tab, with line numbers and the caret line.")]
    public string GetActiveEditor()
    {
        if (host.ActiveDocument is not { } doc) return "No file is open in the editor.";
        var numbered = string.Join("\n", doc.Text.Split('\n').Select((l, i) => $"{i + 1,4}: {l.TrimEnd('\r')}"));
        if (numbered.Length > MaxReadChars) numbered = numbered[..MaxReadChars] + "\n… (truncated)";
        return $"File: {(host.ProjectRoot is { } r ? Path.GetRelativePath(r, doc.Path) : doc.Path)}\nCaret line: {doc.Line}\n{numbered}";
    }

    [KernelFunction, Description("Opens a project file in the editor and moves the caret to a line (1-based, 0 = keep).")]
    public async Task<string> OpenInEditor(string path, int line = 0)
    {
        string full = Resolve(path);
        if (!File.Exists(full)) return $"File not found: {path}";
        await host.OpenFileAsync(full, line);
        return $"Opened {Rel(full)}{(line > 0 ? $" at line {line}" : "")}.";
    }

    // ------------------------------------------------------------------ helpers

    private string RequireRoot() =>
        host.ProjectRoot ?? throw new InvalidOperationException("No project is open. Create one with CreateProjectFromTemplate first.");

    private string Rel(string full) => Path.GetRelativePath(RequireRoot(), full).Replace('\\', '/');

    /// <summary>Resolves a project-relative path and rejects anything outside the project folder.</summary>
    private string Resolve(string path)
    {
        string root = Path.GetFullPath(RequireRoot());
        string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !full.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"'{path}' is outside the project folder. Only files inside {root} can be changed.");
        return full;
    }

    private static IEnumerable<string> EnumerateFiles(string root, string folder, bool recursive)
    {
        string start = Path.GetFullPath(Path.Combine(root, folder));
        if (!Directory.Exists(start)) yield break;
        var stack = new Stack<string>([start]);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            foreach (var f in Directory.EnumerateFiles(dir).Order(StringComparer.OrdinalIgnoreCase))
                yield return Path.GetRelativePath(root, f).Replace('\\', '/');
            if (!recursive) continue;
            foreach (var d in Directory.EnumerateDirectories(dir).OrderDescending(StringComparer.OrdinalIgnoreCase))
                if (!IgnoredFolders.Contains(Path.GetFileName(d))) stack.Push(d);
        }
    }

    internal static bool IsText(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".cs" or ".axaml" or ".xaml" or ".csproj" or ".slnx" or ".sln" or ".json" or ".md" or ".txt" or ".config" or ".xml"
        or ".razor" or ".css" or ".js" or ".html" or ".props" or ".targets" or ".yml" or ".yaml" or ".csv" or ".ps1" or ".sh";
}
