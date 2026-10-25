using System.Buffers;

namespace Chrono.Net;

/// <summary>Axis-range helper for telemetry plots.</summary>
public static class SignalRange
{
    /// <summary>
    /// Returns a display range for <paramref name="values"/>. When a few outliers (e.g. the impulse of the first
    /// contact step) stretch the full range far beyond the bulk of the data, the 2nd–98th percentile range is used
    /// instead, so the signal stays readable; callers clamp points outside it. Always non-degenerate.
    /// </summary>
    public static (double Min, double Max) Robust(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty) return (-1, 1);
        double min = double.MaxValue, max = double.MinValue;
        foreach (double v in values)
        {
            if (!double.IsFinite(v)) continue;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        if (min > max) return (-1, 1);

        if (values.Length >= 20)
        {
            double[] buf = ArrayPool<double>.Shared.Rent(values.Length);
            try
            {
                int n = 0;
                foreach (double v in values) if (double.IsFinite(v)) buf[n++] = v;
                var sorted = buf.AsSpan(0, n);
                sorted.Sort();
                double lo = sorted[(int)(n * 0.02)], hi = sorted[Math.Min(n - 1, (int)(n * 0.98))];
                if (hi - lo > 1e-12 && max - min > 4 * (hi - lo)) { min = lo; max = hi; }
            }
            finally { ArrayPool<double>.Shared.Return(buf); }
        }

        if (max - min < 1e-9) { min -= 0.5; max += 0.5; }
        return (min, max);
    }
}
