namespace Chrono.Net.Samples;

/// <summary>Small helpers shared by the samples to keep scene code readable.</summary>
public static class Build
{
    /// <summary>Fixed ground slab whose top surface is at height 0 (Y-up).</summary>
    public static ChBody Ground(ChSystem sys, ContactMaterial? mat = null, double size = 20, Color? color = null) =>
        sys.Add(ChBody.CreateBox(new(size, 1, size), 1000, mat ?? sys.Own(new ContactMaterial(sys.ContactMethod)))
            .At(0, -0.5, 0).Fixed().Named("ground").Colored(color ?? Color.FromHex("#2A3342")));

    /// <summary>Invisible, non-colliding fixed body used as the reference for joints.</summary>
    public static ChBody Anchor(ChSystem sys) =>
        sys.Add(new ChBody { Name = "anchor", IsFixed = true, CollisionEnabled = false });

    /// <summary>A bar-shaped body spanning from <paramref name="a"/> to <paramref name="b"/> (no collision).</summary>
    public static ChBody Bar(ChSystem sys, Vec3 a, Vec3 b, double thickness, Color color, double density = 2700)
    {
        var d = b - a;
        var bar = ChBody.CreateBox(new(d.Length, thickness, thickness), density, collide: false);
        bar.Position = (a + b) * 0.5;
        bar.Rotation = Quat.FromTo(Vec3.UnitX, d);
        bar.SetColor(color);
        // Round ends make linkages read better.
        var r = thickness * 0.75;
        bar.AddVisualCylinder(r, thickness * 1.3, color, new Frame(new Vec3(-d.Length / 2, 0, 0)));
        bar.AddVisualCylinder(r, thickness * 1.3, color, new Frame(new Vec3(d.Length / 2, 0, 0)));
        return sys.Add(bar);
    }

    /// <summary>Intersection of two circles in the XY plane (upper solution when <paramref name="upper"/>).</summary>
    public static Vec3 CircleIntersection(Vec3 c1, double r1, Vec3 c2, double r2, bool upper = true)
    {
        double d = (c2 - c1).Length;
        double a = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
        double h = Math.Sqrt(Math.Max(0, r1 * r1 - a * a));
        var p = c1 + (c2 - c1) * (a / d);
        double sx = -(c2.Y - c1.Y) / d * h, sy = (c2.X - c1.X) / d * h;
        return upper ? new Vec3(p.X + sx, p.Y + sy, 0) : new Vec3(p.X - sx, p.Y - sy, 0);
    }

    public static readonly Color[] Palette =
    [
        Color.FromHex("#F28C28"), Color.FromHex("#18B6A3"), Color.FromHex("#3B82F6"), Color.FromHex("#EF4444"),
        Color.FromHex("#FACC15"), Color.FromHex("#8B5CF6"), Color.FromHex("#22C55E"), Color.FromHex("#EC4899"),
    ];
}
