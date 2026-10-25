using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Chrono.Net.AppGen.Ai;

namespace Chrono.Net.AppGen.Views;

using Color = global::Avalonia.Media.Color;

/// <summary>
/// Jack's work trace — the IDE's signature element. Each kernel function call is a pulse on a teal scope line:
/// the pulse height grows with the call's duration, failed calls dip below the line in red, and a running call
/// sweeps a bright marker. Reads like the oscilloscope traces in the Chrono.Net Gallery.
/// </summary>
public sealed class TraceStrip : Control
{
    private static readonly IPen Baseline = new Pen(new SolidColorBrush(Color.FromArgb(90, 60, 200, 180)), 1);
    private static readonly IPen Wave = new Pen(new SolidColorBrush(Color.FromRgb(60, 200, 180)), 1.6, lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round);
    private static readonly IPen FaultWave = new Pen(new SolidColorBrush(Color.FromRgb(240, 101, 107)), 1.6, lineJoin: PenLineJoin.Round);
    private readonly List<ToolCall> _calls = [];
    private DispatcherTimer? _sweep;
    private double _phase;

    public TraceStrip()
    {
        Height = 22;
        ClipToBounds = true;
    }

    /// <summary>Animate the sweep marker (while Jack is working) even if there are no calls yet.</summary>
    public bool IsLive
    {
        get => _sweep is not null;
        set
        {
            if (value == IsLive) return;
            if (value)
            {
                _sweep = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
                _sweep.Tick += (_, _) => { _phase = (_phase + 0.012) % 1; InvalidateVisual(); };
                _sweep.Start();
            }
            else
            {
                _sweep!.Stop();
                _sweep = null;
                InvalidateVisual();
            }
        }
    }

    public int Count => _calls.Count;

    public void Add(ToolCall call)
    {
        _calls.Add(call);
        InvalidateVisual();
    }

    public void Refresh() => InvalidateVisual();

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        double mid = b.Height * 0.62, w = b.Width;
        ctx.DrawLine(Baseline, new Point(0, mid), new Point(w, mid));
        if (_calls.Count == 0 && !IsLive) return;

        // Pulses are laid out left to right with a fixed pitch, compressing when there are many.
        int n = Math.Max(_calls.Count, 1);
        double pitch = Math.Min(26, (w - 12) / n);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(0, mid), false);
            for (int i = 0; i < _calls.Count; i++)
            {
                var c = _calls[i];
                double x = 6 + i * pitch;
                double secs = c.Done ? c.Elapsed.TotalSeconds : 0.4;
                double amp = Math.Clamp(3 + Math.Log10(1 + secs * 10) * 7, 3, mid - 2);
                if (c.Failed) amp = -Math.Min(6, b.Height - mid - 1);
                g.LineTo(new Point(x, mid));
                g.LineTo(new Point(x + pitch * 0.2, mid - amp));
                g.LineTo(new Point(x + pitch * 0.4, mid + amp * 0.25));
                g.LineTo(new Point(x + pitch * 0.55, mid));
            }
            g.LineTo(new Point(w, mid));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, _calls.Any(c => c.Failed) && _calls.All(c => c.Failed) ? FaultWave : Wave, geo);

        if (IsLive)
        {
            double x = _phase * w;
            var glow = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0, 60, 200, 180), 0), new GradientStop(Color.FromArgb(150, 60, 200, 180), 1) },
            };
            ctx.FillRectangle(glow, new Rect(Math.Max(0, x - 40), 0, Math.Min(40, x), b.Height));
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(120, 240, 220)), new Rect(x, mid - 3, 2, 6));
        }
    }
}
