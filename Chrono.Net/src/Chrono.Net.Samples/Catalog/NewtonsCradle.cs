namespace Chrono.Net.Samples.Catalog;

public sealed class NewtonsCradle : Sample
{
    public override string Id => "newtons-cradle";
    public override string Title => "Newton's Cradle";
    public override string Category => "Basics";
    public override string Description => "Five steel balls on pendulums exchange momentum through near-elastic impacts (NSC contact with restitution).";
    public override string DescriptionId => "Lima bola baja pada bandul saling bertukar momentum melalui tumbukan hampir elastis (kontak NSC dengan restitusi).";
    public override string[] Tags => ["NSC contact", "restitution", "revolute joint"];
    public override double TimeStep => 0.0005;

    public override SampleRun Create()
    {
        var sys = new ChSystem();
        sys.MaxPenetrationRecoverySpeed = 0.1;
        sys.SetSolver(SolverType.Psor, 200);
        var steel = sys.Own(new ContactMaterial { Friction = 0, Restitution = 1.0f });
        var anchor = Build.Anchor(sys);

        const double r = 0.15, len = 1.2, top = 2.0;
        var frame = Color.FromHex("#475569");
        anchor.AddVisualBox(new Vec3(1.8, 0.06, 0.06), frame, new Frame(new Vec3(0, top, 0.3)));
        anchor.AddVisualBox(new Vec3(1.8, 0.06, 0.06), frame, new Frame(new Vec3(0, top, -0.3)));

        var balls = new List<ChBody>();
        for (int i = 0; i < 5; i++)
        {
            double x = (i - 2) * (2 * r + 1e-4);
            var pivot = new Vec3(x, top, 0);
            var ball = ChBody.CreateSphere(r, 7800, steel).Named($"ball{i}");
            ball.SetColor(new Color(0.78f, 0.80f, 0.84f));
            // Strings drawn as thin boxes on the ball body (visual only).
            ball.AddVisualBox(new Vec3(0.01, len, 0.01), Color.FromHex("#94A3B8"), new Frame(new Vec3(0, len / 2, 0)));
            ball.Position = pivot - new Vec3(0, len, 0);
            if (i == 0)
            {
                // Lift the first ball by 50°.
                var q = Quat.FromAngleZ(-50 * Math.PI / 180);
                ball.Position = pivot + q.Rotate(new Vec3(0, -len, 0));
                ball.Rotation = q;
            }
            sys.Add(ball);
            sys.Add(ChJoint.Revolute(ball, anchor, pivot, Vec3.UnitZ));
            balls.Add(ball);
        }

        return new SampleRun(sys) { Camera = new(new Vec3(0, 1.2, 0), 4.5, 15, 10) }
            .Probe("Left ball x", "m", () => balls[0].Position.X)
            .Probe("Right ball x", "m", () => balls[4].Position.X)
            .Probe("Kinetic energy", "J", () => sys.KineticEnergy);
    }
}
