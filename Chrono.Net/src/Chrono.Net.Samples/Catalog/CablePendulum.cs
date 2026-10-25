using Chrono.Net.Fea;

namespace Chrono.Net.Samples.Catalog;

public sealed class CablePendulum : Sample
{
    public override string Id => "fea-cable";
    public override string Title => "Heavy Rope Pendulum (ANCF)";
    public override string Category => "FEA";
    public override string Description => "A flexible ANCF cable released horizontally swings a steel ball — large deformation and large rotation of a flexible body.";
    public override string DescriptionId => "Kabel ANCF fleksibel yang dilepas horizontal mengayunkan bola baja — deformasi besar dan rotasi besar pada benda fleksibel.";
    public override string[] Tags => ["FEA", "ANCF cable", "large deformation"];
    public override double TimeStep => 0.002;

    public override SampleRun Create()
    {
        var sys = new ChSystem(ContactMethod.Smc);
        sys.SetSolver(SolverType.SparseQr);
        sys.SetTimestepper(TimestepperType.EulerImplicitLinearized);

        var support = sys.Add(ChBody.CreateBox(new(0.3, 0.1, 0.3), 1000, collide: false).At(0, 3, 0).Fixed().Colored(Color.Slate));
        var mesh = sys.Add(new FeaMesh());
        var nodes = mesh.BuildCable(new Vec3(0, 3, 0), new Vec3(2.2, 3, 0), 24, new CableSection(0.02, 2e8, 1500, 0.01));
        mesh.FixNode(nodes.Start.Value);
        int end = nodes.End.Value - 1;

        var ball = sys.Add(ChBody.CreateSphere(0.12, 7800, collide: false).At(2.2, 3, 0).Colored(Color.Teal));
        mesh.AttachNode(end, ball);

        return new SampleRun(sys) { Camera = new(new Vec3(0, 1.8, 0), 6.5, 10, 8) }
            .Probe("Ball height", "m", () => ball.Position.Y)
            .Probe("Ball speed", "m/s", () => ball.LinearVelocity.Length);
    }
}
