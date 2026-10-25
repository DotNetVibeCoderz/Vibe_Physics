using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Chrono.Net.Avalonia;

using Color = global::Avalonia.Media.Color;

/// <summary>A fixed-capacity time series (ring buffer) for live plotting.</summary>
public sealed class PlotSeries(string name, string unit, Color color, int capacity = 1200)
{
    private readonly double[] _t = new double[capacity];
    private readonly double[] _y = new double[capacity];
    private int _start, _count;

    public string Name { get; } = name;
    public string Unit { get; } = unit;
    public Color Color { get; set; } = color;
    public int Count => _count;
    public double Last => _count == 0 ? 0 : _y[(_start + _count - 1) % _y.Length];

    public void Add(double t, double y)
    {
        if (double.IsNaN(y) || double.IsInfinity(y)) return;
        int i = (_start + _count) % _t.Length;
        _t[i] = t;
        _y[i] = y;
        if (_count < _t.Length) _count++;
        else _start = (_start + 1) % _t.Length;
    }

    public void Clear() => _start = _count = 0;

    public (double T, double Y) this[int i]
    {
        get
        {
            int k = (_start + i) % _t.Length;
            return (_t[k], _y[k]);
        }
    }
}

/// <summary>
/// Lightweight live line chart for simulation signals. Each series gets its own vertical scale band when
/// <see cref="Stacked"/> is true (default), otherwise all share one autoscaled axis.
/// </summary>
public class ChronoPlot : Control
{
    public static readonly StyledProperty<bool> StackedProperty =
        AvaloniaProperty.Register<ChronoPlot, bool>(nameof(Stacked), true);

    public static readonly StyledProperty<double> TimeWindowProperty =
        AvaloniaProperty.Register<ChronoPlot, double>(nameof(TimeWindow), 10);

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<ChronoPlot, IBrush?>(nameof(Background), new SolidColorBrush(Color.FromRgb(15, 19, 28)));

    private static readonly Typeface Mono = new("Consolas, Cascadia Mono, monospace");

    public bool Stacked { get => GetValue(StackedProperty); set => SetValue(StackedProperty, value); }
    /// <summary>Visible time span (s).</summary>
    public double TimeWindow { get => GetValue(TimeWindowProperty); set => SetValue(TimeWindowProperty, value); }
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    public List<PlotSeries> Series { get; } = [];

    public static readonly Color[] DefaultColors =
    [
        Color.Parse("#F28C28"), Color.Parse("#18B6A3"), Color.Parse("#60A5FA"), Color.Parse("#F472B6"),
        Color.Parse("#FACC15"), Color.Parse("#A78BFA"),
    ];

    public PlotSeries AddSeries(string name, string unit = "")
    {
        var s = new PlotSeries(name, unit, DefaultColors[Series.Count % DefaultColors.Length]);
        Series.Add(s);
        return s;
    }

    public void Clear()
    {
        Series.Clear();
        InvalidateVisual();
    }

    public void Refresh() => InvalidateVisual();

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        if (Background is { } bg) ctx.FillRectangle(bg, new Rect(b.Size), 10);
        if (Series.Count == 0 || b.Width < 40 || b.Height < 30) return;

        double tMax = Series.Max(s => s.Count > 0 ? s[s.Count - 1].T : 0);
        double tMin = tMax - TimeWindow;
        int bands = Stacked ? Series.Count : 1;
        double pad = 10, bandH = (b.Height - pad) / bands;
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), 1);

        for (int si = 0; si < Series.Count; si++)
        {
            var s = Series[si];
            int band = Stacked ? si : 0;
            var area = new Rect(pad, pad / 2 + band * bandH + 18, b.Width - 2 * pad, bandH - 26);
            if (area.Height < 8) continue;

            var window = new List<double>(s.Count);
            for (int i = 0; i < s.Count; i++)
            {
                var (t, y) = s[i];
                if (t >= tMin) window.Add(y);
            }
            var (yMin, yMax) = SignalRange.Robust(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(window));
            double margin = (yMax - yMin) * 0.08;
            yMin -= margin; yMax += margin;

            if (Stacked || si == 0)
            {
                for (int g = 0; g <= 2; g++)
                {
                    double gy = area.Top + area.Height * g / 2;
                    ctx.DrawLine(gridPen, new Point(area.Left, gy), new Point(area.Right, gy));
                }
            }

            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                bool started = false;
                for (int i = 0; i < s.Count; i++)
                {
                    var (t, y) = s[i];
                    if (t < tMin) continue;
                    var p = new Point(area.Left + (t - tMin) / TimeWindow * area.Width,
                        area.Bottom - Math.Clamp((y - yMin) / (yMax - yMin), 0, 1) * area.Height);
                    if (!started) { g.BeginFigure(p, false); started = true; }
                    else g.LineTo(p);
                }
            }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(s.Color), 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);

            string label = $"{s.Name}  {s.Last.ToString("0.###", CultureInfo.InvariantCulture)} {s.Unit}";
            var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 11, new SolidColorBrush(s.Color));
            double ly = Stacked ? area.Top - 16 : pad / 2 + si * 14;
            ctx.DrawText(ft, new Point(area.Left, ly));
            if (Stacked)
            {
                var range = new FormattedText($"{yMax:0.##} / {yMin:0.##}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 9,
                    new SolidColorBrush(Color.FromArgb(110, 203, 213, 225)));
                ctx.DrawText(range, new Point(area.Right - range.Width, area.Top - 15));
            }
        }
    }
}
