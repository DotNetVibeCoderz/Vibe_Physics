namespace Chrono.Net.Blazor;

/// <summary>Fixed-capacity time series for <see cref="ChronoPlot"/>.</summary>
public sealed class SignalSeries(string name, string unit, string color, int capacity = 600)
{
    private readonly double[] _t = new double[capacity];
    private readonly double[] _y = new double[capacity];
    private int _start, _count;

    public string Name { get; } = name;
    public string Unit { get; } = unit;
    public string Color { get; } = color;
    public int Count => _count;
    public double Last => _count == 0 ? 0 : _y[(_start + _count - 1) % _y.Length];
    public double LastTime => _count == 0 ? 0 : _t[(_start + _count - 1) % _t.Length];

    public void Add(double t, double y)
    {
        if (!double.IsFinite(y)) return;
        int i = (_start + _count) % _t.Length;
        _t[i] = t;
        _y[i] = y;
        if (_count < _t.Length) _count++;
        else _start = (_start + 1) % _t.Length;
    }

    public (double T, double Y) this[int i]
    {
        get
        {
            int k = (_start + i) % _t.Length;
            return (_t[k], _y[k]);
        }
    }

    public static readonly string[] Palette = ["#F2A33A", "#3CC8B4", "#60A5FA", "#F472B6", "#FACC15", "#A78BFA"];
}
