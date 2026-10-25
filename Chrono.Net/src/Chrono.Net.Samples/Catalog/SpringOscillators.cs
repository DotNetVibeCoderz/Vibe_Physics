namespace Chrono.Net.Samples.Catalog;

public sealed class SpringOscillators : Sample
{
    public override string Id => "spring-oscillators";
    public override string Title => "Damping Comparison";
    public override string Category => "Basics";
    public override string Description => "Three identical masses on spring-dampers: under-damped, critically damped and over-damped — plus one with a custom nonlinear force law written in C#.";
    public override string DescriptionId => "Tiga massa identik pada pegas-peredam: kurang teredam, teredam kritis, dan teredam berlebih — ditambah satu dengan hukum gaya nonlinier kustom yang ditulis di C#.";
    public override string[] Tags => ["TSDA spring", "prismatic joint", "custom force law"];
    public override double TimeStep => 0.001;

    public override SampleRun Create()
    {
        var sys = new ChSystem();
        var anchor = Build.Anchor(sys);
        anchor.AddVisualBox(new Vec3(4.2, 0.08, 0.5), Color.Slate, new Frame(new Vec3(0, 3, 0)));

        const double m = 2, k = 200;
        double critical = 2 * Math.Sqrt(k * m);
        (string Name, double Damping, Color Color)[] cases =
        [
            ("Under-damped", 0.1 * critical, Color.Orange),
            ("Critical", critical, Color.Teal),
            ("Over-damped", 3 * critical, Color.Blue),
            ("Nonlinear", 0, Color.Purple),
        ];

        var run = new SampleRun(sys) { Camera = new(new Vec3(0, 1.8, 0), 6.5, 0, 6) };
        for (int i = 0; i < cases.Length; i++)
        {
            var (name, c, color) = cases[i];
            double x = -1.5 + i;
            var mass = sys.Add(ChBody.CreateBox(new(0.35, 0.35, 0.35), 1000, collide: false).At(x, 0.9, 0).Colored(color).Named(name));
            mass.Mass = m;
            sys.Add(ChJoint.Prismatic(mass, anchor, mass.Position, Vec3.UnitY));
            var spring = sys.Add(new ChSpring(mass, anchor, mass.Position + new Vec3(0, 0.175, 0), new Vec3(x, 3, 0), k, c, restLength: 1.2));
            if (name == "Nonlinear")
            {
                // Hardening spring with quadratic drag: F = -k Δ - 400 Δ³ - 4 v|v|
                spring.ForceLaw = (t, rest, len, v) =>
                {
                    double d = len - rest;
                    return -(k * d + 400 * d * d * d) - 4 * v * Math.Abs(v);
                };
            }
            run.Probe(name, "m", () => mass.Position.Y);
        }
        return run;
    }
}
