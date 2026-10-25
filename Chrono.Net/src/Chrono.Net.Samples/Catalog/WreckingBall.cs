namespace Chrono.Net.Samples.Catalog;

public sealed class WreckingBall : Sample
{
    public override string Id => "wrecking-ball";
    public override string Title => "Wrecking Ball vs. Brick Wall";
    public override string Category => "Contacts";
    public override string Description => "A heavy ball on a distance constraint swings into a 76-brick wall. Shows stacking stability and impact with NSC contacts.";
    public override string DescriptionId => "Bola berat pada constraint jarak berayun menghantam dinding 76 bata. Menunjukkan stabilitas tumpukan dan tumbukan dengan kontak NSC.";
    public override string[] Tags => ["distance constraint", "stacking", "impact", "many bodies"];
    public override double TimeStep => 0.003;

    public override SampleRun Create()
    {
        var sys = new ChSystem();
        sys.SetSolver(SolverType.Psor, 60);
        sys.MaxPenetrationRecoverySpeed = 0.05;
        var mat = sys.Own(new ContactMaterial { Friction = 0.6f });
        Build.Ground(sys, mat, 20);

        const double bx = 0.6, by = 0.3, bz = 0.3;
        var brickColors = new[] { Color.FromHex("#B45309"), Color.FromHex("#C2410C"), Color.FromHex("#9A3412") };
        for (int row = 0; row < 8; row++)
        for (int col = 0; col < 10; col++)
        {
            double offset = row % 2 == 0 ? 0 : bx / 2;
            double x = (col - 5) * bx + offset;
            if (row % 2 == 1 && col == 9) continue;
            sys.Add(ChBody.CreateBox(new(bx * 0.98, by, bz), 1800, mat)
                .At(x, by / 2 + 0.005 + row * (by + 0.003), 0).Colored(brickColors[(row + col) % 3]));
        }

        var anchor = Build.Anchor(sys);
        var pivot = new Vec3(0, 6, -1);
        anchor.AddVisualBox(new Vec3(0.3, 0.3, 0.3), Color.Slate, new Frame(pivot));
        var ball = sys.Add(ChBody.CreateSphere(0.6, 7800, mat).At(0, 3.6, -5.2).Colored(new Color(0.25f, 0.27f, 0.3f)).Named("ball"));
        var chain = sys.Add(new ChDistanceConstraint(ball, anchor, ball.Position, pivot));

        // Distance constraints have no geometry: draw the chain as a line from the pivot to the ball.
        var cable = new Chrono.Net.Rendering.RenderLine { Color = new(0.6f, 0.65f, 0.72f), Width = 2.5f };
        void UpdateCable(Chrono.Net.Rendering.RenderWorld _) => cable.Points = [pivot.ToVector3(), ball.Position.ToVector3()];

        return new SampleRun(sys)
            {
                Camera = new(new Vec3(0, 2, 0), 12, 55, 20),
                DecorateWorld = w => { w.Lines.Add(cable); UpdateCable(w); },
                UpdateWorld = UpdateCable,
            }
            .Probe("Ball speed", "m/s", () => ball.LinearVelocity.Length)
            .Probe("Chain tension", "N", () => chain.Reaction.Force.Length)
            .Probe("Contacts", "", () => sys.ContactCount);
    }
}
