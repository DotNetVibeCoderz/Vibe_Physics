using System.Diagnostics;
using System.Text;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Chrono.Net.AppGen.Ai;
using Chrono.Net.AppGen.Build;
using Chrono.Net.AppGen.Editor;
using Chrono.Net.AppGen.Projects;
using Chrono.Net.AppGen.Settings;

namespace Chrono.Net.AppGen.Views;

using Color = global::Avalonia.Media.Color;

/// <summary>
/// ChronoAppGen's IDE window: explorer (left), tabbed code editor (center), logs (bottom), Jack's chat (right),
/// menu, toolbar and status bar. It is also the <see cref="IIdeHost"/> Jack's kernel functions act through.
/// </summary>
public sealed partial class MainWindow : Window, IIdeHost
{
    private readonly AppSettings _s;
    private readonly JackAgent _jack;
    private readonly ExplorerPanel _explorer = new();
    private readonly ChatPanel _chat = new();
    private readonly Grid _main = new();
    private readonly Grid _center = new();
    private readonly GridSplitter _leftSplit = new() { ResizeDirection = GridResizeDirection.Columns, Width = 1 };
    private readonly GridSplitter _rightSplit = new() { ResizeDirection = GridResizeDirection.Columns, Width = 1 };
    private readonly GridSplitter _logSplit = new() { ResizeDirection = GridResizeDirection.Rows, Height = 1 };
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal };
    private readonly ContentControl _editorHost = new();
    private readonly List<EditorDocument> _docs = [];
    private EditorDocument? _active;

    // logs
    private readonly Dictionary<LogChannel, StringBuilder> _logs = new() { [LogChannel.Output] = new(), [LogChannel.Build] = new(), [LogChannel.Jack] = new() };
    private readonly Dictionary<LogChannel, ToggleButton> _logTabs = [];
    private readonly TextBox _logView = new() { Classes = { "log" }, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap };
    private readonly Button _fixBuild = new() { Classes = { "chip" }, Content = "Ask Jack to fix the build", IsVisible = false, Padding = new Thickness(10, 3), FontSize = 12 };
    private LogChannel _logChannel = LogChannel.Output;
    private bool _logsDirty;

    // status bar
    private readonly TextBlock _statusProject = new() { Classes = { "stat" } };
    private readonly TextBlock _statusFile = new() { Classes = { "stat" } };
    private readonly TextBlock _statusCaret = new() { Classes = { "stat" } };
    private readonly TextBlock _statusBuild = new() { Classes = { "stat" } };
    private readonly TextBlock _statusModel = new() { Classes = { "stat" }, Margin = new Thickness(0) };
    private readonly TextBlock _statusJack = new() { Classes = { "stat" }, Foreground = Icons.Trace };
    private readonly TraceStrip _statusTrace = new() { Width = 120, Height = 16, IsVisible = false, Margin = new Thickness(0, 0, 8, 0) };

    // toolbar buttons whose state changes
    private Button _btnRun = null!, _btnStop = null!, _btnBuild = null!, _btnDeploy = null!, _btnSave = null!, _btnFormat = null!, _btnGoTo = null!;

    private string? _projectRoot, _projectFile;
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _fsDebounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly HashSet<string> _fsChanged = new(StringComparer.OrdinalIgnoreCase);
    private Process? _runProcess;
    private CancellationTokenSource? _runCts, _buildCts, _jackCts;
    private JackMessage? _jackMessage;
    private string _lastBuildErrors = "";
    private bool _busyBuild;

    public MainWindow(AppSettings settings, string? startupProject = null)
    {
        _s = settings;
        Title = "Chrono.Net App Generator";
        Width = 1560;
        Height = 960;
        MinWidth = 980;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://ChronoAppGen/Assets/icon.png"))); }
        catch (Exception) { /* icon is optional */ }

        _jack = new JackAgent(_s, this);
        _jack.ToolCallStarted += c => Dispatcher.UIThread.Post(() =>
        {
            _jackMessage?.ToolStarted(c);
            _statusTrace.Add(c);
            _statusJack.Text = $"Jack · {c.Function}";
            AppendLog(LogChannel.Jack, $"▸ {c.Plugin}.{c.Function}({c.Arguments})");
        });
        _jack.ToolCallFinished += c => Dispatcher.UIThread.Post(() =>
        {
            _jackMessage?.ToolFinished(c);
            _statusTrace.Refresh();
            string first = (c.Result ?? "").Split('\n')[0];
            AppendLog(LogChannel.Jack, $"  {(c.Failed ? "✗" : "✓")} {c.Elapsed.TotalSeconds:0.0}s {(first.Length > 160 ? first[..160] + "…" : first)}");
        });

        Content = BuildLayout();
        WireExplorer();
        WireChat();
        RegisterShortcuts();
        ApplyLayoutSettings();
        RefreshModels();
        UpdateCommandStates();
        ShowEditorOrWelcome();

        _fsDebounce.Tick += (_, _) => FlushFileChanges();
        var logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        logTimer.Tick += (_, _) => FlushLogs();
        logTimer.Start();

        Opened += async (_, _) =>
        {
            AppendLog(LogChannel.Output, $"ChronoAppGen · Chrono.Net {ChronoEngine.Version} · {ChronoEngine.Credits}");
            AppendLog(LogChannel.Output, $"Settings: {_s.FilePath}");
            if (startupProject is not null) await OpenProjectAsync(startupProject);
            _chat.FocusInput();
        };
        Closing += OnClosing;
    }

    // ================================================================== layout

    private Control BuildLayout()
    {
        var menu = BuildMenu();
        var toolbar = BuildToolbar();
        var status = BuildStatusBar();

        // center: tabs + editor over logs
        var tabScroll = new ScrollViewer { Content = _tabStrip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var tabBar = new Border { Background = (IBrush)this.FindResource("RailBrush")!, Height = 34, Child = tabScroll };
        var editorArea = new DockPanel();
        DockPanel.SetDock(tabBar, Dock.Top);
        editorArea.Children.Add(tabBar);
        editorArea.Children.Add(_editorHost);

        var logs = BuildLogs();
        _center.RowDefinitions = new RowDefinitions("*,Auto,190");
        Grid.SetRow(editorArea, 0);
        Grid.SetRow(_logSplit, 1);
        Grid.SetRow(logs, 2);
        _center.Children.Add(editorArea);
        _center.Children.Add(_logSplit);
        _center.Children.Add(logs);

        _main.ColumnDefinitions = new ColumnDefinitions("260,Auto,*,Auto,420");
        Grid.SetColumn(_explorer, 0);
        Grid.SetColumn(_leftSplit, 1);
        Grid.SetColumn(_center, 2);
        Grid.SetColumn(_rightSplit, 3);
        Grid.SetColumn(_chat, 4);
        _main.Children.Add(_explorer);
        _main.Children.Add(_leftSplit);
        _main.Children.Add(_center);
        _main.Children.Add(_rightSplit);
        _main.Children.Add(_chat);

        var root = new DockPanel();
        DockPanel.SetDock(menu, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(menu);
        root.Children.Add(toolbar);
        root.Children.Add(status);
        root.Children.Add(_main);
        return root;
    }

    private Menu BuildMenu()
    {
        MenuItem Item(string header, Action action, string? gesture = null)
        {
            var mi = new MenuItem { Header = header };
            if (gesture is not null) mi.InputGesture = KeyGesture.Parse(gesture);
            mi.Click += (_, _) => action();
            return mi;
        }
        MenuItem Top(string header, params object[] items)
        {
            var mi = new MenuItem { Header = header };
            foreach (var i in items) mi.Items.Add(i is string ? new Separator() : i);
            return mi;
        }

        var recent = new MenuItem { Header = "Recent projects" };
        recent.SubmenuOpened += (_, _) =>
        {
            recent.Items.Clear();
            foreach (var p in _s.RecentProjects.Where(Directory.Exists))
                recent.Items.Add(Item(p, async () => await OpenProjectAsync(p)));
            if (recent.Items.Count == 0) recent.Items.Add(new MenuItem { Header = "(none yet)", IsEnabled = false });
        };
        recent.Items.Add(new MenuItem { Header = "(none yet)", IsEnabled = false });

        return new Menu
        {
            Classes = { "chrome" },
            Items =
            {
                Top("_File",
                    Item("New project…", async () => await NewProjectAsync(false), "Ctrl+N"),
                    Item("New project from template…", async () => await NewProjectAsync(true), "Ctrl+Shift+N"),
                    Item("Open project…", async () => await PickProjectAsync(), "Ctrl+Shift+O"),
                    Item("Open file…", async () => await PickFileAsync(), "Ctrl+O"),
                    recent, "-",
                    Item("Save", () => SaveActive(), "Ctrl+S"),
                    Item("Save all", () => SaveAll(), "Ctrl+Shift+S"),
                    Item("Close file", async () => { if (_active is not null) await CloseDocAsync(_active); }, "Ctrl+W"),
                    Item("Close project", async () => await CloseProjectAsync()), "-",
                    Item("Exit", Close, "Alt+F4")),
                Top("_Edit",
                    Item("Undo", () => _active?.Editor.Undo(), "Ctrl+Z"),
                    Item("Redo", () => _active?.Editor.Redo(), "Ctrl+Y"), "-",
                    Item("Find / replace", () => _active?.Editor.TextArea.Focus(), "Ctrl+F"),
                    Item("Go to line…", async () => await GoToLineAsync(), "Ctrl+G"),
                    Item("Format code", FormatActive, "Shift+Alt+F")),
                Top("_View",
                    Item("Explorer", () => TogglePanel("explorer"), "Ctrl+B"),
                    Item("Jack (chat)", () => TogglePanel("chat"), "Ctrl+Shift+J"),
                    Item("Logs", () => TogglePanel("logs"), "Ctrl+J"), "-",
                    Item("Line numbers", ToggleLineNumbers, "Ctrl+Alt+L"),
                    Item("Word wrap", ToggleWordWrap, "Alt+Z")),
                Top("_Build",
                    Item("Build", async () => await BuildAsync(), "Ctrl+Shift+B"),
                    Item("Run", async () => await RunAsync(), "F5"),
                    Item("Stop", StopRun, "Shift+F5"),
                    Item("Self-test (headless)", async () => await SelfTestAsync(), "Ctrl+F5"), "-",
                    Item("Deploy (publish)…", async () => await DeployAsync())),
                Top("_Jack",
                    Item("Ask Jack…", () => { ShowPanel("chat", true); _chat.FocusInput(); }, "Ctrl+I"),
                    Item("Fix build errors", AskJackToFixBuild),
                    Item("Explain current file", () => AskJack("Explain what the open file does, briefly, and suggest one improvement.")),
                    Item("Clear thread", ClearChat), "-",
                    Item("Models and keys…", async () => await OpenSettingsAsync("Models"))),
                Top("_Tools",
                    Item("Settings…", async () => await OpenSettingsAsync(null), "Ctrl+OemComma")),
                Top("_Help",
                    Item("About ChronoAppGen", async () => await AboutAsync()),
                    Item("Chrono.Net on GitHub", () => OpenUrl("https://github.com/DotNetVibeCoderz/Vibe_Physics/tree/main/Chrono.Net")),
                    Item("Project Chrono documentation", () => OpenUrl("https://api.projectchrono.org/"))),
            },
        };
    }

    private Control BuildToolbar()
    {
        Button Tool(string icon, string label, string tip, Action action, bool go = false, IBrush? brush = null)
        {
            var b = new Button { Classes = { "tool" }, Content = Icons.Labeled(icon, label, 14, brush ?? (go ? Icons.Signal : Icons.Ink)) };
            if (go) b.Classes.Add("go");
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => action();
            return b;
        }
        Button IconTool(string icon, string tip, Action action)
        {
            var b = new Button { Classes = { "tool" }, Content = Icons.Make(icon, 15, Icons.Muted), Padding = new Thickness(7, 5) };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => action();
            return b;
        }
        Control Sep() => new global::Avalonia.Controls.Shapes.Rectangle { Classes = { "sep" } };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Tool(Icons.NewProject, "New", "New project (Ctrl+N)", async () => await NewProjectAsync(false)));
        left.Children.Add(Tool(Icons.Open, "Open", "Open project or file (Ctrl+Shift+O)", async () => await PickProjectAsync()));
        left.Children.Add(_btnSave = Tool(Icons.Save, "Save", "Save (Ctrl+S)", () => SaveActive()));
        left.Children.Add(Sep());
        left.Children.Add(_btnBuild = Tool(Icons.Build, "Build", "Build (Ctrl+Shift+B)", async () => await BuildAsync()));
        left.Children.Add(_btnRun = Tool(Icons.Run, "Run", "Run (F5)", async () => await RunAsync(), go: true));
        left.Children.Add(_btnStop = Tool(Icons.Stop, "Stop", "Stop (Shift+F5)", StopRun, brush: Icons.Fault));
        left.Children.Add(_btnDeploy = Tool(Icons.Deploy, "Deploy", "Publish the app (dotnet publish)", async () => await DeployAsync()));
        left.Children.Add(Sep());
        left.Children.Add(_btnGoTo = Tool(Icons.GoTo, "Go to line", "Go to line (Ctrl+G)", async () => await GoToLineAsync()));
        left.Children.Add(_btnFormat = Tool(Icons.Format, "Format", "Format code (Shift+Alt+F)", FormatActive));
        left.Children.Add(IconTool(Icons.LineNumbers, "Show / hide line numbers (Ctrl+Alt+L)", ToggleLineNumbers));

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(IconTool(Icons.Explorer, "Explorer (Ctrl+B)", () => TogglePanel("explorer")));
        right.Children.Add(IconTool(Icons.Logs, "Logs (Ctrl+J)", () => TogglePanel("logs")));
        right.Children.Add(IconTool(Icons.Chat, "Jack (Ctrl+Shift+J)", () => TogglePanel("chat")));
        right.Children.Add(Sep());
        right.Children.Add(IconTool(Icons.Settings, "Settings (Ctrl+,)", async () => await OpenSettingsAsync(null)));

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 14, 0) };
        brand.Children.Add(new Image { Source = TryIcon(), Width = 20, Height = 20 });
        brand.Children.Add(new TextBlock { Text = "CHRONO.NET", Classes = { "display" }, FontSize = 17, LetterSpacing = 1.2, VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(new TextBlock { Text = "APP GENERATOR", Classes = { "eyebrow" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Icons.Signal });

        var dock = new DockPanel();
        DockPanel.SetDock(brand, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(brand);
        dock.Children.Add(right);
        dock.Children.Add(left);
        return new Border { Classes = { "toolbar" }, Child = dock };
    }

    private static global::Avalonia.Media.Imaging.Bitmap? TryIcon()
    {
        try { return new global::Avalonia.Media.Imaging.Bitmap(AssetLoader.Open(new Uri("avares://ChronoAppGen/Assets/icon.png"))); }
        catch (Exception) { return null; }
    }

    private Control BuildStatusBar()
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(_statusProject);
        left.Children.Add(_statusFile);
        left.Children.Add(_statusCaret);
        left.Children.Add(_statusBuild);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_statusTrace);
        right.Children.Add(_statusJack);
        right.Children.Add(_statusModel);
        var credit = new TextBlock { Text = "Gravicode Studios · Kang Fadhil", Classes = { "stat" }, Opacity = 0.6, Margin = new Thickness(18, 0, 0, 0) };
        right.Children.Add(credit);
        var dock = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(left);
        return new Border { Classes = { "status" }, Child = dock };
    }

    private Control BuildLogs()
    {
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var ch in new[] { LogChannel.Output, LogChannel.Build, LogChannel.Jack })
        {
            var t = new ToggleButton { Classes = { "logTab" }, Content = ch.ToString().ToUpperInvariant(), IsChecked = ch == _logChannel };
            t.Click += (_, _) => SelectLog(ch);
            _logTabs[ch] = t;
            tabs.Children.Add(t);
        }
        _fixBuild.Click += (_, _) => AskJackToFixBuild();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(_fixBuild);
        actions.Children.Add(MarkdownView.MiniButton(Icons.Trash, "Clear this log", () => { _logs[_logChannel].Clear(); _logView.Text = ""; }));
        actions.Children.Add(MarkdownView.MiniButton(Icons.Close, "Hide logs (Ctrl+J)", () => ShowPanel("logs", false)));
        var header = new DockPanel { Margin = new Thickness(8, 2, 6, 0) };
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(tabs);
        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer { Content = _logView, Padding = new Thickness(10, 2, 10, 6) });
        return new Border { Classes = { "deck" }, Child = panel };
    }

    // ================================================================== panels

    private void ApplyLayoutSettings()
    {
        _main.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(_s.ExplorerWidth, 160, 600));
        _main.ColumnDefinitions[4].Width = new GridLength(Math.Clamp(_s.ChatWidth, 300, 900));
        _center.RowDefinitions[2].Height = new GridLength(Math.Clamp(_s.LogsHeight, 80, 600));
        ShowPanel("explorer", _s.ExplorerVisible);
        ShowPanel("chat", _s.ChatVisible);
        ShowPanel("logs", _s.LogsVisible);
    }

    private bool IsPanelVisible(string panel) => panel switch
    {
        "explorer" => _explorer.IsVisible,
        "chat" => _chat.IsVisible,
        _ => _center.Children[2].IsVisible,
    };

    private void TogglePanel(string panel) => ShowPanel(panel, !IsPanelVisible(panel));

    private void ShowPanel(string panel, bool show)
    {
        switch (panel)
        {
            case "explorer":
                if (!show && _explorer.IsVisible) _s.ExplorerWidth = _main.ColumnDefinitions[0].ActualWidth;
                _explorer.IsVisible = _leftSplit.IsVisible = show;
                _main.ColumnDefinitions[0].Width = show ? new GridLength(Math.Clamp(_s.ExplorerWidth, 160, 600)) : new GridLength(0);
                _s.ExplorerVisible = show;
                break;
            case "chat":
                if (!show && _chat.IsVisible) _s.ChatWidth = _main.ColumnDefinitions[4].ActualWidth;
                _chat.IsVisible = _rightSplit.IsVisible = show;
                _main.ColumnDefinitions[4].Width = show ? new GridLength(Math.Clamp(_s.ChatWidth, 300, 900)) : new GridLength(0);
                _s.ChatVisible = show;
                break;
            default:
                var logs = _center.Children[2];
                if (!show && logs.IsVisible) _s.LogsHeight = _center.RowDefinitions[2].ActualHeight;
                logs.IsVisible = _logSplit.IsVisible = show;
                _center.RowDefinitions[2].Height = show ? new GridLength(Math.Clamp(_s.LogsHeight, 80, 600)) : new GridLength(0);
                _s.LogsVisible = show;
                break;
        }
    }

    // ================================================================== shortcuts

    private sealed class Relay(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }

    private void RegisterShortcuts()
    {
        void Bind(string gesture, Action action) => KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse(gesture), Command = new Relay(action) });
        Bind("Ctrl+N", async () => await NewProjectAsync(false));
        Bind("Ctrl+Shift+N", async () => await NewProjectAsync(true));
        Bind("Ctrl+Shift+O", async () => await PickProjectAsync());
        Bind("Ctrl+O", async () => await PickFileAsync());
        Bind("Ctrl+S", () => SaveActive());
        Bind("Ctrl+Shift+S", () => SaveAll());
        Bind("Ctrl+W", async () => { if (_active is not null) await CloseDocAsync(_active); });
        Bind("Ctrl+G", async () => await GoToLineAsync());
        Bind("Shift+Alt+F", FormatActive);
        Bind("Ctrl+B", () => TogglePanel("explorer"));
        Bind("Ctrl+Shift+J", () => TogglePanel("chat"));
        Bind("Ctrl+J", () => TogglePanel("logs"));
        Bind("Ctrl+Alt+L", ToggleLineNumbers);
        Bind("Alt+Z", ToggleWordWrap);
        Bind("Ctrl+Shift+B", async () => await BuildAsync());
        Bind("F5", async () => await RunAsync());
        Bind("Shift+F5", StopRun);
        Bind("Ctrl+F5", async () => await SelfTestAsync());
        Bind("Ctrl+I", () => { ShowPanel("chat", true); _chat.FocusInput(); });
        Bind("Ctrl+OemComma", async () => await OpenSettingsAsync(null));
    }

    // ================================================================== state

    private void UpdateCommandStates()
    {
        bool project = _projectFile is not null;
        bool running = _runProcess is { HasExited: false };
        _btnBuild.IsEnabled = project && !_busyBuild;
        _btnRun.IsEnabled = project && !running && !_busyBuild;
        _btnStop.IsEnabled = running || _busyBuild;
        _btnDeploy.IsEnabled = project && !_busyBuild;
        _btnSave.IsEnabled = _active is not null;
        _btnFormat.IsEnabled = _active is not null;
        _btnGoTo.IsEnabled = _active is not null;

        _statusProject.Text = _projectRoot is null ? "No project" : "◆ " + Path.GetFileName(_projectRoot);
        _statusFile.Text = _active is null ? "" : RelativeToProject(_active.Path);
        UpdateCaret();
        Title = _projectRoot is null ? "Chrono.Net App Generator" : $"{Path.GetFileName(_projectRoot)} — Chrono.Net App Generator";
    }

    private void UpdateCaret() =>
        _statusCaret.Text = _active is null ? "" : $"Ln {_active.CaretLine}, Col {_active.CaretColumn}   {Language(_active.Path)}";

    private static string Language(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "C#",
        ".axaml" or ".xaml" => "AXAML",
        ".csproj" or ".props" or ".config" or ".xml" => "XML",
        ".json" => "JSON",
        ".md" => "Markdown",
        var e => e.TrimStart('.').ToUpperInvariant(),
    };

    private string RelativeToProject(string path) =>
        _projectRoot is not null && path.StartsWith(_projectRoot, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(_projectRoot, path) : path;

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    // ================================================================== logs

    private void AppendLog(LogChannel channel, string line)
    {
        var sb = _logs[channel];
        lock (sb)
        {
            sb.AppendLine(line);
            if (sb.Length > 400_000) sb.Remove(0, sb.Length - 300_000);
        }
        if (channel == _logChannel) _logsDirty = true;
    }

    private void FlushLogs()
    {
        if (!_logsDirty) return;
        _logsDirty = false;
        var sb = _logs[_logChannel];
        lock (sb) _logView.Text = sb.ToString();
        _logView.CaretIndex = _logView.Text?.Length ?? 0;
    }

    private void SelectLog(LogChannel ch)
    {
        _logChannel = ch;
        foreach (var (k, t) in _logTabs) t.IsChecked = k == ch;
        _logsDirty = true;
        FlushLogs();
    }
}
