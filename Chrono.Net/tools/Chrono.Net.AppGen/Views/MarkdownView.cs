using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace Chrono.Net.AppGen.Views;

using Color = global::Avalonia.Media.Color;

/// <summary>
/// Renders the markdown subset LLMs use (headings, paragraphs, bullet/numbered lists, **bold**, *italic*,
/// `code`, fenced code blocks, tables as monospace) into selectable Avalonia controls.
/// </summary>
public static partial class MarkdownView
{
    private static readonly FontFamily Mono = new("avares://Chrono.Net.Avalonia/Assets/Fonts#JetBrains Mono");
    private static readonly IBrush CodeInk = new SolidColorBrush(Color.Parse("#C9D4E5"));
    private static readonly IBrush InlineCode = new SolidColorBrush(Color.Parse("#8FE3D6"));

    /// <param name="onCode">Called with (language, code, action) when a code block button is pressed ("copy" / "insert").</param>
    public static StackPanel Render(string markdown, Action<string, string, string>? onCode = null)
    {
        var root = new StackPanel { Spacing = 6 };
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var para = new List<string>();

        void FlushPara()
        {
            if (para.Count == 0) return;
            root.Children.Add(Text(string.Join(" ", para.Select(p => p.Trim()))));
            para.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string t = line.Trim();
            if (t.StartsWith("```", StringComparison.Ordinal))
            {
                FlushPara();
                string lang = t[3..].Trim();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal); i++) code.Add(lines[i]);
                root.Children.Add(CodeBlock(lang, string.Join("\n", code), onCode));
                continue;
            }
            if (t.Length == 0) { FlushPara(); continue; }
            if (HeadingRegex().Match(t) is { Success: true } h)
            {
                FlushPara();
                int level = h.Groups[1].Length;
                var tb = Text(h.Groups[2].Value);
                tb.FontWeight = FontWeight.SemiBold;
                tb.FontSize = level <= 2 ? 15.5 : 14;
                tb.Margin = new Thickness(0, 6, 0, 0);
                root.Children.Add(tb);
                continue;
            }
            if (BulletRegex().Match(line) is { Success: true } b)
            {
                FlushPara();
                int indent = b.Groups[1].Value.Length / 2;
                root.Children.Add(ListItem("•", b.Groups[2].Value, indent));
                continue;
            }
            if (NumberRegex().Match(line) is { Success: true } n)
            {
                FlushPara();
                root.Children.Add(ListItem(n.Groups[2].Value + ".", n.Groups[3].Value, n.Groups[1].Value.Length / 2));
                continue;
            }
            if (t.StartsWith('|'))
            {
                FlushPara();
                var table = new List<string>();
                for (; i < lines.Length && lines[i].Trim().StartsWith('|'); i++)
                    if (!TableRuleRegex().IsMatch(lines[i].Trim())) table.Add(lines[i].Trim());
                i--;
                root.Children.Add(Table(table));
                continue;
            }
            if (t is "---" or "***") { FlushPara(); continue; }
            para.Add(line);
        }
        FlushPara();
        return root;
    }

    private static SelectableTextBlock Text(string text)
    {
        var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 20 };
        foreach (var inline in Inlines(text)) tb.Inlines!.Add(inline);
        return tb;
    }

    private static Control ListItem(string marker, string text, int indent)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(4 + indent * 16, 0, 0, 0) };
        var m = new TextBlock { Text = marker, Foreground = Icons.Trace, Margin = new Thickness(0, 0, 8, 0), FontSize = 13.5, LineHeight = 20 };
        var body = Text(text);
        Grid.SetColumn(body, 1);
        g.Children.Add(m);
        g.Children.Add(body);
        return g;
    }

    /// <summary>**bold**, *italic*, `code` inline spans.</summary>
    private static IEnumerable<Inline> Inlines(string text)
    {
        int pos = 0;
        foreach (Match m in InlineRegex().Matches(text))
        {
            if (m.Index > pos) yield return new Run(text[pos..m.Index]);
            if (m.Groups["code"].Success)
                yield return new Run(m.Groups["code"].Value) { FontFamily = Mono, Foreground = InlineCode, FontSize = 12.5 };
            else if (m.Groups["bold"].Success)
                yield return new Run(m.Groups["bold"].Value.Replace("`", "")) { FontWeight = FontWeight.SemiBold };
            else if (m.Groups["italic"].Success)
                yield return new Run(m.Groups["italic"].Value) { FontStyle = FontStyle.Italic };
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) yield return new Run(text[pos..]);
    }

    private static Control Table(List<string> rows)
    {
        var cells = rows.Select(r => r.Trim('|').Split('|').Select(c => c.Trim()).ToArray()).ToList();
        int cols = cells.Max(c => c.Length);
        var widths = Enumerable.Range(0, cols).Select(c => cells.Max(r => c < r.Length ? r[c].Length : 0)).ToArray();
        var lines = cells.Select(r => string.Join("  ", Enumerable.Range(0, cols).Select(c => (c < r.Length ? r[c] : "").PadRight(widths[c]))));
        return new Border
        {
            Classes = { "codeBlock" },
            Padding = new Thickness(10, 8),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new SelectableTextBlock { Text = string.Join("\n", lines), FontFamily = Mono, FontSize = 12, Foreground = CodeInk },
            },
        };
    }

    private static Control CodeBlock(string lang, string code, Action<string, string, string>? onCode)
    {
        var header = new DockPanel { Margin = new Thickness(10, 4, 4, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        if (onCode is not null)
        {
            actions.Children.Add(MiniButton(Icons.Copy, "Copy", () => onCode(lang, code, "copy")));
            actions.Children.Add(MiniButton(Icons.Insert, "Insert at cursor", () => onCode(lang, code, "insert")));
        }
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(new TextBlock { Text = lang.Length > 0 ? lang : "code", Classes = { "mono", "muted" }, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });

        var body = new ScrollViewer
        {
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Padding = new Thickness(12, 2, 12, 10),
            Content = new SelectableTextBlock { Text = code, FontFamily = Mono, FontSize = 12, Foreground = CodeInk, LineHeight = 18 },
        };
        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(body);
        return new Border { Classes = { "codeBlock" }, Child = panel };
    }

    internal static Button MiniButton(string icon, string tip, Action onClick)
    {
        var b = new Button { Classes = { "mini" }, Content = Icons.Make(icon, 13, Icons.Muted) };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    [GeneratedRegex(@"^(#{1,4})\s+(.*)$")]
    private static partial Regex HeadingRegex();
    [GeneratedRegex(@"^(\s*)[-*+]\s+(.*)$")]
    private static partial Regex BulletRegex();
    [GeneratedRegex(@"^(\s*)(\d+)[.)]\s+(.*)$")]
    private static partial Regex NumberRegex();
    [GeneratedRegex(@"^\|?\s*:?-{2,}")]
    private static partial Regex TableRuleRegex();
    [GeneratedRegex(@"`(?<code>[^`]+)`|\*\*(?<bold>[^*]+)\*\*|(?<![\w*])\*(?<italic>[^*\s][^*]*)\*(?![\w*])")]
    private static partial Regex InlineRegex();
}
