using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace Chrono.Net.AppGen.Editor;

using Color = global::Avalonia.Media.Color;

/// <summary>One open file: its editor control (with its own undo stack), syntax highlighting and dirty state.</summary>
public sealed class EditorDocument : IDisposable
{
    private static readonly RegistryOptions Registry = new(ThemeName.DarkPlus);
    private readonly TextMate.Installation _textMate;
    private DateTime _diskStamp;

    public EditorDocument(string path, EditorStyle style)
    {
        Path = path;
        string text = File.Exists(path) ? File.ReadAllText(path) : "";
        _diskStamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        Editor = new TextEditor
        {
            Document = new TextDocument(text),
            ShowLineNumbers = style.ShowLineNumbers,
            WordWrap = style.WordWrap,
            FontFamily = style.FontFamily,
            FontSize = style.FontSize,
            Background = new SolidColorBrush(Color.Parse("#12161F")),
            Foreground = new SolidColorBrush(Color.Parse("#DCE3EE")),
            LineNumbersForeground = new SolidColorBrush(Color.Parse("#4B5670")),
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Padding = new global::Avalonia.Thickness(6, 4, 0, 0),
        };
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = style.TabSize;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableHyperlinks = false;
        Editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.Parse("#171D29"));
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.Parse("#1E2533")), 1);
        Editor.TextArea.SelectionBrush = new SolidColorBrush(Color.Parse("#3A3CC8B4"));
        Editor.TextArea.TextView.Margin = new global::Avalonia.Thickness(12, 0, 0, 0);   // gap after the line numbers
        Editor.TextArea.Caret.CaretBrush = new SolidColorBrush(Color.Parse("#F2A33A"));

        AvaloniaEdit.Search.SearchPanel.Install(Editor);   // Ctrl+F / Ctrl+H inside the editor
        _textMate = Editor.InstallTextMate(Registry);
        if (LanguageScope(path) is { } scope) _textMate.SetGrammar(scope);
        Editor.Document.UndoStack.MarkAsOriginalFile();
        Editor.Document.UndoStack.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UndoStack.IsOriginalFile)) DirtyChanged?.Invoke(this);
        };
    }

    public string Path { get; private set; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public TextEditor Editor { get; }
    public bool IsDirty => !Editor.Document.UndoStack.IsOriginalFile;
    public int CaretLine => Editor.TextArea.Caret.Line;
    public int CaretColumn => Editor.TextArea.Caret.Column;
    public string Text => Editor.Document.Text;

    public event Action<EditorDocument>? DirtyChanged;

    public void Save()
    {
        File.WriteAllText(Path, Editor.Document.Text, new System.Text.UTF8Encoding(false));
        _diskStamp = File.GetLastWriteTimeUtc(Path);
        Editor.Document.UndoStack.MarkAsOriginalFile();
        DirtyChanged?.Invoke(this);
    }

    /// <summary>Reloads from disk when the file changed there and the editor has no unsaved edits.</summary>
    public bool ReloadIfChangedOnDisk()
    {
        if (!File.Exists(Path)) return false;
        var stamp = File.GetLastWriteTimeUtc(Path);
        if (stamp == _diskStamp || IsDirty) return false;
        _diskStamp = stamp;
        int line = CaretLine;
        Editor.Document.Text = File.ReadAllText(Path);
        Editor.Document.UndoStack.ClearAll();
        Editor.Document.UndoStack.MarkAsOriginalFile();
        GoTo(Math.Min(line, Editor.Document.LineCount), focus: false);
        DirtyChanged?.Invoke(this);
        return true;
    }

    /// <summary>Replaces the whole text as one undoable edit (used by Format Code).</summary>
    public void ReplaceAll(string text)
    {
        int line = CaretLine;
        Editor.Document.Replace(0, Editor.Document.TextLength, text);
        GoTo(Math.Min(line, Editor.Document.LineCount), focus: true);
    }

    public void GoTo(int line, int column = 1, bool focus = true)
    {
        line = Math.Clamp(line, 1, Editor.Document.LineCount);
        Editor.TextArea.Caret.Line = line;
        Editor.TextArea.Caret.Column = Math.Max(1, column);
        Editor.ScrollToLine(line);
        if (focus) Editor.TextArea.Focus();
    }

    public void InsertAtCaret(string text)
    {
        Editor.Document.Insert(Editor.TextArea.Caret.Offset, text);
        Editor.TextArea.Focus();
    }

    public void Apply(EditorStyle style)
    {
        Editor.ShowLineNumbers = style.ShowLineNumbers;
        Editor.WordWrap = style.WordWrap;
        Editor.FontSize = style.FontSize;
        Editor.FontFamily = style.FontFamily;
        Editor.Options.IndentationSize = style.TabSize;
    }

    public void Rename(string newPath) => Path = newPath;

    public void Dispose() => _textMate.Dispose();

    /// <summary>TextMate grammar scope for a file extension (AXAML/csproj use XML, Razor uses HTML).</summary>
    private static string? LanguageScope(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".axaml" or ".xaml" or ".csproj" or ".props" or ".targets" or ".config" or ".slnx" or ".resx" or ".nuspec" => ".xml",
            ".razor" or ".cshtml" => ".html",
            ".yml" => ".yaml",
            var e => e,
        };
        var lang = Registry.GetLanguageByExtension(ext);
        return lang is null ? null : Registry.GetScopeByLanguageId(lang.Id);
    }
}

/// <summary>Editor appearance shared by all tabs (from Settings → Editor).</summary>
public sealed record EditorStyle(bool ShowLineNumbers, bool WordWrap, double FontSize, FontFamily FontFamily, int TabSize);
