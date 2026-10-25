using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Chrono.Net.AppGen.Ai;
using Chrono.Net.AppGen.Settings;
using Microsoft.Extensions.AI;

namespace Chrono.Net.AppGen.Views;

/// <summary>Settings → every value in app.config, editable. Save writes the file; Cancel discards the edits.</summary>
public sealed class SettingsDialog : Window
{
    private readonly AppSettings _s;
    private readonly List<Action> _commit = [];
    private readonly ListBox _providers = new() { Width = 190, Classes = { "providers" } };
    private readonly ContentControl _providerForm = new();
    private ProviderKind _current;

    public SettingsDialog(AppSettings settings, string? initialTab = null)
    {
        _s = settings;
        Title = "Settings";
        Width = 900;
        Height = 700;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var tabs = new TabControl { Margin = new Thickness(18, 10, 18, 0) };
        tabs.Items.Add(new TabItem { Header = "Models", Content = ModelsTab() });
        tabs.Items.Add(new TabItem { Header = "Jack & tools", Content = Scroll(JackTab()) });
        tabs.Items.Add(new TabItem { Header = "Editor", Content = Scroll(EditorTab()) });
        tabs.Items.Add(new TabItem { Header = "Projects & build", Content = Scroll(ProjectsTab()) });
        if (initialTab is not null)
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().FirstOrDefault(t => (string)t.Header! == initialTab) ?? tabs.Items[0];

        var save = new Button { Content = "Save settings", Classes = { "primary" }, Padding = new Thickness(18, 8) };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 8) };
        cancel.Click += (_, _) => Close();
        var path = new TextBlock { Text = settings.FilePath, Classes = { "stat" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(path, "Settings are stored in this app.config file");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        var bottom = new DockPanel { Margin = new Thickness(18, 12, 18, 16) };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(path);

        var root = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(tabs);
        Content = root;
    }

    /// <summary>True when the user saved.</summary>
    public bool Saved { get; private set; }

    private void Save()
    {
        CommitProvider();
        foreach (var c in _commit) c();
        _s.Save();
        Saved = true;
        Close();
    }

    // ------------------------------------------------------------------ models

    private Control ModelsTab()
    {
        _providers.ItemsSource = Enum.GetValues<ProviderKind>().Select(k => new ProviderItem(k, _s.Providers[k])).ToList();
        _providers.SelectionChanged += (_, _) =>
        {
            if (_providers.SelectedItem is not ProviderItem p) return;
            CommitProvider();
            _current = p.Kind;
            _providerForm.Content = ProviderForm(_s.Providers[p.Kind]);
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 12, 0, 0) };
        var form = Scroll(_providerForm);
        Grid.SetColumn(form, 1);
        _providers.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(_providers);
        grid.Children.Add(form);
        _providers.SelectedIndex = (int)_s.ActiveProvider;
        return grid;
    }

    private sealed record ProviderItem(ProviderKind Kind, ProviderSettings Settings)
    {
        public override string ToString() => $"{(Settings.IsConfigured ? "●" : "○")}  {ProviderCatalog.DisplayName(Kind)}";
    }

    private Action? _commitProvider;

    private void CommitProvider()
    {
        _commitProvider?.Invoke();
        _commitProvider = null;
    }

    private Control ProviderForm(ProviderSettings p)
    {
        var d = ProviderCatalog.Defaults(p.Kind);
        var models = new TextBox { Text = string.Join(", ", p.Models) };
        var key = new TextBox { Text = p.ApiKey, PasswordChar = '•', PlaceholderText = d.KeyHint };
        var reveal = new CheckBox { Content = "Show", Margin = new Thickness(8, 0, 0, 0) };
        reveal.IsCheckedChanged += (_, _) => key.PasswordChar = reveal.IsChecked == true ? '\0' : '•';
        var endpoint = new TextBox { Text = p.Endpoint, PlaceholderText = d.Endpoint.Length > 0 ? d.Endpoint : "https://<resource>.openai.azure.com/" };
        var workspace = new TextBox { Text = p.WorkspaceId, PlaceholderText = "only for keys not scoped to a workspace" };
        var temp = new TextBox { Text = p.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "", PlaceholderText = "blank = provider default", Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
        var maxTokens = new NumericUpDown { Value = p.MaxTokens, Minimum = 256, Maximum = 200000, Increment = 1024, Width = 180, HorizontalAlignment = HorizontalAlignment.Left, FormatString = "0" };
        var prompt = new TextBox { Text = p.SystemPrompt, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 120, PlaceholderText = "blank = Jack's prompt from the Jack & tools tab" };
        var test = new Button { Content = "Test connection" };
        var testResult = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };

        void Commit()
        {
            p.Models = (models.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            p.ApiKey = (key.Text ?? "").Trim();
            p.Endpoint = (endpoint.Text ?? "").Trim();
            p.WorkspaceId = (workspace.Text ?? "").Trim();
            p.Temperature = double.TryParse(temp.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? Math.Clamp(t, 0, 2) : null;
            p.MaxTokens = (int)(maxTokens.Value ?? 8192);
            p.SystemPrompt = prompt.Text ?? "";
        }
        _commitProvider = Commit;
        test.Click += async (_, _) =>
        {
            Commit();
            testResult.Foreground = Icons.Muted;
            testResult.Text = "Contacting the provider…";
            test.IsEnabled = false;
            try
            {
                string model = p.Models.FirstOrDefault() ?? "";
                using var client = ProviderCatalog.CreateClient(p, model);
                var options = new ChatOptions { MaxOutputTokens = 64 };
                var r = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Reply with the single word: ready")], options);
                testResult.Foreground = Icons.Trace;
                testResult.Text = $"Connected to {model}: “{r.Text.Trim()}”";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                testResult.Foreground = Icons.Fault;
                testResult.Text = ex.Message.Length > 300 ? ex.Message[..300] + "…" : ex.Message;
            }
            finally { test.IsEnabled = true; }
        };

        var sp = new StackPanel { Margin = new Thickness(22, 0, 8, 12), Spacing = 0 };
        sp.Children.Add(new TextBlock { Text = ProviderCatalog.DisplayName(p.Kind), Classes = { "h1" }, FontSize = 24 });
        sp.Children.Add(new TextBlock
        {
            Text = p.Kind switch
            {
                ProviderKind.Claude => "Anthropic API (native SDK).",
                ProviderKind.Ollama => "Local models through Ollama's OpenAI-compatible endpoint. No key needed.",
                ProviderKind.Gemini => "Google Gemini through its OpenAI-compatible endpoint.",
                ProviderKind.AzureOpenAI => "Azure OpenAI resource (v1 API). Models are your deployment names.",
                ProviderKind.DeepSeek => "DeepSeek API (OpenAI-compatible).",
                _ => "OpenAI API, or any OpenAI-compatible endpoint.",
            },
            Classes = { "muted" },
            FontSize = 12.5,
        });
        sp.Children.Add(Label("Models (comma separated, the first is the default)"));
        sp.Children.Add(models);
        sp.Children.Add(Label("API key"));
        var keyRow = new DockPanel();
        DockPanel.SetDock(reveal, Dock.Right);
        keyRow.Children.Add(reveal);
        keyRow.Children.Add(key);
        sp.Children.Add(keyRow);
        sp.Children.Add(Label("Endpoint"));
        sp.Children.Add(endpoint);
        if (p.Kind == ProviderKind.Claude)
        {
            sp.Children.Add(Label("Workspace ID"));
            sp.Children.Add(workspace);
        }
        var nums = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        var tcol = new StackPanel();
        tcol.Children.Add(Label("Temperature (0 – 2)"));
        tcol.Children.Add(temp);
        var mcol = new StackPanel();
        mcol.Children.Add(Label("Max output tokens"));
        mcol.Children.Add(maxTokens);
        nums.Children.Add(tcol);
        nums.Children.Add(mcol);
        sp.Children.Add(nums);
        sp.Children.Add(Label("System prompt override for this provider"));
        sp.Children.Add(prompt);
        var testRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        testRow.Children.Add(test);
        testRow.Children.Add(testResult);
        sp.Children.Add(testRow);
        return sp;
    }

    // ------------------------------------------------------------------ Jack & tools

    private Control JackTab()
    {
        var prompt = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(_s.SystemPrompt) ? JackPrompt.Default : _s.SystemPrompt,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 300,
            FontFamily = (FontFamily)Application.Current!.FindResource("MonoFont")!,
            FontSize = 12,
        };
        var reset = new Button { Content = "Reset to Jack's default prompt", Margin = new Thickness(0, 8, 0, 0) };
        reset.Click += (_, _) => prompt.Text = JackPrompt.Default;
        var rounds = new NumericUpDown { Value = _s.MaxToolRounds, Minimum = 4, Maximum = 200, Width = 180, HorizontalAlignment = HorizontalAlignment.Left, FormatString = "0" };
        var tavily = new TextBox { Text = _s.TavilyApiKey, PasswordChar = '•', PlaceholderText = "tvly-…" };
        _commit.Add(() =>
        {
            string text = prompt.Text ?? "";
            // Store blank when unchanged, so future versions of the built-in prompt apply.
            _s.SystemPrompt = text.Trim() == JackPrompt.Default.Trim() ? "" : text;
            _s.MaxToolRounds = (int)(rounds.Value ?? 40);
            _s.TavilyApiKey = (tavily.Text ?? "").Trim();
        });

        var sp = Page();
        sp.Children.Add(Label("Jack's system prompt (used by every provider unless overridden)"));
        sp.Children.Add(prompt);
        sp.Children.Add(reset);
        sp.Children.Add(Label("Maximum tool calls per message"));
        sp.Children.Add(rounds);
        sp.Children.Add(Label("Tavily API key (Search internet)"));
        sp.Children.Add(tavily);
        sp.Children.Add(new TextBlock
        {
            Text = "Jack's tools: project files (read, write, edit, search), build, self-test and launch, NuGet packages, the Chrono.Net guide, API reference and samples, project templates, internet search (Tavily), web page reading, exact math and unit conversion, date and time.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
            FontSize = 12,
            Margin = new Thickness(0, 14, 0, 0),
        });
        return sp;
    }

    // ------------------------------------------------------------------ editor

    private Control EditorTab()
    {
        var size = new NumericUpDown { Value = (decimal)_s.EditorFontSize, Minimum = 8, Maximum = 32, Increment = 1, Width = 160, HorizontalAlignment = HorizontalAlignment.Left, FormatString = "0" };
        var family = new TextBox { Text = _s.EditorFontFamily };
        var lineNumbers = new CheckBox { Content = "Show line numbers", IsChecked = _s.ShowLineNumbers };
        var wrap = new CheckBox { Content = "Word wrap", IsChecked = _s.WordWrap };
        var tab = new NumericUpDown { Value = _s.TabSize, Minimum = 1, Maximum = 8, Width = 160, HorizontalAlignment = HorizontalAlignment.Left, FormatString = "0" };
        _commit.Add(() =>
        {
            _s.EditorFontSize = (double)(size.Value ?? 14);
            _s.EditorFontFamily = string.IsNullOrWhiteSpace(family.Text) ? "JetBrains Mono" : family.Text.Trim();
            _s.ShowLineNumbers = lineNumbers.IsChecked == true;
            _s.WordWrap = wrap.IsChecked == true;
            _s.TabSize = (int)(tab.Value ?? 4);
        });
        var sp = Page();
        sp.Children.Add(Label("Font size"));
        sp.Children.Add(size);
        sp.Children.Add(Label("Font family (JetBrains Mono is bundled)"));
        sp.Children.Add(family);
        sp.Children.Add(Label("Indent size"));
        sp.Children.Add(tab);
        sp.Children.Add(Label("Display"));
        sp.Children.Add(lineNumbers);
        sp.Children.Add(wrap);
        return sp;
    }

    // ------------------------------------------------------------------ projects & build

    private Control ProjectsTab()
    {
        var root = new TextBox { Text = _s.ProjectsRoot };
        var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += async (_, _) =>
        {
            var f = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Projects folder" });
            if (f.FirstOrDefault()?.TryGetLocalPath() is { } p) root.Text = p;
        };
        var chrono = new TextBox { Text = _s.ChronoNetVersion, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
        var avalonia = new TextBox { Text = _s.AvaloniaVersion, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
        var feed = new TextBox { Text = _s.LocalPackageFeed, PlaceholderText = Projects.ProjectGenerator.ResolveFeed(null) is { } auto ? $"auto: {auto}" : "blank = nuget.org" };
        var config = new ComboBox { ItemsSource = new[] { "Debug", "Release" }, SelectedItem = _s.BuildConfiguration, Width = 180 };
        var rid = new ComboBox { ItemsSource = new[] { "win-x64" }, SelectedItem = _s.DeployRuntime, Width = 180 };
        var selfContained = new CheckBox { Content = "Self-contained (bundle the .NET runtime)", IsChecked = _s.DeploySelfContained };
        var single = new CheckBox { Content = "Single file", IsChecked = _s.DeploySingleFile };
        _commit.Add(() =>
        {
            _s.ProjectsRoot = (root.Text ?? "").Trim();
            _s.ChronoNetVersion = (chrono.Text ?? "0.1.0").Trim();
            _s.AvaloniaVersion = (avalonia.Text ?? "12.1.4").Trim();
            _s.LocalPackageFeed = (feed.Text ?? "").Trim();
            _s.BuildConfiguration = config.SelectedItem as string ?? "Debug";
            _s.DeployRuntime = rid.SelectedItem as string ?? "win-x64";
            _s.DeploySelfContained = selfContained.IsChecked == true;
            _s.DeploySingleFile = single.IsChecked == true;
        });
        var sp = Page();
        sp.Children.Add(Label("New projects are created in"));
        var row = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(root);
        sp.Children.Add(row);
        sp.Children.Add(Label("Chrono.Net package version"));
        sp.Children.Add(chrono);
        sp.Children.Add(Label("Avalonia version"));
        sp.Children.Add(avalonia);
        sp.Children.Add(Label("Local package folder for Chrono.Net (.nupkg)"));
        sp.Children.Add(feed);
        sp.Children.Add(Label("Build configuration"));
        sp.Children.Add(config);
        sp.Children.Add(Label("Deploy (dotnet publish)"));
        sp.Children.Add(rid);
        sp.Children.Add(selfContained);
        sp.Children.Add(single);
        return sp;
    }

    // ------------------------------------------------------------------ helpers

    private static StackPanel Page() => new() { Margin = new Thickness(4, 12, 12, 12), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };

    private static TextBlock Label(string text) => new() { Text = text, Classes = { "label" } };

    private static ScrollViewer Scroll(Control c) => new() { Content = c, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
}
