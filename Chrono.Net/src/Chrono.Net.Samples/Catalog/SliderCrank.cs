namespace Chrono.Net.Samples.Catalog;

public sealed class SliderCrank : Sample
{
    public override string Id => "slider-crank";
    public override string Title => "Engine Slider-Crank";
    public override string Category => "Mechanisms";
    public override string Description => "Crank, connecting rod and piston on a prismatic guide — the heart of a combustion engine. Plots piston position and the motor torque needed.";
    public override string DescriptionId => "Engkol, batang penghubung dan piston pada pemandu prismatik — jantung mesin pembakaran. Menampilkan posisi piston dan torsi motor yang diperlukan.";
    public override string[] Tags => ["prismatic joint", "rotation motor", "reaction forces"];

    public override SampleRun Create()
    {
        var sys = new ChSystem();
        var anchor = Build.Anchor(sys);
        const double r = 0.3, L = 1.0;
        var O = new Vec3(0, 1, 0);
        var A = O + new Vec3(r, 0, 0);
        var P = O + new Vec3(r + L, 0, 0);

        anchor.AddVisualBox(new Vec3(1.1, 0.05, 0.4), Color.FromHex("#475569"), new Frame(new Vec3(r + L, 0.82, 0)));
        anchor.AddVisualBox(new Vec3(1.1, 0.05, 0.4), Color.FromHex("#475569"), new Frame(new Vec3(r + L, 1.18, 0)));
        anchor.AddVisualCylinder(0.35, 0.12, Color.Slate, new Frame(O + new Vec3(0, 0, -0.12)));

        var crank = Build.Bar(sys, O, A, 0.1, Color.Orange).Named("crank");
        crank.AddVisualCylinder(0.22, 0.08, Color.FromHex("#B45309"), new Frame(new Vec3(-r / 2, 0, -0.05)));
        var rod = Build.Bar(sys, A, P, 0.07, Color.Teal).Named("rod");
        var piston = sys.Add(ChBody.CreateBox(new(0.3, 0.3, 0.3), 2700, collide: false).At(P).Colored(Color.Gray).Named("piston"));

        var motor = sys.Add(ChMotor.RotationSpeed(crank, anchor, O, Vec3.UnitZ, 2 * Math.PI));
        sys.Add(ChJoint.Revolute(rod, crank, A, Vec3.UnitZ));
        sys.Add(ChJoint.Revolute(piston, rod, P, Vec3.UnitZ));
        sys.Add(ChJoint.Prismatic(piston, anchor, P, Vec3.UnitX));

        return new SampleRun(sys) { Camera = new(new Vec3(0.7, 1, 0), 3.6, 10, 18) }
            .Probe("Piston x", "m", () => piston.Position.X)
            .Probe("Piston speed", "m/s", () => piston.LinearVelocity.X)
            .Probe("Motor torque", "N·m", () => motor.Effort);
    }
}
