using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Chrono.Net.AppGen.Views;

/// <summary>Small modal dialogs: Go To Line, text prompt, confirm.</summary>
public static class Dialogs
{
    private static Window Shell(string title, double width) => new()
    {
        Title = title,
        Width = width,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ShowInTaskbar = false,
    };

    public static async Task<int?> GoToLineAsync(Window owner, int current, int max)
    {
        var w = Shell("Go to line", 340);
        var box = new TextBox { Text = current.ToString(), PlaceholderText = $"1 – {max}" };
        var error = new TextBlock { Foreground = Icons.Fault, FontSize = 12, IsVisible = false };
        int? result = null;
        void Accept()
        {
            if (int.TryParse(box.Text, out int line) && line >= 1 && line <= max) { result = line; w.Close(); }
            else { error.Text = $"Enter a line number from 1 to {max}."; error.IsVisible = true; }
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Accept(); else if (e.Key == Key.Escape) w.Close(); };
        w.Content = Body($"Line (1 – {max})", box, error, Buttons(("Go", true, Accept), ("Cancel", false, w.Close)));
        w.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        await w.ShowDialog(owner);
        return result;
    }

    public static async Task<string?> PromptAsync(Window owner, string title, string label, string initial = "", string accept = "OK")
    {
        var w = Shell(title, 420);
        var box = new TextBox { Text = initial };
        string? result = null;
        void Accept()
        {
            if (!string.IsNullOrWhiteSpace(box.Text)) { result = box.Text.Trim(); w.Close(); }
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Accept(); else if (e.Key == Key.Escape) w.Close(); };
        w.Content = Body(label, box, null, Buttons((accept, true, Accept), ("Cancel", false, w.Close)));
        w.Opened += (_, _) =>
        {
            box.Focus();
            int dot = initial.LastIndexOf('.');
            box.SelectionStart = 0;
            box.SelectionEnd = dot > 0 ? dot : initial.Length;
        };
        await w.ShowDialog(owner);
        return result;
    }

    /// <summary>Shows a message with buttons; returns the chosen button's label (or null when closed).</summary>
    public static async Task<string?> ConfirmAsync(Window owner, string title, string message, params string[] buttons)
    {
        var w = Shell(title, 440);
        string? result = null;
        var defs = buttons.Select((b, i) => (b, i == 0, (Action)(() => { result = b; w.Close(); }))).ToArray();
        w.Content = Body(null, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 20 }, null, Buttons(defs));
        await w.ShowDialog(owner);
        return result;
    }

    private static Control Body(string? label, Control main, Control? extra, Control buttons)
    {
        var sp = new StackPanel { Margin = new Thickness(20, 16, 20, 18), Spacing = 8 };
        if (label is not null) sp.Children.Add(new TextBlock { Text = label, Classes = { "muted" }, FontSize = 12.5 });
        sp.Children.Add(main);
        if (extra is not null) sp.Children.Add(extra);
        sp.Children.Add(buttons);
        return sp;
    }

    private static Control Buttons(params (string Label, bool Primary, Action Click)[] defs)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (label, primary, click) in defs)
        {
            var b = new Button { Content = label, MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (primary) b.Classes.Add("primary");
            b.Click += (_, _) => click();
            sp.Children.Add(b);
        }
        return sp;
    }
}
