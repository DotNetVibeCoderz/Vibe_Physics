namespace Chrono.Net.Samples.Catalog;

public sealed class Mixer : Sample
{
    public override string Id => "mixer";
    public override string Title => "Industrial Mixer";
    public override string Category => "Contacts";
    public override string Description => "A motor-driven paddle stirs 240 spheres, boxes and cylinders inside a container — the classic Chrono collision demo.";
    public override string DescriptionId => "Pengaduk bermotor mengaduk 240 bola, kotak dan silinder di dalam wadah — demo tumbukan klasik Chrono.";
    public override string[] Tags => ["Bullet collision", "rotation motor", "mixed shapes", "many bodies"];
    public override double TimeStep => 0.003;

    public override SampleRun Create()
    {
        var sys = new ChSystem();
        sys.SetSolver(SolverType.Psor, 50);
        var mat = sys.Own(new ContactMaterial { Friction = 0.4f });
        var wall = Color.FromHex("#334155");

        var floor = sys.Add(ChBody.CreateBox(new(6, 0.4, 6), 1000, mat).At(0, -0.2, 0).Fixed().Colored(Color.FromHex("#1E293B")));
        foreach (var (pos, size) in new (Vec3, Vec3)[]
        {
            (new(-3, 1, 0), new(0.2, 2, 6)), (new(3, 1, 0), new(0.2, 2, 6)),
            (new(0, 1, -3), new(6, 2, 0.2)),
        })
            sys.Add(ChBody.CreateBox(size, 1000, mat).At(pos).Fixed().Colored(wall));
        // Front wall is collision-only so the camera can look inside.
        sys.Add(ChBody.CreateBox(new(6, 2, 0.2), 1000, mat, visual: false).At(0, 1, 3).Fixed());

        var paddle = sys.Add(ChBody.CreateBox(new(4.6, 0.8, 0.25), 3000, mat).At(0, 0.45, 0).Colored(Color.Orange).Named("paddle"));
        var motor = sys.Add(ChMotor.RotationSpeed(paddle, floor, new Vec3(0, 0.45, 0), Vec3.UnitY, Math.PI / 3));

        var rng = new Random(7);
        for (int i = 0; i < 240; i++)
        {
            var pos = new Vec3(rng.NextDouble() * 5 - 2.5, 1.5 + i * 0.03, rng.NextDouble() * 5 - 2.5);
            var color = Build.Palette[i % Build.Palette.Length];
            ChBody body = (i % 3) switch
            {
                0 => ChBody.CreateSphere(0.15 + rng.NextDouble() * 0.08, 1000, mat),
                1 => ChBody.CreateBox(new(0.3, 0.2, 0.25), 800, mat),
                _ => ChBody.CreateCylinder(Axis.Y, 0.12, 0.3, 900, mat),
            };
            sys.Add(body.At(pos).Colored(color));
        }

        return new SampleRun(sys) { Camera = new(new Vec3(0, 0.5, 0), 9, 20, 40) }
            .Probe("Paddle torque", "N·m", () => motor.Effort)
            .Probe("Motor power", "W", () => motor.Power)
            .Probe("Contacts", "", () => sys.ContactCount);
    }
}
