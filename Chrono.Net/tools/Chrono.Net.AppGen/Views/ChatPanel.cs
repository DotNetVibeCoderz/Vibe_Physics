using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Chrono.Net.AppGen.Ai;

namespace Chrono.Net.AppGen.Views;

using Color = global::Avalonia.Media.Color;

/// <summary>A model the picker offers: provider + model id.</summary>
public sealed record ModelOption(ProviderKind Provider, string Model, bool Configured)
{
    public override string ToString() => $"{ProviderCatalog.DisplayName(Provider)} · {Model}{(Configured ? "" : "  (no key)")}";
}

/// <summary>Jack's chat panel: model picker, thread, composer with image attachments.</summary>
public sealed class ChatPanel : UserControl
{
    private readonly ComboBox _model = new() { MinWidth = 200, MaxWidth = 300, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _thread = new() { Margin = new Thickness(16, 12, 16, 12) };
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input;
    private readonly WrapPanel _attachStrip = new() { Margin = new Thickness(0, 2, 0, 4) };
    private readonly Button _send;
    private readonly Control _welcome;
    private readonly List<ChatAttachment> _attachments = [];
    private bool _busy;
    private bool _suppressModelEvent;

    public ChatPanel()
    {
        // ---- header
        var avatar = Avatar(26);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        titles.Children.Add(new TextBlock { Text = "JACK", Classes = { "panelTitle" }, Foreground = Icons.Trace });
        titles.Children.Add(new TextBlock { Text = JackPrompt.Title, FontSize = 11, Classes = { "muted" } });
        var clear = MarkdownView.MiniButton(Icons.Trash, "Clear chat thread", () => ClearRequested?.Invoke());
        var hide = MarkdownView.MiniButton(Icons.Close, "Hide chat (Ctrl+Shift+J)", () => HideRequested?.Invoke());
        var header = new DockPanel { Margin = new Thickness(12, 8, 8, 8) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        right.Children.Add(clear);
        right.Children.Add(hide);
        DockPanel.SetDock(right, Dock.Right);
        DockPanel.SetDock(avatar, Dock.Left);
        DockPanel.SetDock(titles, Dock.Left);
        header.Children.Add(right);
        header.Children.Add(avatar);
        header.Children.Add(titles);
        _model.Margin = new Thickness(10, 0, 6, 0);
        _model.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(_model);
        ToolTip.SetTip(_model, "Model used for the next message");
        _model.SelectionChanged += (_, _) =>
        {
            if (!_suppressModelEvent && _model.SelectedItem is ModelOption m) ModelChanged?.Invoke(m);
        };

        // ---- thread
        _welcome = Welcome();
        _thread.Children.Add(_welcome);
        _scroll = new ScrollViewer { Content = _thread, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        // ---- composer
        _input = new TextBox
        {
            Classes = { "composer" },
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
            MaxHeight = 220,
            PlaceholderText = "Describe the app you want, or ask about your code…",
        };
        _input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        var attach = MarkdownView.MiniButton(Icons.Attach, "Attach images", async () => await PickImagesAsync());
        _send = new Button { Classes = { "send" }, Content = Icons.Labeled(Icons.Send, "Send", 13, new SolidColorBrush(Color.Parse("#1B1206"))) };
        ToolTip.SetTip(_send, "Send (Ctrl+Enter)");
        _send.Click += (_, _) => { if (_busy) StopRequested?.Invoke(); else Send(); };
        var hint = new TextBlock { Text = "Ctrl+Enter to send", Classes = { "stat" }, FontSize = 11, Margin = new Thickness(6, 0, 0, 0) };
        var footer = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(_send, Dock.Right);
        DockPanel.SetDock(attach, Dock.Left);
        footer.Children.Add(_send);
        footer.Children.Add(attach);
        footer.Children.Add(hint);
        var composerStack = new StackPanel();
        composerStack.Children.Add(_attachStrip);
        composerStack.Children.Add(_input);
        composerStack.Children.Add(footer);
        var composer = new Border { Classes = { "composerBox" }, Margin = new Thickness(12, 6, 12, 12), Child = composerStack };

        var root = new DockPanel();
        var headerBorder = new Border { Child = header, BorderBrush = (IBrush)Application.Current!.FindResource("RuleBrush")!, BorderThickness = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(headerBorder, Dock.Top);
        DockPanel.SetDock(composer, Dock.Bottom);
        root.Children.Add(headerBorder);
        root.Children.Add(composer);
        root.Children.Add(_scroll);
        Content = new Border { Classes = { "deck" }, Child = root };
    }

    public event Action<string, IReadOnlyList<ChatAttachment>>? SendRequested;
    public event Action? StopRequested;
    public event Action? ClearRequested;
    public event Action? HideRequested;
    public event Action<ModelOption>? ModelChanged;
    /// <summary>Code block action from a message: (language, code, "copy" | "insert").</summary>
    public event Action<string, string, string>? CodeAction;

    public ModelOption? SelectedModel => _model.SelectedItem as ModelOption;

    public void SetModels(IEnumerable<ModelOption> models, ModelOption? selected)
    {
        _suppressModelEvent = true;
        var list = models.ToList();
        _model.ItemsSource = list;
        _model.SelectedItem = selected is null ? list.FirstOrDefault() : list.FirstOrDefault(m => m.Provider == selected.Provider && m.Model == selected.Model) ?? list.FirstOrDefault();
        _suppressModelEvent = false;
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        _send.Classes.Set("stop", busy);
        _send.Content = busy
            ? Icons.Labeled(Icons.Stop, "Stop", 12, new SolidColorBrush(Color.Parse("#1B1206")))
            : Icons.Labeled(Icons.Send, "Send", 13, new SolidColorBrush(Color.Parse("#1B1206")));
        _model.IsEnabled = !busy;
    }

    public void FocusInput() => _input.Focus();

    public void Prefill(string text)
    {
        _input.Text = text;
        _input.CaretIndex = text.Length;
        _input.Focus();
    }

    public void ClearThread()
    {
        _thread.Children.Clear();
        _thread.Children.Add(_welcome);
    }

    public void AddUser(string text, IReadOnlyList<ChatAttachment> attachments)
    {
        _thread.Children.Remove(_welcome);
        var stack = new StackPanel { Spacing = 6 };
        if (attachments.Count > 0)
        {
            var thumbs = new WrapPanel();
            foreach (var a in attachments) thumbs.Children.Add(Thumb(a, null));
            stack.Children.Add(thumbs);
        }
        stack.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 20 });
        _thread.Children.Add(new Border { Classes = { "userMsg" }, Child = stack });
        ScrollToEnd();
    }

    public JackMessage BeginJack()
    {
        _thread.Children.Remove(_welcome);
        var msg = new JackMessage((l, c, a) => CodeAction?.Invoke(l, c, a), ScrollToEnd);
        _thread.Children.Add(msg.Root);
        ScrollToEnd();
        return msg;
    }

    public void AddNote(string text, bool error)
    {
        _thread.Children.Remove(_welcome);
        _thread.Children.Add(new Border
        {
            Classes = { error ? "errorMsg" : "jackMsg" },
            Child = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = error ? Icons.Ink : Icons.Muted },
        });
        ScrollToEnd();
    }

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), DispatcherPriority.Background);

    // ------------------------------------------------------------------ composer

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            if (!_busy) Send();
        }
    }

    private void Send()
    {
        string text = (_input.Text ?? "").Trim();
        if (text.Length == 0 && _attachments.Count == 0) return;
        if (text.Length == 0) text = "Look at the attached image.";
        var attachments = _attachments.ToList();
        _attachments.Clear();
        _attachStrip.Children.Clear();
        _input.Text = "";
        SendRequested?.Invoke(text, attachments);
    }

    private async Task PickImagesAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Attach images for Jack",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif"] }],
        });
        foreach (var f in files)
        {
            await using var s = await f.OpenReadAsync();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            string ext = Path.GetExtension(f.Name).ToLowerInvariant();
            string mime = ext switch { ".png" => "image/png", ".webp" => "image/webp", ".gif" => "image/gif", _ => "image/jpeg" };
            AddAttachment(new ChatAttachment(f.Name, ms.ToArray(), mime));
        }
        _input.Focus();
    }

    public void AddAttachment(ChatAttachment a)
    {
        _attachments.Add(a);
        _attachStrip.Children.Add(Thumb(a, () =>
        {
            int i = _attachments.IndexOf(a);
            if (i < 0) return;
            _attachments.RemoveAt(i);
            _attachStrip.Children.RemoveAt(i);
        }));
    }

    private static Control Thumb(ChatAttachment a, Action? remove)
    {
        Control image;
        try
        {
            image = new Image { Source = new Bitmap(new MemoryStream(a.Data)), Width = 52, Height = 52, Stretch = Stretch.UniformToFill };
        }
        catch (Exception)
        {
            image = new TextBlock { Text = a.FileName, Width = 52, TextWrapping = TextWrapping.Wrap, FontSize = 10 };
        }
        var border = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = image, Margin = new Thickness(0, 0, 6, 0) };
        ToolTip.SetTip(border, a.FileName);
        if (remove is null) return border;
        var x = new Button { Classes = { "mini" }, Content = Icons.Make(Icons.Close, 9, Icons.Ink), Padding = new Thickness(3), Background = new SolidColorBrush(Color.Parse("#CC0D1017")),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
        x.Click += (_, _) => remove();
        var grid = new Grid();
        grid.Children.Add(border);
        grid.Children.Add(x);
        return grid;
    }

    // ------------------------------------------------------------------ pieces

    internal static Control Avatar(double size) => new Border
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = new SolidColorBrush(Color.Parse("#173A37")),
        BorderBrush = Icons.Trace,
        BorderThickness = new Thickness(1.2),
        Child = new TextBlock
        {
            Text = "J",
            Classes = { "display" },
            FontSize = size * 0.55,
            Foreground = Icons.Trace,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private Control Welcome()
    {
        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(0, 24, 0, 0) };
        stack.Children.Add(Avatar(44));
        stack.Children.Add(new TextBlock { Text = "Jack — The Code Bender", Classes = { "display" }, FontSize = 24 });
        stack.Children.Add(new TextBlock
        {
            Text = "Describe an app. Jack writes the code with Chrono.Net, builds it, runs the physics headless and fixes what breaks.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
            FontSize = 13,
        });
        var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var prompt in new[]
                 {
                     "Build a gantry crane that lifts a crate and plot the cable tension",
                     "Add a slider to the window that changes gravity live",
                     "Make a Newton's cradle with 7 balls",
                     "Explain what Simulation.cs does",
                     "The build fails — find and fix the errors",
                 })
        {
            var chip = new Button { Classes = { "chip" }, Content = new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 }, Margin = new Thickness(0, 0, 6, 6) };
            chip.Click += (_, _) => Prefill(prompt);
            chips.Children.Add(chip);
        }
        stack.Children.Add(chips);
        return stack;
    }
}

/// <summary>One assistant message: work trace, collapsible step list and the streamed markdown answer.</summary>
public sealed class JackMessage
{
    private readonly StackPanel _steps = new() { Spacing = 1, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Button _stepsToggle;
    private readonly TextBlock _stepsLabel = new() { Classes = { "stat" }, Margin = new Thickness(0) };
    private readonly ContentControl _body = new();
    private readonly TextBlock _status;
    private readonly Action<string, string, string> _onCode;
    private readonly Action _scroll;
    private readonly Dictionary<ToolCall, TextBlock> _rows = [];
    private readonly System.Text.StringBuilder _text = new();
    private readonly DispatcherTimer _renderTimer;
    private bool _dirty;
    private DateTime _started = DateTime.Now;

    public JackMessage(Action<string, string, string> onCode, Action scroll)
    {
        _onCode = onCode;
        _scroll = scroll;
        Trace = new TraceStrip { IsLive = true, Margin = new Thickness(0, 2, 0, 2) };
        _status = new TextBlock { Text = "thinking…", Classes = { "stat" }, Margin = new Thickness(8, 0, 0, 0), Foreground = Icons.Trace };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(ChatPanel.Avatar(20));
        head.Children.Add(new TextBlock { Text = "Jack", FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(_status);
        // A light disclosure toggle for the step list (a full Expander is too heavy inside a message).
        _stepsToggle = new Button { Classes = { "mini" }, Content = _stepsLabel, IsVisible = false, Padding = new Thickness(2, 1), HorizontalAlignment = HorizontalAlignment.Left };
        _stepsToggle.Click += (_, _) => SetStepsExpanded(!_steps.IsVisible);
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(head);
        stack.Children.Add(Trace);
        stack.Children.Add(_stepsToggle);
        stack.Children.Add(_steps);
        stack.Children.Add(_body);
        Root = new Border { Classes = { "jackMsg" }, Child = stack };

        // Re-render the markdown at most ~12 times a second while streaming.
        _renderTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
        _renderTimer.Tick += (_, _) => { if (_dirty) RenderBody(); };
        _renderTimer.Start();
    }

    public Border Root { get; }
    public TraceStrip Trace { get; }
    public string Text => _text.ToString();

    public void Append(string chunk)
    {
        _text.Append(chunk);
        _dirty = true;
        _status.Text = "writing…";
    }

    public void ToolStarted(ToolCall call)
    {
        Trace.Add(call);
        _stepsToggle.IsVisible = true;
        var row = new TextBlock { Classes = { "stat" }, Margin = new Thickness(0), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Icons.Trace };
        _rows[call] = row;
        UpdateRow(call);
        _steps.Children.Add(row);
        SetStepsExpanded(_steps.IsVisible);
        _status.Text = $"{call.Function}…";
        // Text written before a tool call belongs to an earlier "thought"; keep it but separate the next part.
        if (_text.Length > 0 && !_text.ToString().EndsWith("\n\n", StringComparison.Ordinal)) _text.Append("\n\n");
        _scroll();
    }

    public void ToolFinished(ToolCall call)
    {
        UpdateRow(call);
        Trace.Refresh();
    }

    private void SetStepsExpanded(bool expanded)
    {
        _steps.IsVisible = expanded;
        _stepsLabel.Text = $"{(expanded ? "▾" : "▸")} {_rows.Count} step{(_rows.Count == 1 ? "" : "s")}";
    }

    private void UpdateRow(ToolCall c)
    {
        if (!_rows.TryGetValue(c, out var row)) return;
        string mark = !c.Done ? "▸" : c.Failed ? "✗" : "✓";
        row.Text = $"{mark} {c.Function}  {Summ(c.Arguments)}{(c.Done ? $"  {c.Elapsed.TotalSeconds:0.0}s" : "")}";
        row.Foreground = c.Failed ? Icons.Fault : c.Done ? Icons.Muted : Icons.Trace;
        ToolTip.SetTip(row, (c.Arguments + "\n\n" + (c.Result ?? "")).Trim() is var tip && tip.Length > 1500 ? tip[..1500] + "…" : tip);
    }

    private static string Summ(string args)
    {
        // Show the most telling argument: path, query, templateId, url, expression.
        foreach (var key in new[] { "path=", "templateId=", "query=", "url=", "expression=", "typeName=", "sampleId=", "seconds=" })
        {
            int i = args.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) continue;
            string v = args[(i + key.Length)..];
            int end = v.IndexOf(", ", StringComparison.Ordinal);
            return end > 0 ? v[..end] : v;
        }
        return args.Length > 50 ? args[..50] + "…" : args;
    }

    public void Finish(bool cancelled = false)
    {
        _renderTimer.Stop();
        Trace.IsLive = false;
        RenderBody();
        SetStepsExpanded(_rows.Count <= 4);
        var secs = (DateTime.Now - _started).TotalSeconds;
        _status.Text = cancelled ? "stopped" : $"{secs:0}s · {_rows.Count} step{(_rows.Count == 1 ? "" : "s")}";
        _status.Foreground = Icons.Muted;
        if (_rows.Count == 0) Trace.IsVisible = false;
    }

    public void Fail(string message)
    {
        Finish();
        _status.Text = "error";
        _status.Foreground = Icons.Fault;
        var body = (StackPanel)((Border)Root).Child!;
        body.Children.Add(new Border
        {
            Classes = { "errorMsg" },
            Child = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 },
        });
        _scroll();
    }

    private void RenderBody()
    {
        _dirty = false;
        string text = _text.ToString().Trim();
        _body.Content = text.Length == 0 ? null : MarkdownView.Render(text, _onCode);
        _scroll();
    }
}
