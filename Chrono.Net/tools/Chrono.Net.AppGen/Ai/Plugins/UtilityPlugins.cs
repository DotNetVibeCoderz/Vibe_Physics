using System.ComponentModel;
using System.Globalization;
using Microsoft.SemanticKernel;
using NCalc;

namespace Chrono.Net.AppGen.Ai.Plugins;

/// <summary>Exact arithmetic for Jack (LLMs are unreliable calculators).</summary>
public sealed class MathPlugin
{
    [KernelFunction, Description("Evaluates a math expression exactly. Supports + - * / ^ (power), parentheses, Sqrt, Sin, Cos, Tan, Asin, Acos, Atan, Atan2, Exp, Log(x, base), Log10, Abs, Min, Max, Round, Floor, Ceiling, Pi and E. Angles are in radians.")]
    public string Calculate([Description("e.g. Sqrt(2*9.81*1.5) or 0.5*1200*(50/3.6)^2")] string expression)
    {
        try
        {
            var e = new Expression(expression.Replace("^", "**"), ExpressionOptions.IgnoreCaseAtBuiltInFunctions);
            e.Parameters["Pi"] = Math.PI;
            e.Parameters["pi"] = Math.PI;
            e.Parameters["E"] = Math.E;
            e.EvaluateFunction += (name, args) =>
            {
                if (name.Equals("Atan2", StringComparison.OrdinalIgnoreCase))
                {
                    var p = args.Parameters;
                    args.Result = Math.Atan2(Convert.ToDouble(p.Evaluate(0), CultureInfo.InvariantCulture), Convert.ToDouble(p.Evaluate(1), CultureInfo.InvariantCulture));
                }
            };
            object? result = e.Evaluate();
            return result switch
            {
                double d => d.ToString("G15", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => result?.ToString() ?? "null",
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"Could not evaluate '{expression}': {ex.Message}";
        }
    }

    [KernelFunction, Description("Converts between common units: m, cm, mm, km, in, ft, mi; kg, g, lb, t; s, min, h; m/s, km/h, mph, knot; N, kN, lbf; J, kJ, kWh; W, kW, hp; Pa, kPa, MPa, bar, psi; deg, rad.")]
    public string ConvertUnits(double value, string from, string to)
    {
        var table = new Dictionary<string, (string Dim, double Factor)>(StringComparer.OrdinalIgnoreCase)
        {
            ["m"] = ("L", 1), ["cm"] = ("L", 0.01), ["mm"] = ("L", 0.001), ["km"] = ("L", 1000), ["in"] = ("L", 0.0254), ["ft"] = ("L", 0.3048), ["mi"] = ("L", 1609.344),
            ["kg"] = ("M", 1), ["g"] = ("M", 0.001), ["lb"] = ("M", 0.45359237), ["t"] = ("M", 1000),
            ["s"] = ("T", 1), ["min"] = ("T", 60), ["h"] = ("T", 3600),
            ["m/s"] = ("V", 1), ["km/h"] = ("V", 1 / 3.6), ["mph"] = ("V", 0.44704), ["knot"] = ("V", 0.514444),
            ["N"] = ("F", 1), ["kN"] = ("F", 1000), ["lbf"] = ("F", 4.4482216),
            ["J"] = ("E", 1), ["kJ"] = ("E", 1000), ["kWh"] = ("E", 3.6e6),
            ["W"] = ("P", 1), ["kW"] = ("P", 1000), ["hp"] = ("P", 745.69987),
            ["Pa"] = ("S", 1), ["kPa"] = ("S", 1000), ["MPa"] = ("S", 1e6), ["bar"] = ("S", 1e5), ["psi"] = ("S", 6894.757),
            ["rad"] = ("A", 1), ["deg"] = ("A", Math.PI / 180),
        };
        if (!table.TryGetValue(from, out var a) || !table.TryGetValue(to, out var b)) return $"Unknown unit '{(table.ContainsKey(from) ? to : from)}'.";
        if (a.Dim != b.Dim) return $"Cannot convert {from} to {to}.";
        return string.Create(CultureInfo.InvariantCulture, $"{value} {from} = {value * a.Factor / b.Factor:G10} {to}");
    }
}

/// <summary>Date and time for Jack.</summary>
public sealed class DateTimePlugin
{
    [KernelFunction, Description("Returns the current local date and time, the time zone and UTC. Optionally for another IANA/Windows time zone id (e.g. 'Asia/Jakarta').")]
    public string GetCurrentDateTime(string timeZone = "")
    {
        var now = DateTimeOffset.Now;
        var tz = TimeZoneInfo.Local;
        if (!string.IsNullOrWhiteSpace(timeZone))
        {
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone.Trim()); }
            catch (TimeZoneNotFoundException) { return $"Unknown time zone '{timeZone}'."; }
            now = TimeZoneInfo.ConvertTime(now, tz);
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"{now:dddd, yyyy-MM-dd HH:mm:ss} ({tz.Id}, UTC{now:zzz}); UTC {now.UtcDateTime:yyyy-MM-dd HH:mm:ss}; ISO week {ISOWeek.GetWeekOfYear(now.DateTime)}");
    }

    [KernelFunction, Description("Number of days (and weeks) between two dates in yyyy-MM-dd format.")]
    public string DaysBetween(string from, string to)
    {
        if (!DateTime.TryParse(from, CultureInfo.InvariantCulture, out var a) || !DateTime.TryParse(to, CultureInfo.InvariantCulture, out var b))
            return "Use yyyy-MM-dd dates.";
        double days = (b - a).TotalDays;
        return string.Create(CultureInfo.InvariantCulture, $"{days:0.##} days ({days / 7:0.##} weeks)");
    }
}
