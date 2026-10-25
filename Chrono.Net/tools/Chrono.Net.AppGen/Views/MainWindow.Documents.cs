using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Chrono.Net.AppGen.Ai;
using Chrono.Net.AppGen.Editor;
using Chrono.Net.AppGen.Projects;

namespace Chrono.Net.AppGen.Views;

using Color = global::Avalonia.Media.Color;

// Projects, documents, tabs and the welcome screen.
public sealed partial class MainWindow
{
    private EditorStyle Style()
    {
        var family = _s.EditorFontFamily.Equals("JetBrains Mono", StringComparison.OrdinalIgnoreCase)
            ? (FontFamily)this.FindResource("MonoFont")!
            : new FontFamily(_s.EditorFontFamily);
        return new EditorStyle(_s.ShowLineNumbers, _s.WordWrap, _s.EditorFontSize, family, _s.TabSize);
    }

    // ================================================================== projects

    private async Task NewProjectAsync(bool templates)
    {
        var dlg = new NewProjectDialog(_s.ProjectsRoot, templates);
        await dlg.ShowDialog(this);
        if (dlg.Result is not { } r) return;
        try
        {
            Directory.CreateDirectory(r.Location);
            string csproj = ProjectGenerator.Create(r.Template, r.Location, r.Name,
                new GeneratorOptions(_s.ChronoNetVersion, _s.AvaloniaVersion, _s.LocalPackageFeed));
            AppendLog(LogChannel.Output, $"Created {csproj} from '{r.Template.Name}'.");
            await OpenProjectAsync(Path.GetDirectoryName(csproj)!);
        }
        catch (IOException ex)
        {
            await Dialogs.ConfirmAsync(this, "New project", ex.Message, "OK");
        }
    }

    private async Task PickProjectAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open a project folder", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) await OpenProjectAsync(path);
    }

    private async Task PickFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a file or project",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Code and projects") { Patterns = ["*.cs", "*.csproj", "*.slnx", "*.sln", "*.axaml", "*.json", "*.md", "*.config", "*.xml", "*.razor"] },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });
        foreach (var f in files)
            if (f.TryGetLocalPath() is { } p)
            {
                if (p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                    await OpenProjectAsync(p);
                else await OpenFileAsync(p);
            }
    }

    /// <summary>Opens a folder, .csproj or solution as the current project.</summary>
    public async Task OpenProjectAsync(string path)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => OpenProjectAsync(path));
            return;
        }
        path = Path.GetFullPath(path);
        string root = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
        if (!Directory.Exists(root))
        {
            AppendLog(LogChannel.Output, $"Not found: {path}");
            return;
        }
        if (_projectRoot is not null && !_projectRoot.Equals(root, StringComparison.OrdinalIgnoreCase) && !await CloseProjectAsync()) return;

        string? csproj = path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? path
            : Directory.EnumerateFiles(root, "*.csproj").FirstOrDefault()
              ?? Directory.EnumerateDirectories(root).Where(d => !Ai.Plugins.WorkspacePlugin.IgnoredFolders.Contains(Path.GetFileName(d)))
                  .SelectMany(d => Directory.EnumerateFiles(d, "*.csproj")).FirstOrDefault();
        _projectRoot = root;
        _projectFile = csproj;
        _explorer.SetRoot(root);
        StartWatcher(root);
        var recent = _s.RecentProjects;
        recent.RemoveAll(r => r.Equals(root, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, root);
        _s.RecentProjects = recent;
        TrySaveSettings();

        AppendLog(LogChannel.Output, $"Opened {root}{(csproj is null ? " (no .csproj found)" : "")}");
        // Open the file people usually start with.
        foreach (var name in new[] { "Simulation.cs", "Program.cs", "README.md" })
        {
            string f = Path.Combine(Path.GetDirectoryName(csproj ?? Path.Combine(root, "x"))!, name);
            if (File.Exists(f)) { await OpenFileAsync(f); break; }
        }
        UpdateCommandStates();
    }

    private async Task<bool> CloseProjectAsync()
    {
        if (!await ConfirmDirtyAsync(_docs.Where(d => d.IsDirty).ToList())) return false;
        foreach (var d in _docs.ToList()) RemoveDoc(d);
        _watcher?.Dispose();
        _watcher = null;
        _projectRoot = _projectFile = null;
        _explorer.SetRoot(null);
        ShowEditorOrWelcome();
        UpdateCommandStates();
        return true;
    }

    // ================================================================== documents

    public async Task OpenFileAsync(string path, int line = 0)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => OpenFileAsync(path, line));
            return;
        }
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) return;
        var doc = _docs.FirstOrDefault(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (doc is null)
        {
            if (new FileInfo(path).Length > 8_000_000)
            {
                AppendLog(LogChannel.Output, $"{Path.GetFileName(path)} is too large to open in the editor.");
                return;
            }
            if (IsBinary(path))
            {
                AppendLog(LogChannel.Output, $"{Path.GetFileName(path)} is a binary file.");
                return;
            }
            doc = new EditorDocument(path, Style());
            doc.DirtyChanged += _ => RenderTabs();
            doc.Editor.TextArea.Caret.PositionChanged += (_, _) => { if (ReferenceEquals(doc, _active)) UpdateCaret(); };
            _docs.Add(doc);
        }
        Activate(doc);
        if (line > 0) doc.GoTo(line, focus: false);
        await Task.CompletedTask;
    }

    private static bool IsBinary(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".ico" or ".dll" or ".exe" or ".pdb" or ".nupkg" or ".zip" or ".webp") return true;
        using var fs = File.OpenRead(path);
        var buf = new byte[Math.Min(4096, fs.Length)];
        int n = fs.Read(buf, 0, buf.Length);
        return buf.AsSpan(0, n).Contains((byte)0);
    }

    private void Activate(EditorDocument doc)
    {
        _active = doc;
        _editorHost.Content = doc.Editor;
        RenderTabs();
        _explorer.Reveal(doc.Path);
        UpdateCommandStates();
        Dispatcher.UIThread.Post(() => doc.Editor.TextArea.Focus(), DispatcherPriority.Background);
    }

    private void RemoveDoc(EditorDocument doc)
    {
        int i = _docs.IndexOf(doc);
        _docs.Remove(doc);
        doc.Dispose();
        if (ReferenceEquals(doc, _active))
        {
            _active = _docs.Count == 0 ? null : _docs[Math.Clamp(i - 1, 0, _docs.Count - 1)];
            if (_active is not null) Activate(_active);
        }
        ShowEditorOrWelcome();
        RenderTabs();
        UpdateCommandStates();
    }

    private async Task CloseDocAsync(EditorDocument doc)
    {
        if (doc.IsDirty && !await ConfirmDirtyAsync([doc])) return;
        RemoveDoc(doc);
    }

    /// <summary>Asks to save unsaved files. Returns false when the user cancels.</summary>
    private async Task<bool> ConfirmDirtyAsync(List<EditorDocument> dirty)
    {
        if (dirty.Count == 0) return true;
        string names = string.Join(", ", dirty.Select(d => d.FileName));
        var choice = await Dialogs.ConfirmAsync(this, "Unsaved changes", $"Save changes to {names}?", "Save", "Don't save", "Cancel");
        if (choice == "Save") { foreach (var d in dirty) d.Save(); return true; }
        return choice == "Don't save";
    }

    private bool SaveActive()
    {
        if (_active is null) return false;
        try
        {
            _active.Save();
            AppendLog(LogChannel.Output, $"Saved {RelativeToProject(_active.Path)}");
            return true;
        }
        catch (IOException ex)
        {
            AppendLog(LogChannel.Output, $"Could not save: {ex.Message}");
            return false;
        }
    }

    private void SaveAll()
    {
        foreach (var d in _docs.Where(d => d.IsDirty))
        {
            try { d.Save(); }
            catch (IOException ex) { AppendLog(LogChannel.Output, $"Could not save {d.FileName}: {ex.Message}"); }
        }
    }

    private void ShowEditorOrWelcome()
    {
        if (_active is null) _editorHost.Content = Welcome();
    }

    private void RenderTabs()
    {
        _tabStrip.Children.Clear();
        foreach (var d in _docs)
        {
            var doc = d;
            var name = new TextBlock { Text = doc.FileName, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Foreground = ReferenceEquals(doc, _active) ? Icons.Ink : Icons.Muted };
            var close = new Button { Classes = { "mini" }, Padding = new Thickness(4), Margin = new Thickness(6, 0, 0, 0),
                Content = doc.IsDirty ? new TextBlock { Text = "●", FontSize = 10, Foreground = Icons.Signal } : Icons.Make(Icons.Close, 9, Icons.Muted) };
            ToolTip.SetTip(close, doc.IsDirty ? "Unsaved — click to close" : "Close (Ctrl+W)");
            close.Click += async (_, _) => await CloseDocAsync(doc);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(name);
            sp.Children.Add(close);
            var tab = new Border { Classes = { "tab" }, Child = sp };
            if (ReferenceEquals(doc, _active)) tab.Classes.Add("active");
            ToolTip.SetTip(tab, doc.Path);
            tab.PointerPressed += async (_, e) =>
            {
                if (e.GetCurrentPoint(tab).Properties.IsMiddleButtonPressed) await CloseDocAsync(doc);
                else Activate(doc);
            };
            _tabStrip.Children.Add(tab);
        }
    }

    private Control Welcome()
    {
        var stack = new StackPanel { Spacing = 14, MaxWidth = 760, Margin = new Thickness(48, 56, 48, 24), HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(new TextBlock { Text = "CHRONO.NET · APP GENERATOR", Classes = { "eyebrow" }, Foreground = Icons.Signal });
        stack.Children.Add(new TextBlock { Text = "Build physics apps by describing them.", Classes = { "display" }, FontSize = 40, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock
        {
            Text = "Start from a template or a blank app, then ask Jack for what you need. He writes the Chrono.Net code, builds it, runs the physics headless and fixes what breaks.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
            FontSize = 14.5,
            LineHeight = 22,
        });

        Border Action(string title, string text, string gesture, Func<Task> go)
        {
            var sp = new StackPanel { Spacing = 4 };
            var head = new DockPanel();
            var g = new TextBlock { Text = gesture, Classes = { "stat" }, Margin = new Thickness(0) };
            DockPanel.SetDock(g, Dock.Right);
            head.Children.Add(g);
            head.Children.Add(new TextBlock { Text = title, Classes = { "display" }, FontSize = 19 });
            sp.Children.Add(head);
            sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, FontSize = 12.5 });
            var card = new Border { Classes = { "card" }, Width = 236, Margin = new Thickness(0, 0, 12, 0), Child = sp };
            card.PointerPressed += async (_, _) => await go();
            return card;
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(Action("From a template", $"{TemplateCatalog.Gallery.Count()} ready apps: simulators, science, industry, safety, games…", "Ctrl+Shift+N", () => NewProjectAsync(true)));
        actions.Children.Add(Action("Blank app", "An empty desktop or console app wired to Chrono.Net.", "Ctrl+N", () => NewProjectAsync(false)));
        actions.Children.Add(Action("Open project", "A folder with a .csproj, or any code file.", "Ctrl+Shift+O", PickProjectAsync));
        stack.Children.Add(actions);

        var recent = _s.RecentProjects.Where(Directory.Exists).Take(6).ToList();
        if (recent.Count > 0)
        {
            stack.Children.Add(new TextBlock { Text = "RECENT", Classes = { "panelTitle" }, Margin = new Thickness(0, 18, 0, 0) });
            foreach (var r in recent)
            {
                var link = new Button { Classes = { "mini" }, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 3) };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                row.Children.Add(new TextBlock { Text = Path.GetFileName(r), Foreground = Icons.Ink, FontSize = 13 });
                row.Children.Add(new TextBlock { Text = r, Classes = { "stat" }, Margin = new Thickness(0) });
                link.Content = row;
                string path = r;
                link.Click += async (_, _) => await OpenProjectAsync(path);
                stack.Children.Add(link);
            }
        }
        stack.Children.Add(new TextBlock { Text = ChronoEngine.Credits, Classes = { "stat" }, Margin = new Thickness(0, 26, 0, 0) });
        return new ScrollViewer { Content = stack, Background = (IBrush)this.FindResource("BenchBrush")! };
    }

    // ================================================================== editor commands

    private async Task GoToLineAsync()
    {
        if (_active is null) return;
        if (await Dialogs.GoToLineAsync(this, _active.CaretLine, _active.Editor.Document.LineCount) is { } line) _active.GoTo(line);
    }

    private void FormatActive()
    {
        if (_active is null) return;
        var (text, error) = CodeFormatter.Format(_active.Path, _active.Text);
        if (text is null)
        {
            AppendLog(LogChannel.Output, error ?? "Could not format.");
            SelectLog(LogChannel.Output);
            return;
        }
        if (text != _active.Text) _active.ReplaceAll(text);
        AppendLog(LogChannel.Output, $"Formatted {_active.FileName}");
    }

    private void ToggleLineNumbers()
    {
        _s.ShowLineNumbers = !_s.ShowLineNumbers;
        ApplyEditorStyle();
    }

    private void ToggleWordWrap()
    {
        _s.WordWrap = !_s.WordWrap;
        ApplyEditorStyle();
    }

    private void ApplyEditorStyle()
    {
        var style = Style();
        foreach (var d in _docs) d.Apply(style);
        TrySaveSettings();
    }

    // ================================================================== explorer + file system

    private void WireExplorer()
    {
        _explorer.OpenRequested += async p => await OpenFileAsync(p);
        _explorer.NewFileRequested += async folder =>
        {
            if (folder.Length == 0) return;
            if (await Dialogs.PromptAsync(this, "New file", $"File name in {RelativeToProject(folder)}", "NewFile.cs", "Create") is not { } name) return;
            string path = Path.Combine(folder, name);
            if (File.Exists(path)) { await OpenFileAsync(path); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, Path.GetExtension(name) == ".cs" ? CSharpStub(name) : "");
            _explorer.Refresh();
            await OpenFileAsync(path);
        };
        _explorer.NewFolderRequested += async folder =>
        {
            if (folder.Length == 0) return;
            if (await Dialogs.PromptAsync(this, "New folder", $"Folder name in {RelativeToProject(folder)}", "Folder", "Create") is not { } name) return;
            Directory.CreateDirectory(Path.Combine(folder, name));
            _explorer.Refresh();
        };
        _explorer.RenameRequested += async path =>
        {
            string old = Path.GetFileName(path);
            if (await Dialogs.PromptAsync(this, "Rename", "New name", old, "Rename") is not { } name || name == old) return;
            string target = Path.Combine(Path.GetDirectoryName(path)!, name);
            try
            {
                if (Directory.Exists(path)) Directory.Move(path, target); else File.Move(path, target);
                foreach (var d in _docs.Where(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) d.Rename(target);
                RenderTabs();
                _explorer.Refresh();
            }
            catch (IOException ex) { await Dialogs.ConfirmAsync(this, "Rename", ex.Message, "OK"); }
        };
        _explorer.DeleteRequested += async path =>
        {
            if (await Dialogs.ConfirmAsync(this, "Delete", $"Delete {Path.GetFileName(path)}? This cannot be undone.", "Delete", "Cancel") != "Delete") return;
            try
            {
                foreach (var d in _docs.Where(d => d.Path.StartsWith(path, StringComparison.OrdinalIgnoreCase)).ToList()) RemoveDoc(d);
                if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path);
                _explorer.Refresh();
            }
            catch (IOException ex) { await Dialogs.ConfirmAsync(this, "Delete", ex.Message, "OK"); }
        };
    }

    private string CSharpStub(string fileName)
    {
        string ns = _projectFile is null ? "App" : ProjectGenerator.ToIdentifier(Path.GetFileNameWithoutExtension(_projectFile));
        return $"namespace {ns};\r\n\r\npublic class {Path.GetFileNameWithoutExtension(fileName)}\r\n{{\r\n}}\r\n";
    }

    private void StartWatcher(string root)
    {
        _watcher?.Dispose();
        try
        {
            _watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
            FileSystemEventHandler h = (_, e) => QueueFileChange(e.FullPath);
            _watcher.Changed += h;
            _watcher.Created += h;
            _watcher.Deleted += h;
            _watcher.Renamed += (_, e) => { QueueFileChange(e.OldFullPath); QueueFileChange(e.FullPath); };
            _watcher.EnableRaisingEvents = true;
        }
        catch (ArgumentException) { _watcher = null; }
    }

    private void QueueFileChange(string path)
    {
        string sep = Path.DirectorySeparatorChar.ToString();
        if (path.Contains(sep + "bin" + sep, StringComparison.OrdinalIgnoreCase) || path.Contains(sep + "obj" + sep, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(sep + "bin", StringComparison.OrdinalIgnoreCase) || path.EndsWith(sep + "obj", StringComparison.OrdinalIgnoreCase)) return;
        lock (_fsChanged) _fsChanged.Add(path);
        Dispatcher.UIThread.Post(() => { _fsDebounce.Stop(); _fsDebounce.Start(); });
    }

    private void FlushFileChanges()
    {
        _fsDebounce.Stop();
        lock (_fsChanged) _fsChanged.Clear();
        _explorer.Refresh();
        foreach (var d in _docs.ToList())
        {
            if (!File.Exists(d.Path)) continue;
            if (d.ReloadIfChangedOnDisk()) AppendLog(LogChannel.Output, $"Reloaded {d.FileName} (changed on disk)");
        }
        if (_active is not null) _explorer.Reveal(_active.Path);
    }
}
