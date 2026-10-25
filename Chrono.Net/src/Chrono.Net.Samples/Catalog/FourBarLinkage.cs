namespace Chrono.Net.Samples.Catalog;

public sealed class FourBarLinkage : Sample
{
    public override string Id => "four-bar";
    public override string Title => "Four-Bar Linkage";
    public override string Category => "Mechanisms";
    public override string Description => "A crank-rocker four-bar mechanism driven by a constant-speed motor. Closed kinematic loop solved by Chrono's constraint solver.";
    public override string DescriptionId => "Mekanisme empat batang crank-rocker yang digerakkan motor berkecepatan konstan. Loop kinematik tertutup diselesaikan oleh solver constraint Chrono.";
    public override string[] Tags => ["closed loop", "rotation motor", "revolute joint", "assembly"];
    public override double TimeStep => 0.002;

    public override SampleRun Create()
    {
        var sys = new ChSystem();
        var anchor = Build.Anchor(sys);
        var O = new Vec3(0, 1, 0);       // crank pivot
        var D = new Vec3(1.6, 1, 0);     // rocker pivot
        const double crank = 0.4, coupler = 1.6, rocker = 1.0;
        var A = O + new Vec3(0, crank, 0);
        var B = Build.CircleIntersection(A, coupler, D, rocker);

        anchor.AddVisualBox(new Vec3(2.4, 0.1, 0.3), Color.Slate, new Frame(new Vec3(0.8, 0.55, 0)));
        anchor.AddVisualCylinder(0.07, 0.4, Color.Gray, new Frame(O));
        anchor.AddVisualCylinder(0.07, 0.4, Color.Gray, new Frame(D));

        var crankBar = Build.Bar(sys, O, A, 0.08, Color.Orange).Named("crank");
        var couplerBar = Build.Bar(sys, A, B, 0.07, Color.Teal).Named("coupler");
        var rockerBar = Build.Bar(sys, D, B, 0.08, Color.Blue).Named("rocker");

        var motor = sys.Add(ChMotor.RotationSpeed(crankBar, anchor, O, Vec3.UnitZ, Math.PI));
        sys.Add(ChJoint.Revolute(couplerBar, crankBar, A, Vec3.UnitZ));
        sys.Add(ChJoint.Revolute(rockerBar, couplerBar, B, Vec3.UnitZ));
        sys.Add(ChJoint.Revolute(rockerBar, anchor, D, Vec3.UnitZ));
        sys.DoAssembly();

        return new SampleRun(sys) { Camera = new(new Vec3(0.8, 1.3, 0), 4.5, 0, 10) }
            .Probe("Crank angle", "rad", () => motor.Value % (2 * Math.PI))
            .Probe("Rocker angle", "rad", () => Math.Atan2(rockerBar.Position.Y - D.Y, rockerBar.Position.X - D.X))
            .Probe("Motor torque", "N·m", () => motor.Effort);
    }
}
