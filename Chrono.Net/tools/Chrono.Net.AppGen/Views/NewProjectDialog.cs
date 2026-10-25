using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Chrono.Net.AppGen.Projects;

namespace Chrono.Net.AppGen.Views;

/// <summary>New Project: Blank or From Template (filtered by category), then name and location.</summary>
public sealed class NewProjectDialog : Window
{
    private readonly WrapPanel _cards = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox _search = new() { PlaceholderText = "Search templates", Width = 260 };
    private readonly TextBox _name = new() { Width = 260 };
    private readonly TextBox _location = new() { MinWidth = 360 };
    private readonly TextBlock _error = new() { Foreground = Icons.Fault, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _nav = new() { Spacing = 2 };
    private readonly TextBlock _heading = new() { Classes = { "h1" } };
    private readonly TextBlock _subheading = new() { Classes = { "muted" }, FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private string _mode = "Blank";
    private string _category = "All";
    private ProjectTemplate? _selected;
    private bool _nameEdited;

    public NewProjectDialog(string defaultLocation, bool startWithTemplates = false)
    {
        Title = "New project";
        Width = 1040;
        Height = 720;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _location.Text = defaultLocation;
        _mode = startWithTemplates ? "Template" : "Blank";

        _search.TextChanged += (_, _) => RenderCards();
        _name.TextChanged += (_, _) => { if (_name.IsFocused) _nameEdited = true; _error.Text = ""; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        // ---- left navigation
        var navHost = new Border { Classes = { "deck" }, Width = 230, Padding = new Thickness(12, 18, 12, 12), Child = _nav };
        BuildNav();

        // ---- main
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(_search, Dock.Right);
        _search.VerticalAlignment = VerticalAlignment.Bottom;
        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(_heading);
        titles.Children.Add(_subheading);
        top.Children.Add(_search);
        top.Children.Add(titles);

        var scroll = new ScrollViewer { Content = _cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        // ---- bottom bar
        var browse = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Project location", AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } p) _location.Text = p;
        };
        var create = new Button { Content = "Create project", Classes = { "primary" }, Padding = new Thickness(18, 8) };
        create.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 8) };
        cancel.Click += (_, _) => Close();
        // name | location (stretches) + browse | create, cancel
        _name.Width = double.NaN;
        _location.MinWidth = 0;
        var loc = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        loc.Children.Add(browse);
        loc.Children.Add(_location);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 0, 0) };
        buttons.Children.Add(create);
        buttons.Children.Add(cancel);
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*,Auto"), ColumnSpacing = 12 };
        var nameField = Field("Project name", _name);
        var locField = Field("Location", loc);
        Grid.SetColumn(locField, 1);
        Grid.SetColumn(buttons, 2);
        fields.Children.Add(nameField);
        fields.Children.Add(locField);
        fields.Children.Add(buttons);
        var bottom = new StackPanel { Margin = new Thickness(0, 14, 0, 0), Spacing = 6 };
        bottom.Children.Add(fields);
        bottom.Children.Add(_error);
        var bottomBorder = new Border { BorderBrush = (IBrush)Application.Current!.FindResource("RuleBrush")!, BorderThickness = new Thickness(0, 1, 0, 0), Child = bottom };

        var main = new DockPanel { Margin = new Thickness(24, 20, 24, 18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottomBorder, Dock.Bottom);
        main.Children.Add(top);
        main.Children.Add(bottomBorder);
        main.Children.Add(scroll);

        var root = new DockPanel();
        DockPanel.SetDock(navHost, Dock.Left);
        root.Children.Add(navHost);
        root.Children.Add(main);
        Content = root;
        RenderCards();
    }

    /// <summary>The chosen template, project name and parent folder; null when cancelled.</summary>
    public (ProjectTemplate Template, string Name, string Location)? Result { get; private set; }

    private static Control Field(string label, Control input)
    {
        var sp = new StackPanel { Spacing = 4 };
        sp.Children.Add(new TextBlock { Text = label, Classes = { "muted" }, FontSize = 12 });
        sp.Children.Add(input);
        return sp;
    }

    private void BuildNav()
    {
        _nav.Children.Clear();
        _nav.Children.Add(new TextBlock { Text = "NEW PROJECT", Classes = { "panelTitle" }, Margin = new Thickness(6, 0, 0, 10) });
        _nav.Children.Add(NavButton("Blank", "Blank", TemplateCatalog.BlankTemplates.Count()));
        _nav.Children.Add(NavButton("From template", "Template", TemplateCatalog.Gallery.Count()));
        if (_mode != "Template") return;
        _nav.Children.Add(new TextBlock { Text = "CATEGORIES", Classes = { "panelTitle" }, Margin = new Thickness(6, 18, 0, 6) });
        _nav.Children.Add(CategoryButton("All", TemplateCatalog.Gallery.Count()));
        foreach (var c in TemplateCatalog.Categories)
            _nav.Children.Add(CategoryButton(c, TemplateCatalog.Gallery.Count(t => t.Category == c)));
    }

    private Button NavButton(string label, string mode, int count)
    {
        var b = new Button { Classes = { "sample" }, HorizontalAlignment = HorizontalAlignment.Stretch, Content = Row(label, count) };
        if (_mode == mode) b.Classes.Add("active");
        b.Click += (_, _) =>
        {
            _mode = mode;
            _category = "All";
            _selected = null;
            BuildNav();
            RenderCards();
        };
        return b;
    }

    private Button CategoryButton(string category, int count)
    {
        var b = new Button { Classes = { "sample" }, HorizontalAlignment = HorizontalAlignment.Stretch, Content = Row(category, count), Padding = new Thickness(18, 6, 10, 6) };
        if (_category == category) b.Classes.Add("active");
        b.Click += (_, _) =>
        {
            _category = category;
            BuildNav();
            RenderCards();
        };
        return b;
    }

    private static Control Row(string label, int count)
    {
        var d = new DockPanel();
        var n = new TextBlock { Text = count.ToString(), Classes = { "stat" }, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(n, Dock.Right);
        d.Children.Add(n);
        d.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return d;
    }

    private void RenderCards()
    {
        string q = (_search.Text ?? "").Trim();
        var source = _mode == "Blank" ? TemplateCatalog.BlankTemplates : TemplateCatalog.Gallery.Where(t => _category == "All" || t.Category == _category);
        var list = source.Where(t => q.Length == 0 || t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                     t.Tags.Any(tag => tag.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        _heading.Text = _mode == "Blank" ? "Start blank" : _category == "All" ? "Start from a template" : _category;
        _subheading.Text = _mode == "Blank"
            ? "An empty app wired to Chrono.Net. Describe what you want to Jack and he fills it in."
            : "Every template is a complete app that builds and runs: a 3D viewport, live plots and a scene you can change.";
        _search.IsVisible = _mode != "Blank";
        if (_selected is null || !list.Contains(_selected)) Select(list.FirstOrDefault());
        _cards.Children.Clear();
        foreach (var t in list) _cards.Children.Add(Card(t));
        if (list.Count == 0) _cards.Children.Add(new TextBlock { Text = "No template matches the search.", Classes = { "muted" }, Margin = new Thickness(4, 10) });
    }

    private void Select(ProjectTemplate? t)
    {
        _selected = t;
        if (t is not null && !_nameEdited) _name.Text = UniqueName(ProjectGenerator.ToIdentifier(t.IsBlank ? (t.Kind == AppKind.Console ? "ChronoConsole" : "ChronoApp") : t.Name));
        foreach (var c in _cards.Children.OfType<Border>()) c.Classes.Set("selected", ReferenceEquals(c.Tag, t));
    }

    private string UniqueName(string baseName)
    {
        string loc = _location.Text ?? "";
        string name = baseName;
        for (int i = 2; Directory.Exists(Path.Combine(loc, name)); i++) name = baseName + i;
        return name;
    }

    private Border Card(ProjectTemplate t)
    {
        var sp = new StackPanel { Spacing = 5 };
        var eyebrow = new DockPanel();
        var kind = new TextBlock { Text = t.Kind == AppKind.Desktop ? "DESKTOP" : "CONSOLE", Classes = { "stat" }, Margin = new Thickness(0), Foreground = t.Kind == AppKind.Desktop ? Icons.Signal : Icons.Trace };
        DockPanel.SetDock(kind, Dock.Right);
        eyebrow.Children.Add(kind);
        eyebrow.Children.Add(new TextBlock { Text = t.Category.ToUpperInvariant(), Classes = { "eyebrow" }, FontSize = 11.5 });
        sp.Children.Add(eyebrow);
        sp.Children.Add(new TextBlock { Text = t.Name, Classes = { "display" }, FontSize = 19, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(new TextBlock { Text = t.Description, TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, FontSize = 12.5, LineHeight = 18 });
        sp.Children.Add(new TextBlock { Text = string.Join("  ·  ", t.Tags), Classes = { "stat" }, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 10.5 });
        var card = new Border { Classes = { "card" }, Width = 352, MinHeight = 170, Margin = new Thickness(0, 0, 12, 12), Child = sp, Tag = t };
        card.PointerPressed += (_, e) =>
        {
            Select(t);
            if (e.ClickCount == 2) Accept();
        };
        if (ReferenceEquals(t, _selected)) card.Classes.Add("selected");
        return card;
    }

    private void Accept()
    {
        if (_selected is null) { _error.Text = "Choose a template."; return; }
        string name = (_name.Text ?? "").Trim();
        string loc = (_location.Text ?? "").Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { _error.Text = "Enter a project name without \\ / : * ? \" < > |."; return; }
        if (loc.Length == 0) { _error.Text = "Choose a location."; return; }
        if (Directory.Exists(Path.Combine(loc, name)) && Directory.EnumerateFileSystemEntries(Path.Combine(loc, name)).Any())
        {
            _error.Text = $"A folder named {name} already exists there. Choose another name.";
            return;
        }
        Result = (_selected, name, loc);
        Close();
    }
}
