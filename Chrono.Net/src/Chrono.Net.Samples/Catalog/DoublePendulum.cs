namespace Chrono.Net.Samples.Catalog;

public sealed class DoublePendulum : Sample
{
    public override string Id => "double-pendulum";
    public override string Title => "Chaotic Double Pendulum";
    public override string Category => "Basics";
    public override string Description => "Two rigid links connected by revolute joints — a classic chaotic system. Watch energy flow between the arms.";
    public override string DescriptionId => "Dua batang kaku yang dihubungkan sendi revolute — sistem chaos klasik. Perhatikan energi berpindah antar lengan.";
    public override string[] Tags => ["revolute joint", "rigid body", "chaos"];
    public override double TimeStep => 0.001;
    public override double Duration => 20;

    public override SampleRun Create()
    {
        var sys = new ChSystem();
        sys.SetTimestepper(TimestepperType.EulerImplicitLinearized);
        var anchor = Build.Anchor(sys);
        anchor.AddVisualCylinder(0.06, 0.2, Color.Slate);

        var pivot = new Vec3(0, 2.2, 0);
        // Start nearly upside-down so the motion is wild from the first second.
        var a = pivot + Quat.FromAngleZ(2.4).Rotate(new Vec3(1, 0, 0));
        var b = a + Quat.FromAngleZ(2.9).Rotate(new Vec3(0.8, 0, 0));

        var arm1 = Build.Bar(sys, pivot, a, 0.08, Color.Orange).Named("arm1");
        var arm2 = Build.Bar(sys, a, b, 0.08, Color.Teal).Named("arm2");
        arm2.AddVisualSphere(0.12, Color.Yellow, new Frame(new Vec3(0.4, 0, 0)));

        var j1 = sys.Add(ChJoint.Revolute(arm1, anchor, pivot, Vec3.UnitZ));
        var j2 = sys.Add(ChJoint.Revolute(arm2, arm1, a, Vec3.UnitZ));

        return new SampleRun(sys) { Camera = new(new Vec3(0, 1.2, 0), 7, 0, 8) }
            .Probe("Tip height", "m", () => arm2.Position.Y)
            .Probe("Kinetic energy", "J", () => sys.KineticEnergy)
            .Probe("Joint 1 reaction", "N", () => j1.Reaction.Force.Length);
    }
}
