namespace Chrono.Net.Samples.Catalog;

public sealed class GearTrain : Sample
{
    public override string Id => "gear-train";
    public override string Title => "Gear Train";
    public override string Category => "Mechanisms";
    public override string Description => "Three spur gears coupled by gear constraints. The speed ratio follows the radii; the middle idler reverses direction.";
    public override string DescriptionId => "Tiga roda gigi lurus yang dikopel constraint gear. Rasio kecepatan mengikuti jari-jari; roda gigi tengah membalik arah putaran.";
    public override string[] Tags => ["gear constraint", "rotation motor", "transmission ratio"];

    public override SampleRun Create()
    {
        var sys = new ChSystem();
        var anchor = Build.Anchor(sys);
        anchor.AddVisualBox(new Vec3(3.6, 1.6, 0.05), Color.FromHex("#1E293B"), new Frame(new Vec3(0.9, 1, -0.15)));

        (double R, Vec3 C, Color Col)[] gears =
        [
            (0.5, new Vec3(0, 1, 0), Color.Orange),
            (0.3, new Vec3(0.8, 1, 0), Color.Teal),
            (0.7, new Vec3(1.8, 1, 0), Color.Blue),
        ];
        var bodies = new List<ChBody>();
        foreach (var (R, C, col) in gears)
        {
            var g = ChBody.CreateCylinder(Axis.Z, R, 0.12, 2000, collide: false).At(C).Colored(col);
            // Teeth and a spoke so rotation is visible.
            int teeth = (int)(R * 40);
            for (int t = 0; t < teeth; t++)
            {
                var q = Quat.FromAngleZ(2 * Math.PI * t / teeth);
                g.AddVisualBox(new Vec3(0.07, 0.05, 0.12), col, new Frame(q.Rotate(new Vec3(R + 0.02, 0, 0)), q));
            }
            g.AddVisualBox(new Vec3(R * 1.6, 0.06, 0.14), Color.White, new Frame(new Vec3(0, 0, 0.01)));
            sys.Add(g);
            sys.Add(ChJoint.Revolute(g, anchor, C, Vec3.UnitZ));
            bodies.Add(g);
        }

        var motor = sys.Add(ChMotor.RotationSpeed(bodies[0], anchor, gears[0].C, Vec3.UnitZ, 1.5));
        sys.Add(new ChGear(bodies[0], bodies[1], gears[0].R / gears[1].R, Frame.Identity, Frame.Identity));
        sys.Add(new ChGear(bodies[1], bodies[2], gears[1].R / gears[2].R, Frame.Identity, Frame.Identity));

        return new SampleRun(sys) { Camera = new(new Vec3(0.9, 1, 0), 4.2, 0, 5) }
            .Probe("Driver ω", "rad/s", () => bodies[0].AngularVelocity.Z)
            .Probe("Idler ω", "rad/s", () => bodies[1].AngularVelocity.Z)
            .Probe("Output ω", "rad/s", () => bodies[2].AngularVelocity.Z);
    }
}
