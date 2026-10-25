using System.Configuration;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Chrono.Net.AppGen.Ai;

namespace Chrono.Net.AppGen.Views;

// Jack (chat), the IIdeHost implementation, settings and shutdown.
public sealed partial class MainWindow
{
    private void WireChat()
    {
        _chat.SendRequested += (text, attachments) => _ = SendToJackAsync(text, attachments);
        _chat.StopRequested += () => _jackCts?.Cancel();
        _chat.ClearRequested += ClearChat;
        _chat.HideRequested += () => ShowPanel("chat", false);
        _chat.ModelChanged += m =>
        {
            _s.ActiveProvider = m.Provider;
            _s.ActiveModel = m.Model;
            TrySaveSettings();
            UpdateModelStatus();
            if (!m.Configured) _chat.AddNote($"{ProviderCatalog.DisplayName(m.Provider)} has no API key yet. Add it in Settings → Models (Jack menu → Models and keys).", error: false);
        };
        _chat.CodeAction += async (lang, code, action) =>
        {
            if (action == "copy")
            {
                if (Clipboard is { } cb) await cb.SetTextAsync(code);
                AppendLog(LogChannel.Output, "Copied code to the clipboard.");
            }
            else if (_active is not null) _active.InsertAtCaret(code);
            else AppendLog(LogChannel.Output, "Open a file first to insert code.");
        };
    }

    private void RefreshModels()
    {
        var options = Enum.GetValues<ProviderKind>()
            .SelectMany(k => _s.Providers[k].Models.Select(m => new ModelOption(k, m, _s.Providers[k].IsConfigured)))
            .OrderByDescending(o => o.Configured)
            .ToList();
        var selected = options.FirstOrDefault(o => o.Provider == _s.ActiveProvider && (o.Model == _s.ActiveModel || _s.ActiveModel.Length == 0))
                       ?? options.FirstOrDefault(o => o.Configured) ?? options.FirstOrDefault();
        _chat.SetModels(options, selected);
        if (selected is not null)
        {
            _s.ActiveProvider = selected.Provider;
            _s.ActiveModel = selected.Model;
        }
        UpdateModelStatus();
    }

    private void UpdateModelStatus() =>
        _statusModel.Text = _chat.SelectedModel is { } m ? $"{ProviderCatalog.DisplayName(m.Provider)} · {m.Model}" : "no model";

    private void AskJack(string text)
    {
        ShowPanel("chat", true);
        _ = SendToJackAsync(text, []);
    }

    private void AskJackToFixBuild()
    {
        if (_lastBuildErrors.Length == 0)
        {
            AskJack("Build the project and fix any errors.");
            return;
        }
        AskJack("The build failed with these errors. Fix them, build again and run the self-test:\n\n" + _lastBuildErrors);
    }

    private void ClearChat()
    {
        _jackCts?.Cancel();
        _jack.Clear();
        _chat.ClearThread();
        _statusTrace.IsVisible = false;
        _statusJack.Text = "";
    }

    /// <summary>One Jack turn, run off the UI thread; text streams into the message, tool calls into the trace.</summary>
    private async Task SendToJackAsync(string text, IReadOnlyList<ChatAttachment> attachments)
    {
        if (_jackCts is not null) return;   // a turn is already running
        if (_chat.SelectedModel is not { } model)
        {
            _chat.AddNote("No model is configured. Add a provider in Settings → Models.", error: true);
            return;
        }
        SaveAll();   // Jack reads files from disk
        _chat.AddUser(text, attachments);
        var msg = _chat.BeginJack();
        _jackMessage = msg;
        _chat.SetBusy(true);
        _statusTrace.IsVisible = true;
        _statusTrace.IsLive = true;
        _statusJack.Text = "Jack is working";
        AppendLog(LogChannel.Jack, $"── {DateTime.Now:HH:mm:ss} you: {text.Split('\n')[0]}");
        var cts = _jackCts = new CancellationTokenSource();

        // Tell Jack where the user is; the chat shows only what they typed.
        string context = _projectRoot is null
            ? "[IDE context: no project is open]"
            : $"[IDE context: project {_projectRoot}{(_active is null ? "" : $"; active file {RelativeToProject(_active.Path)}, caret line {_active.CaretLine}")}]";
        string prompt = context + "\n" + text;
        bool cancelled = false;
        try
        {
            await Task.Run(async () =>
            {
                await foreach (var chunk in _jack.SendAsync(prompt, attachments, model.Provider, model.Model, cts.Token))
                    Dispatcher.UIThread.Post(() => msg.Append(chunk));
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (JackException ex)
        {
            Dispatcher.UIThread.Post(() => msg.Fail(ex.Message));
            AppendLog(LogChannel.Jack, "error: " + ex.InnerException?.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            Dispatcher.UIThread.Post(() => msg.Fail(ex.Message));
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                msg.Finish(cancelled);
                _chat.SetBusy(false);
                _statusTrace.IsLive = false;
                _statusJack.Text = cancelled ? "Jack stopped" : $"Jack · {_statusTrace.Count} steps";
                _jackMessage = null;
                _jackCts = null;
                cts.Dispose();
                _explorer.Refresh();
                UpdateCommandStates();
            });
        }
    }

    /// <summary>The chat panel (used by tools that script the UI, e.g. the screenshot generator).</summary>
    public ChatPanel Chat => _chat;

    /// <summary>Sends a message to Jack as if typed in the chat.</summary>
    public Task AskJackAsync(string text, IReadOnlyList<ChatAttachment>? attachments = null) => SendToJackAsync(text, attachments ?? []);

    /// <summary>Appends a line to a log channel immediately (UI thread).</summary>
    public void WriteLog(LogChannel channel, string text)
    {
        AppendLog(channel, text);
        SelectLog(channel);
    }

    // ================================================================== IIdeHost

    string? IIdeHost.ProjectRoot => OnUi(() => _projectRoot);
    string? IIdeHost.ProjectFile => OnUi(() => _projectFile);
    (string Path, string Text, int Line)? IIdeHost.ActiveDocument => OnUi<(string, string, int)?>(() => _active is null ? null : (_active.Path, _active.Text, _active.CaretLine));

    public Task FileChangedAsync(string path) => Dispatcher.UIThread.InvokeAsync(() =>
    {
        foreach (var d in _docs.Where(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (!File.Exists(path)) RemoveDoc(d);
            else d.ReloadIfChangedOnDisk();
        }
        _explorer.Refresh();
    }).GetTask();

    public void Log(LogChannel channel, string text) => Dispatcher.UIThread.Post(() => AppendLog(channel, text));

    private static T OnUi<T>(Func<T> f) => Dispatcher.UIThread.CheckAccess() ? f() : Dispatcher.UIThread.Invoke(f);

    // ================================================================== settings, about, closing

    private async Task OpenSettingsAsync(string? tab)
    {
        var dlg = new SettingsDialog(_s, tab);
        await dlg.ShowDialog(this);
        if (!dlg.Saved)
        {
            _s.Reload();
            return;
        }
        ApplyEditorStyle();
        RefreshModels();
        AppendLog(LogChannel.Output, $"Settings saved to {_s.FilePath}");
    }

    private void TrySaveSettings()
    {
        try { _s.Save(); }
        catch (ConfigurationErrorsException ex) { AppendLog(LogChannel.Output, $"Could not save settings: {ex.Message}"); }
    }

    private async Task AboutAsync() => await Dialogs.ConfirmAsync(this, "About ChronoAppGen",
        $"Chrono.Net App Generator\nChrono.Net {ChronoEngine.Version} · Project Chrono {(ChronoEngine.IsAvailable ? ChronoEngine.ChronoVersion : "(native engine not found)")}\n\n" +
        "Jack — The Code Bender writes, builds and tests Chrono.Net apps with Semantic Kernel and the LLM of your choice " +
        "(OpenAI, Azure OpenAI, Claude, Gemini, Ollama, DeepSeek).\n\n" + ChronoEngine.Credits + "\n" + ChronoEngine.CreditsId, "Close");

    private bool _closingConfirmed;

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingConfirmed) return;
        var dirty = _docs.Where(d => d.IsDirty).ToList();
        if (dirty.Count > 0)
        {
            e.Cancel = true;
            if (!await ConfirmDirtyAsync(dirty)) return;
            _closingConfirmed = true;
            Close();
            return;
        }
        // Remember the layout.
        if (_explorer.IsVisible) _s.ExplorerWidth = _main.ColumnDefinitions[0].ActualWidth;
        if (_chat.IsVisible) _s.ChatWidth = _main.ColumnDefinitions[4].ActualWidth;
        if (_center.Children[2].IsVisible) _s.LogsHeight = _center.RowDefinitions[2].ActualHeight;
        TrySaveSettings();
        _jackCts?.Cancel();
        StopRun();
        _watcher?.Dispose();
    }
}
