using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Chrono.Net.AppGen.Ai.Plugins;

namespace Chrono.Net.AppGen.Views;

using Color = global::Avalonia.Media.Color;

/// <summary>A file or folder in the explorer tree.</summary>
public sealed class FileNode(string path, bool isDirectory) : INotifyPropertyChanged
{
    private bool _isExpanded;

    public string FullPath { get; } = path;
    public string Name { get; } = Path.GetFileName(path);
    public bool IsDirectory { get; } = isDirectory;
    public ObservableCollection<FileNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>VS Code–style project explorer: folder tree, file-type glyphs, context actions.</summary>
public sealed class ExplorerPanel : UserControl
{
    private static readonly FontFamily Mono = new("avares://Chrono.Net.Avalonia/Assets/Fonts#JetBrains Mono");
    private readonly TreeView _tree = new() { Classes = { "explorer" }, Margin = new Thickness(4, 0, 4, 6) };
    private readonly TextBlock _projectName = new() { FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 2, 8, 6) };
    private readonly ObservableCollection<FileNode> _roots = [];
    private readonly Control _empty;
    private string? _root;
    private bool _suppressOpen;

    public ExplorerPanel()
    {
        _tree.ItemsSource = _roots;
        _tree.ItemTemplate = new FuncTreeDataTemplate<FileNode>((node, _) => Row(node), node => node.Children);
        _tree.SelectionChanged += (_, _) =>
        {
            if (!_suppressOpen && _tree.SelectedItem is FileNode { IsDirectory: false } f) OpenRequested?.Invoke(f.FullPath);
        };

        var title = new TextBlock { Text = "EXPLORER", Classes = { "panelTitle" } };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        actions.Children.Add(MarkdownView.MiniButton(Icons.NewFile, "New file", () => NewFileRequested?.Invoke(SelectedFolder())));
        actions.Children.Add(MarkdownView.MiniButton(Icons.NewFolder, "New folder", () => NewFolderRequested?.Invoke(SelectedFolder())));
        actions.Children.Add(MarkdownView.MiniButton(Icons.Refresh, "Refresh", Refresh));
        actions.Children.Add(MarkdownView.MiniButton(Icons.Collapse, "Collapse all", CollapseAll));
        var header = new DockPanel { Margin = new Thickness(12, 8, 6, 2) };
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(title);

        _empty = new StackPanel
        {
            Margin = new Thickness(14, 10),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "No project open.", Classes = { "muted" } },
                new TextBlock { Text = "Create one from a template, open a folder, or ask Jack to build an app.", TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, FontSize = 12 },
            },
        };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_projectName, Dock.Top);
        DockPanel.SetDock(_empty, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_projectName);
        root.Children.Add(_empty);
        root.Children.Add(_tree);
        Content = new Border { Classes = { "deck" }, Child = root };
    }

    public event Action<string>? OpenRequested;
    public event Action<string>? NewFileRequested;
    public event Action<string>? NewFolderRequested;
    public event Action<string>? RenameRequested;
    public event Action<string>? DeleteRequested;

    public void SetRoot(string? root)
    {
        _root = root;
        _projectName.Text = root is null ? "" : Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
        _empty.IsVisible = root is null;
        Refresh();
    }

    /// <summary>Rebuilds the tree from disk, keeping expanded folders expanded.</summary>
    public void Refresh()
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Collect(IEnumerable<FileNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.IsExpanded) expanded.Add(n.FullPath);
                Collect(n.Children);
            }
        }
        Collect(_roots);
        string? selected = (_tree.SelectedItem as FileNode)?.FullPath;
        _suppressOpen = true;
        _roots.Clear();
        if (_root is not null && Directory.Exists(_root))
            foreach (var n in Load(_root, expanded, depth: 0)) _roots.Add(n);
        if (selected is not null && Find(_roots, selected) is { } again) _tree.SelectedItem = again;
        _suppressOpen = false;
    }

    /// <summary>Highlights a file (e.g. the active editor tab) without opening it again.</summary>
    public void Reveal(string path)
    {
        if (_root is null || !path.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) return;
        // Expand the ancestors so the item is realised.
        for (var dir = Path.GetDirectoryName(path); dir is not null && dir.Length > _root.Length; dir = Path.GetDirectoryName(dir))
            if (Find(_roots, dir) is { } folder) folder.IsExpanded = true;
        if (Find(_roots, path) is { } node && !ReferenceEquals(_tree.SelectedItem, node))
        {
            _suppressOpen = true;
            _tree.SelectedItem = node;
            _suppressOpen = false;
        }
    }

    private void CollapseAll()
    {
        void C(IEnumerable<FileNode> nodes) { foreach (var n in nodes) { n.IsExpanded = false; C(n.Children); } }
        C(_roots);
    }

    private string SelectedFolder() => _tree.SelectedItem switch
    {
        FileNode { IsDirectory: true } d => d.FullPath,
        FileNode f => Path.GetDirectoryName(f.FullPath)!,
        _ => _root ?? "",
    };

    private static List<FileNode> Load(string dir, HashSet<string> expanded, int depth)
    {
        var list = new List<FileNode>();
        if (depth > 12) return list;
        try
        {
            foreach (var d in Directory.EnumerateDirectories(dir).Order(StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(d);
                if (WorkspacePlugin.IgnoredFolders.Contains(name) || name.StartsWith('.')) continue;
                var node = new FileNode(d, true) { IsExpanded = expanded.Contains(d) };
                foreach (var c in Load(d, expanded, depth + 1)) node.Children.Add(c);
                list.Add(node);
            }
            foreach (var f in Directory.EnumerateFiles(dir).Order(StringComparer.OrdinalIgnoreCase))
                list.Add(new FileNode(f, false));
        }
        catch (UnauthorizedAccessException) { }
        return list;
    }

    private static FileNode? Find(IEnumerable<FileNode> nodes, string path)
    {
        foreach (var n in nodes)
        {
            if (n.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)) return n;
            if (n.IsDirectory && path.StartsWith(n.FullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Find(n.Children, path) is { } hit) return hit;
        }
        return null;
    }

    private Control Row(FileNode node)
    {
        var (glyph, color) = Glyph(node);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Background = Brushes.Transparent };
        sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Mono, FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(color), Width = 20, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center });

        var menu = new ContextMenu();
        void Item(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
        string folder = node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath)!;
        if (!node.IsDirectory) Item("Open", () => OpenRequested?.Invoke(node.FullPath));
        Item("New file…", () => NewFileRequested?.Invoke(folder));
        Item("New folder…", () => NewFolderRequested?.Invoke(folder));
        Item("Rename…", () => RenameRequested?.Invoke(node.FullPath));
        Item("Delete", () => DeleteRequested?.Invoke(node.FullPath));
        Item("Reveal in File Explorer", () =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", node.IsDirectory ? $"\"{node.FullPath}\"" : $"/select,\"{node.FullPath}\""); }
            catch (System.ComponentModel.Win32Exception) { }
        });
        sp.ContextMenu = menu;
        ToolTip.SetTip(sp, node.FullPath);
        return sp;
    }

    private static (string Glyph, Color Color) Glyph(FileNode n)
    {
        if (n.IsDirectory) return ("▰", Color.Parse("#8391A8"));
        return Path.GetExtension(n.Name).ToLowerInvariant() switch
        {
            ".cs" => ("C#", Color.Parse("#3CC8B4")),
            ".axaml" or ".xaml" => ("</>", Color.Parse("#F2A33A")),
            ".csproj" or ".slnx" or ".sln" or ".props" => ("◆", Color.Parse("#A78BFA")),
            ".json" => ("{ }", Color.Parse("#FACC15")),
            ".md" => ("M↓", Color.Parse("#60A5FA")),
            ".razor" or ".html" => ("<>", Color.Parse("#F472B6")),
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".ico" or ".webp" => ("▣", Color.Parse("#22C55E")),
            ".config" or ".xml" => ("⚙", Color.Parse("#94A3B8")),
            ".csv" or ".txt" => ("≡", Color.Parse("#94A3B8")),
            _ => ("·", Color.Parse("#64748B")),
        };
    }
}
