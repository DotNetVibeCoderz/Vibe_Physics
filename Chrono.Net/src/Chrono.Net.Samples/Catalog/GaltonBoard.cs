namespace Chrono.Net.Samples.Catalog;

public sealed class GaltonBoard : Sample
{
    public override string Id => "galton-board";
    public override string Title => "Galton Board";
    public override string Category => "Contacts";
    public override string Description => "Balls bounce through a lattice of pegs and pile up into a bell curve — the central limit theorem, simulated.";
    public override string DescriptionId => "Bola memantul melewati kisi pasak dan menumpuk membentuk kurva lonceng — teorema limit pusat, disimulasikan.";
    public override string[] Tags => ["contacts", "restitution", "statistics"];
    public override double TimeStep => 0.003;
    public override double Duration => 20;

    public override SampleRun Create()
    {
        var sys = new ChSystem();
        sys.SetSolver(SolverType.Psor, 25);
        // Friction on pegs damps the sideways velocity a deflected ball would otherwise keep (which makes balls drift
        // to the walls instead of random-walking). The glass and hopper are slick (Chrono combines friction by
        // minimum), so balls never drag on the panes or stall in the hopper.
        var mat = sys.Own(new ContactMaterial { Friction = 0.5f, Restitution = 0.05f });
        var slick = sys.Own(new ContactMaterial { Friction = 0f, Restitution = 0.05f });
        var dark = Color.FromHex("#1E293B");

        const double depth = 0.16, height = 7.8, cy = height / 2 - 0.1;
        // Back plate and invisible front glass keep the balls in a thin slab.
        sys.Add(ChBody.CreateBox(new(4.4, height, 0.1), 1000, slick).At(0, cy, -depth / 2 - 0.05).Fixed().Colored(dark));
        sys.Add(ChBody.CreateBox(new(4.4, height, 0.1), 1000, slick, visual: false).At(0, cy, depth / 2 + 0.05).Fixed());
        sys.Add(ChBody.CreateBox(new(4.4, 0.2, 0.5), 1000, mat).At(0, -0.1, 0).Fixed().Colored(Color.Slate).Named("ground"));
        sys.Add(ChBody.CreateBox(new(0.1, height, 0.5), 1000, mat).At(-2.2, cy, 0).Fixed().Colored(Color.Slate));
        sys.Add(ChBody.CreateBox(new(0.1, height, 0.5), 1000, mat).At(2.2, cy, 0).Fixed().Colored(Color.Slate));

        // Hopper: two planks from the side walls down to a throat three balls wide, so nothing spills around it.
        const double throat = 0.3, slope = 0.45, span = 2.15 - throat / 2, throatY = 5.15;
        double plank = span / Math.Cos(slope), plankY = throatY + span / 2 * Math.Tan(slope);
        sys.Add(ChBody.CreateBox(new(plank, 0.06, depth), 1000, slick).At(-(throat + span) / 2, plankY, 0).Rotated(Quat.FromAngleZ(-slope)).Fixed().Colored(Color.Gray));
        sys.Add(ChBody.CreateBox(new(plank, 0.06, depth), 1000, slick).At((throat + span) / 2, plankY, 0).Rotated(Quat.FromAngleZ(slope)).Fixed().Colored(Color.Gray));

        // Peg lattice, wall to wall with alternating offsets (a triangle would let balls roll down its outside).
        // Peg radius closes the diagonal corridors (0.172 - 2r < ball Ø 0.1, else balls fly straight through them)
        // while the gap to each diagonal neighbour (0.197 - 2r) still lets a ball pass.
        const double pitch = 0.2, rowGap = 0.17, pegRadius = 0.04, binTop = 2.8;
        for (int row = 0; row < 12; row++)
        {
            double offset = row % 2 == 0 ? 0 : pitch / 2;
            for (int k = -10; k <= 10; k++)
            {
                double x = k * pitch + offset;
                if (Math.Abs(x) > 1.95) continue; // keep a ball-sized gap to the side walls
                sys.Add(ChBody.CreateCylinder(Axis.Z, pegRadius, depth, 1000, mat).At(x, 4.85 - row * rowGap, 0).Fixed().Colored(Color.Yellow));
            }
        }
        // Bin walls sit under the last row's pegs (odd row → offset), so balls drop into bins, not onto wall tops.
        // They reach up to just below the lattice so balls cannot hop between bins.
        for (int i = -10; i <= 9; i++)
            sys.Add(ChBody.CreateBox(new(0.03, binTop, depth), 1000, mat).At((i + 0.5) * pitch, binTop / 2, 0).Fixed().Colored(Color.Gray));

        // Balls start in a loose grid inside the hopper (jittered so the stack does not stay symmetric).
        var balls = new List<ChBody>();
        var rng = new Random(3);
        for (int i = 0; i < 160; i++)
        {
            int col = i % 20, row = i / 20;
            double x = (col - 9.5) * 0.17 + (rng.NextDouble() - 0.5) * 0.03;
            double floor = throatY + (Math.Abs(x) - throat / 2 + 0.1) * Math.Tan(slope);
            var b = ChBody.CreateSphere(0.05, 2000, mat).At(x, Math.Max(floor, throatY) + 0.12 + row * 0.13, 0).Colored(Color.Teal);
            sys.Add(b);
            balls.Add(b);
        }

        return new SampleRun(sys) { Camera = new(new Vec3(0, 3.7, 0), 10, 0, 4) }
            .Probe("Balls landed", "", () => balls.Count(b => b.Position.Y < binTop))
            .Probe("Spread (σ)", "m", () =>
            {
                var xs = balls.Where(b => b.Position.Y < binTop).Select(b => b.Position.X).ToArray();
                if (xs.Length < 2) return 0;
                double mean = xs.Average();
                return Math.Sqrt(xs.Sum(x => (x - mean) * (x - mean)) / xs.Length);
            });
    }
}
