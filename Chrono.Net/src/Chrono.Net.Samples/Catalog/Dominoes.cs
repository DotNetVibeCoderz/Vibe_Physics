namespace Chrono.Net.Samples.Catalog;

public sealed class Dominoes : Sample
{
    public override string Id => "dominoes";
    public override string Title => "Domino Spiral";
    public override string Category => "Contacts";
    public override string Description => "Eighty dominoes on a spiral path. Friction and non-smooth contacts propagate the toppling wave.";
    public override string DescriptionId => "Delapan puluh domino pada lintasan spiral. Gesekan dan kontak non-smooth merambatkan gelombang jatuhnya domino.";
    public override string[] Tags => ["NSC contact", "friction", "many bodies"];
    public override double TimeStep => 0.002;
    public override double Duration => 12;

    public override SampleRun Create()
    {
        var sys = new ChSystem();
        sys.SetSolver(SolverType.Psor, 60);
        // Bodies spawned in (margin-level) contact must not be popped upward by penetration recovery.
        sys.MaxPenetrationRecoverySpeed = 0.05;
        var mat = sys.Own(new ContactMaterial { Friction = 0.5f });
        Build.Ground(sys, mat, 12);

        const double h = 0.4, w = 0.2, t = 0.05, gap = 0.18;
        var dominoes = new List<ChBody>();
        double angle = 0, radius = 3.2;
        for (int i = 0; i < 80; i++)
        {
            // Spawn 1 cm above the floor: they settle in the first frames instead of starting in collision margin.
            var pos = new Vec3(radius * Math.Cos(angle), h / 2 + 0.01, radius * Math.Sin(angle));
            // Domino faces along the path tangent.
            var rot = Quat.FromAngleY(-angle);
            var d = ChBody.CreateBox(new(w, h, t), 700, mat).At(pos).Rotated(rot)
                .Colored(Build.Palette[i % Build.Palette.Length]);
            sys.Add(d);
            dominoes.Add(d);
            double step = gap / radius;
            angle += step;
            radius = Math.Max(0.9, radius - 0.03);
        }
        // A finger push: a short force on the top of the first domino along the path tangent (+Z).
        var lead = dominoes[0];
        sys.BeforeStep += (s, dt) =>
        {
            lead.ClearForces();
            if (s.Time is > 0.3 and < 0.5)
                lead.AddForce(new Vec3(0, 0, 9), point: new Vec3(0, h / 2, 0), local: true);
        };

        int Fallen() => dominoes.Count(d => d.Rotation.AxisY.Y < 0.7);
        return new SampleRun(sys) { Camera = new(new Vec3(0, 0, 0), 9, 35, 45) }
            .Probe("Fallen dominoes", "", () => Fallen())
            .Probe("Lead tilt", "deg", () => Math.Acos(Math.Clamp(dominoes[0].Rotation.AxisY.Y, -1, 1)) * 180 / Math.PI)
            .Probe("Contacts", "", () => sys.ContactCount)
            .Probe("Kinetic energy", "J", () => sys.KineticEnergy);
    }
}
