using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Chrono.Net.AppGen.Views;

using Color = global::Avalonia.Media.Color;

/// <summary>Line icons (24×24 paths) for the toolbar and panels.</summary>
public static class Icons
{
    public const string NewProject = "M12 5v14M5 12h14";
    public const string Open = "M3 7h6l2 2h10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2zM3 7V5a2 2 0 0 1 2-2h4l2 2";
    public const string Save = "M5 3h11l3 3v13a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM7 3v6h8V3M7 21v-7h10v7";
    public const string Close = "M6 6l12 12M18 6L6 18";
    public const string Build = "M14 6l4 4-9 9H5v-4zM12 8l4 4M16 3l5 5";
    public const string Run = "M7 4l13 8-13 8z";
    public const string Stop = "M6 6h12v12H6z";
    public const string Deploy = "M12 3v12M7 8l5-5 5 5M4 15v4a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-4";
    public const string GoTo = "M4 6h10M4 12h16M4 18h7M17 15l3 3-3 3";
    public const string Format = "M4 6h16M8 10h12M4 14h16M8 18h12";
    public const string LineNumbers = "M3 6h2M3 12h2M3 18h2M9 6h12M9 12h12M9 18h12";
    public const string Settings = "M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM19 12l2-1-1-3-2 .3-1.4-1.4L17 5l-3-1-1 2h-2l-1-2-3 1 .4 2L6 8.4 4 8l-1 3 2 1v1l-2 1 1 3 2-.3 1.4 1.4L7 19l3 1 1-2h2l1 2 3-1-.4-2 1.4-1.4 2 .4 1-3-2-1z";
    public const string Chat = "M4 5h16v11H9l-5 4z";
    public const string Explorer = "M3 4h7v16H3zM14 4h7v7h-7zM14 14h7v6h-7z";
    public const string Logs = "M4 4h16v16H4zM8 9l3 3-3 3M13 15h4";
    public const string Attach = "M21 11l-8.5 8.5a5 5 0 0 1-7-7L14 4a3.5 3.5 0 0 1 5 5l-8.5 8.5a2 2 0 0 1-3-3L15 7";
    public const string Send = "M4 12l16-8-6 16-3-7z";
    public const string Trash = "M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3";
    public const string Refresh = "M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7";
    public const string NewFile = "M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9zM14 3v6h6M12 12v6M9 15h6";
    public const string NewFolder = "M3 7h6l2 2h10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2zM12 12v5M9.5 14.5h5";
    public const string Collapse = "M6 15l6-6 6 6";
    public const string Copy = "M8 8h12v12H8zM4 16V4h12";
    public const string Insert = "M12 4v10M8 10l4 4 4-4M4 20h16";
    public const string Folder = "M3 7h6l2 2h10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z";

    public static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#DCE3EE"));
    public static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#8391A8"));
    public static readonly IBrush Signal = new SolidColorBrush(Color.Parse("#F2A33A"));
    public static readonly IBrush Trace = new SolidColorBrush(Color.Parse("#3CC8B4"));
    public static readonly IBrush Fault = new SolidColorBrush(Color.Parse("#F0656B"));

    /// <summary>A stroked icon sized for buttons.</summary>
    public static global::Avalonia.Controls.Shapes.Path Make(string data, double size = 15, IBrush? brush = null)
    {
        var p = new global::Avalonia.Controls.Shapes.Path
        {
            Data = StreamGeometry.Parse(data),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        p.Stroke = brush ?? Ink;
        return p;
    }

    /// <summary>Icon + label content for a toolbar button.</summary>
    public static StackPanel Labeled(string data, string label, double size = 14, IBrush? brush = null)
    {
        var sp = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(Make(data, size, brush));
        sp.Children.Add(new TextBlock { Text = label, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center });
        return sp;
    }
}
