namespace Chrono.Net.Samples.Catalog;

public sealed class RobotArm : Sample
{
    public override string Id => "robot-arm";
    public override string Title => "3-DOF Robot Arm";
    public override string Category => "Robotics";
    public override string Description => "A turntable, shoulder and elbow driven by angle motors follow smooth trajectories while the arm knocks over a stack of crates. Joint torques are plotted live.";
    public override string DescriptionId => "Meja putar, bahu dan siku yang digerakkan motor sudut mengikuti lintasan halus sambil lengan menjatuhkan tumpukan peti. Torsi sendi ditampilkan secara langsung.";
    public override string[] Tags => ["angle motor", "trajectory", "inverse dynamics", "robotics"];

    public override SampleRun Create()
    {
        var sys = new ChSystem();
        var mat = sys.Own(new ContactMaterial { Friction = 0.6f });
        var ground = Build.Ground(sys, mat, 10);
        var steel = Color.FromHex("#CBD5E1");

        var basePos = new Vec3(0, 0.15, 0);
        sys.Add(ChBody.CreateCylinder(Axis.Y, 0.35, 0.3, 4000, mat).At(basePos).Fixed().Colored(Color.Slate));

        var turret = sys.Add(ChBody.CreateCylinder(Axis.Y, 0.25, 0.3, 2000, collide: false).At(0, 0.45, 0).Colored(Color.Orange).Named("turret"));
        var shoulder = new Vec3(0, 0.6, 0);
        var elbow = shoulder + new Vec3(0, 1.0, 0);
        var wrist = elbow + new Vec3(0.9, 0, 0);
        var upper = Build.Bar(sys, shoulder, elbow, 0.14, steel).Named("upper-arm");
        var fore = Build.Bar(sys, elbow, wrist, 0.11, Color.Orange).Named("forearm");
        fore.AddVisualBox(new Vec3(0.12, 0.25, 0.2), Color.Slate, new Frame(new Vec3(0.45, 0, 0)));
        fore.AddCollisionBox(new Vec3(0.9, 0.11, 0.11), material: mat);
        fore.AddCollisionBox(new Vec3(0.12, 0.25, 0.2), new Frame(new Vec3(0.45, 0, 0)), mat);
        fore.CollisionEnabled = true;

        var anchor = ground;
        // Angle motors are relative to the start pose, so every law starts at 0 (no jerk at t = 0).
        // Base yaw sweeps ±70°, shoulder leans forward, elbow folds down — smooth laws.
        var yaw = sys.Add(ChMotor.RotationAngle(turret, anchor, new Vec3(0, 0.3, 0), Vec3.UnitY, ChFunction.Sine(1.2, 0.12)));
        var sh = sys.Add(ChMotor.RotationAngle(upper, turret, shoulder, Vec3.UnitZ,
            ChFunction.FromDelegate(t => -0.5 * (1 - Math.Cos(0.6 * t)))));
        var el = sys.Add(ChMotor.RotationAngle(fore, upper, elbow, Vec3.UnitZ,
            ChFunction.FromDelegate(t => -0.7 * (1 - Math.Cos(0.9 * t)))));

        // Crates to push around.
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3 - i; j++)
            sys.Add(ChBody.CreateBox(new(0.25, 0.25, 0.25), 300, mat).At(1.1, 0.125 + i * 0.25, -0.3 + j * 0.27 + i * 0.13)
                .Colored(Build.Palette[(i + j) % 4 + 1]));

        return new SampleRun(sys) { Camera = new(new Vec3(0.3, 0.9, 0), 5, 50, 25) }
            .Probe("Base torque", "N·m", () => yaw.Effort)
            .Probe("Shoulder torque", "N·m", () => sh.Effort)
            .Probe("Elbow torque", "N·m", () => el.Effort);
    }
}
